using Microsoft.Extensions.Logging;
using VoterVerification.Helpers;
using VoterVerification.Models;
using VoterVerification.Services;

namespace VoterVerification.ViewModels;

public partial class VoterDetailViewModel : ViewModelBase, IQueryAttributable
{
    private readonly IVoterDataService _data;
    private string? _requestedKey;
    private bool _hasExistingRow;
    private string? _etag;
    private bool _wasVerified;
    private DateTimeOffset? _previousVerifiedAtUtc;

    public VoterDetailViewModel(IVoterDataService data, IConnectivityService connectivity, ILogger<VoterDetailViewModel> logger)
        : base(connectivity, logger)
    {
        _data = data;
    }

    public string WardId { get; private set; } = "";

    public string SerialNumber { get; private set; } = "";

    [ObservableProperty]
    private string title = "Voter";

    [ObservableProperty]
    private bool isLoaded;

    [ObservableProperty]
    private bool notFound;

    [ObservableProperty]
    private string nameTelugu = "—";

    [ObservableProperty]
    private string nameEnglish = "—";

    [ObservableProperty]
    private string relativeTelugu = "—";

    [ObservableProperty]
    private string relativeEnglish = "—";

    [ObservableProperty]
    private string epicNumber = "—";

    [ObservableProperty]
    private string assemblyRoll = "—";

    [ObservableProperty]
    private string sectionNumber = "—";

    [ObservableProperty]
    private string age = "—";

    [ObservableProperty]
    private string gender = "—";

    [ObservableProperty]
    private string houseNumber = "—";

    [ObservableProperty]
    private string phoneNumber = "";

    [ObservableProperty]
    private string? phoneError;

    [ObservableProperty]
    private bool isVerified;

    [ObservableProperty]
    private string recordedBy = "";

    [ObservableProperty]
    private string verifiedAt = "";

    [ObservableProperty]
    private string updatedAt = "";

    public bool HasPhoneError => !string.IsNullOrWhiteSpace(PhoneError);

    public bool HasRecordedBy => !string.IsNullOrWhiteSpace(RecordedBy);

    public bool HasVerifiedAt => !string.IsNullOrWhiteSpace(VerifiedAt);

    public bool HasUpdatedAt => !string.IsNullOrWhiteSpace(UpdatedAt);

    partial void OnPhoneErrorChanged(string? value) => OnPropertyChanged(nameof(HasPhoneError));

    partial void OnRecordedByChanged(string value) => OnPropertyChanged(nameof(HasRecordedBy));

    partial void OnVerifiedAtChanged(string value) => OnPropertyChanged(nameof(HasVerifiedAt));

    partial void OnUpdatedAtChanged(string value) => OnPropertyChanged(nameof(HasUpdatedAt));

    partial void OnPhoneNumberChanged(string value) => ValidatePhone();

    partial void OnIsVerifiedChanged(bool value) => ValidatePhone();

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        var wardId = QueryValues.Read(query, "wardId");
        var serial = QueryValues.Read(query, "serial");
        var key = wardId + "|" + serial;
        if (string.IsNullOrEmpty(wardId) || string.IsNullOrEmpty(serial) || key == _requestedKey)
        {
            return;
        }

        _requestedKey = key;
        WardId = wardId;
        SerialNumber = serial;
        _ = LoadAsync();
    }

    [RelayCommand]
    private Task LoadAsync() =>
        ExecuteAsync(async cancellationToken =>
        {
            IsLoaded = false;
            NotFound = false;
            var details = await _data.GetVoterAsync(WardId, SerialNumber, cancellationToken);
            if (details is null)
            {
                NotFound = true;
                Title = "Voter not found";
                return;
            }

            ApplyDetails(details);
        });

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        ValidatePhone();
        if (!string.IsNullOrEmpty(PhoneError))
        {
            return;
        }

        await ExecuteAsync(async cancellationToken =>
        {
            if (!PhoneNumberRules.TryNormalize(PhoneNumber, out var normalized, out var error))
            {
                throw new AppException(AppErrorKind.Validation, error ?? "Enter a 10-digit phone number.");
            }

            if (IsVerified && normalized.Length == 0)
            {
                throw new AppException(AppErrorKind.Validation, "A phone number is required when marking the voter verified.");
            }

            try
            {
                var saved = await _data.SaveVerificationAsync(new VerificationDraft
                {
                    WardId = WardId,
                    SerialNumber = SerialNumber,
                    PhoneNumber = normalized,
                    IsVerified = IsVerified,
                    HasExistingRow = _hasExistingRow,
                    ETag = _etag,
                    WasVerified = _wasVerified,
                    PreviousVerifiedAtUtc = _previousVerifiedAtUtc
                }, cancellationToken);
                ApplyVerification(saved);
                SuccessMessage = "Verification saved.";
            }
            catch (AppException ex) when (ex.Kind == AppErrorKind.Conflict)
            {
                await ReloadVerificationAsync(cancellationToken);
                throw;
            }
        });
    }

    private bool CanSave() => IsLoaded && !IsBusy && string.IsNullOrEmpty(PhoneError);

    protected override void OnBusyChanged() => SaveCommand.NotifyCanExecuteChanged();

    private async Task ReloadVerificationAsync(CancellationToken cancellationToken)
    {
        var details = await _data.GetVoterAsync(WardId, SerialNumber, cancellationToken);
        if (details is null)
        {
            NotFound = true;
            IsLoaded = false;
            return;
        }

        ApplyDetails(details);
    }

    private void ApplyDetails(VoterDetails details)
    {
        var voter = details.Voter;
        Title = $"Serial {voter.WardVoterSerialNumber}";
        NameTelugu = Show(voter.NameTelugu);
        NameEnglish = Show(voter.NameEnglish);
        RelativeTelugu = Show(voter.RelativeNameTelugu);
        RelativeEnglish = Show(voter.RelativeNameEnglish);
        EpicNumber = Show(voter.EpicNumber);
        AssemblyRoll = Show(voter.AssemblyRollReference);
        SectionNumber = Show(voter.SectionNumber);
        Age = voter.Age > 0 ? voter.Age.ToString(System.Globalization.CultureInfo.InvariantCulture) : "—";
        Gender = Show(voter.Gender);
        HouseNumber = Show(voter.HouseNumber);
        ApplyVerification(details.Verification);
        IsLoaded = true;
        NotFound = false;
    }

    private void ApplyVerification(VoterVerificationRecord? verification)
    {
        _hasExistingRow = verification is not null;
        _etag = verification?.ETag;
        _wasVerified = verification?.IsVerified ?? false;
        _previousVerifiedAtUtc = verification?.VerifiedAtUtc;
        PhoneNumber = PhoneNumberRules.Format(verification?.PhoneNumber);
        IsVerified = verification?.IsVerified ?? false;
        RecordedBy = verification?.VerifiedBy ?? "";
        VerifiedAt = verification?.IsVerified == true
            ? IndiaTime.Format(verification.VerifiedAtUtc)
            : IndiaTime.Format(verification?.VerifiedAtUtc);
        UpdatedAt = IndiaTime.Format(verification?.UpdatedAtUtc);
        if (verification?.IsVerified == false && verification.VerifiedAtUtc is null)
        {
            VerifiedAt = "";
        }
    }

    private void ValidatePhone()
    {
        if (!PhoneNumberRules.TryNormalize(PhoneNumber, out var normalized, out var error))
        {
            PhoneError = error;
        }
        else if (IsVerified && normalized.Length == 0)
        {
            PhoneError = "A phone number is required when marking the voter verified.";
        }
        else
        {
            PhoneError = null;
        }

        SaveCommand.NotifyCanExecuteChanged();
    }

    private static string Show(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}

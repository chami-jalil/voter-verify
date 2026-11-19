using Microsoft.Extensions.Logging;
using VoterVerification.Authentication;
using VoterVerification.Configuration;
using VoterVerification.Helpers;
using VoterVerification.Models;
using VoterVerification.Services;

namespace VoterVerification.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IQueryAttributable
{
    private readonly IVoterDataService _data;
    private readonly ISessionCoordinator _session;
    private readonly IEntraAuthService _auth;
    private string? _requestedWardId;

    public DashboardViewModel(
        IVoterDataService data,
        ISessionCoordinator session,
        IEntraAuthService auth,
        IConnectivityService connectivity,
        ILogger<DashboardViewModel> logger)
        : base(connectivity, logger)
    {
        _data = data;
        _session = session;
        _auth = auth;
    }

    [ObservableProperty]
    private string title = "Ward";

    [ObservableProperty]
    private string expectedVoters = "—";

    [ObservableProperty]
    private string votersOnFile = "—";

    [ObservableProperty]
    private string verifiedCount = "—";

    [ObservableProperty]
    private string signedInAs = "";

    public string WardId { get; private set; } = "";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        var wardId = QueryValues.Read(query, "wardId");
        if (string.IsNullOrEmpty(wardId) || wardId == _requestedWardId)
        {
            return;
        }

        _requestedWardId = wardId;
        WardId = wardId;
        _ = LoadAsync();
    }

    [RelayCommand]
    private Task LoadAsync()
    {
        var ward = WardCatalog.Find(WardId);
        if (ward is null || !ward.IsConfigured)
        {
            ErrorMessage = "This ward is not configured on this device.";
            return Task.CompletedTask;
        }

        Title = ward.DisplayName;
        ExpectedVoters = ward.ExpectedVoters?.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        SignedInAs = _auth.CurrentUser is { Username.Length: > 0 } user
            ? $"Signed in as {user.Username}"
            : "Signed in";
        VotersOnFile = "—";
        VerifiedCount = "—";

        return ExecuteAsync(async cancellationToken =>
        {
            var stats = await _data.GetDashboardStatsAsync(ward.WardId, cancellationToken);
            VotersOnFile = stats.VotersOnFile.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            VerifiedCount = stats.VerifiedCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        });
    }

    [RelayCommand]
    private Task SearchAsync()
    {
        if (string.IsNullOrEmpty(WardId))
        {
            return Task.CompletedTask;
        }

        var route = $"{Routes.Search}?wardId={Uri.EscapeDataString(WardId)}";
        return MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(route));
    }

    [RelayCommand]
    private Task ChangeWardAsync() =>
        MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(".."));

    [RelayCommand]
    private Task SignOutAsync() => _session.SignOutAsync();
}

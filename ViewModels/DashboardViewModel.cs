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
    private readonly ActiveWardContext _wardContext;
    private string? _requestedWardId;

    public DashboardViewModel(
        IVoterDataService data,
        ISessionCoordinator session,
        IEntraAuthService auth,
        ActiveWardContext wardContext,
        IConnectivityService connectivity,
        ILogger<DashboardViewModel> logger)
        : base(connectivity, logger)
    {
        _data = data;
        _session = session;
        _auth = auth;
        _wardContext = wardContext;
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
        if (string.IsNullOrEmpty(WardId))
        {
            ErrorMessage = "This ward is not available.";
            return Task.CompletedTask;
        }

        var selected = _wardContext.Current;
        var ward = selected is not null && string.Equals(selected.WardId, WardId, StringComparison.Ordinal)
            ? selected
            : WardCatalog.Find(WardId);
        Title = ward is { IsConfigured: true, DisplayName.Length: > 0 } ? ward.DisplayName : $"Ward {WardId}";
        ExpectedVoters = ward?.ExpectedVoters?.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        SignedInAs = _auth.CurrentUser is { Username.Length: > 0 } user
            ? $"Signed in as {user.Username}"
            : "Signed in";
        VotersOnFile = "—";
        VerifiedCount = "—";

        return ExecuteAsync(async cancellationToken =>
        {
            var stats = await _data.GetDashboardStatsAsync(WardId, cancellationToken);
            if (stats.ExpectedVoters is int expected)
            {
                ExpectedVoters = expected.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            }

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

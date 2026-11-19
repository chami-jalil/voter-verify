using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using VoterVerification.Authentication;
using VoterVerification.Configuration;
using VoterVerification.Helpers;
using VoterVerification.Models;
using VoterVerification.Services;

namespace VoterVerification.ViewModels;

public partial class WardSelectionViewModel : ViewModelBase
{
    private readonly ISessionCoordinator _session;
    private readonly IEntraAuthService _auth;

    public WardSelectionViewModel(
        ISessionCoordinator session,
        IEntraAuthService auth,
        IConnectivityService connectivity,
        ILogger<WardSelectionViewModel> logger)
        : base(connectivity, logger)
    {
        _session = session;
        _auth = auth;
        Wards = new ObservableCollection<WardOption>(WardCatalog.GetWards());
    }

    public ObservableCollection<WardOption> Wards { get; }

    public string SignedInAs =>
        _auth.CurrentUser is { Username.Length: > 0 } user
            ? $"Signed in as {user.Username}"
            : "Signed in";

    [RelayCommand]
    private Task OpenWardAsync(WardOption? ward)
    {
        if (ward is null || !ward.IsConfigured)
        {
            return Task.CompletedTask;
        }

        var route = $"{Routes.Dashboard}?wardId={Uri.EscapeDataString(ward.WardId)}";
        return MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(route));
    }

    [RelayCommand]
    private Task SignOutAsync() => _session.SignOutAsync();
}

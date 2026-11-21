using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using VoterVerification.Authentication;
using VoterVerification.Helpers;
using VoterVerification.Models;
using VoterVerification.Services;

namespace VoterVerification.ViewModels;

public partial class WardSelectionViewModel : ViewModelBase
{
    private readonly ISessionCoordinator _session;
    private readonly IEntraAuthService _auth;
    private readonly IVoterDataService _data;
    private readonly ActiveWardContext _wardContext;

    public WardSelectionViewModel(
        ISessionCoordinator session,
        IEntraAuthService auth,
        IVoterDataService data,
        ActiveWardContext wardContext,
        IConnectivityService connectivity,
        ILogger<WardSelectionViewModel> logger)
        : base(connectivity, logger)
    {
        _session = session;
        _auth = auth;
        _data = data;
        _wardContext = wardContext;
        Wards = [];
    }

    public ObservableCollection<WardOption> Wards { get; }

    [ObservableProperty]
    private string emptyMessage = "";

    public string SignedInAs =>
        _auth.CurrentUser is { Username.Length: > 0 } user
            ? $"Signed in as {user.Username}"
            : "Signed in";

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(async cancellationToken =>
        {
            var wards = await _data.GetAssignedWardsAsync(cancellationToken);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Wards.Clear();
                foreach (var ward in wards.Where(candidate => candidate.IsConfigured && !string.IsNullOrWhiteSpace(candidate.WardId)))
                {
                    Wards.Add(ward);
                }

                EmptyMessage = Wards.Count == 0 ? "No wards are assigned to this account." : "";
            });
        });
    }

    [RelayCommand]
    private Task OpenWardAsync(WardOption? ward)
    {
        if (ward is null || !ward.IsConfigured)
        {
            return Task.CompletedTask;
        }

        _wardContext.Current = ward;
        var route = $"{Routes.Dashboard}?wardId={Uri.EscapeDataString(ward.WardId)}";
        return MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(route));
    }

    [RelayCommand]
    private Task SignOutAsync() => _session.SignOutAsync();
}

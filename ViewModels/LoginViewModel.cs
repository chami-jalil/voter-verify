using Microsoft.Extensions.Logging;
using VoterVerification.Authentication;
using VoterVerification.Configuration;
using VoterVerification.Helpers;
using VoterVerification.Services;

namespace VoterVerification.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly IEntraAuthService _auth;
    private bool _restoreStarted;

    public LoginViewModel(IEntraAuthService auth, IConnectivityService connectivity, ILogger<LoginViewModel> logger)
        : base(connectivity, logger)
    {
        _auth = auth;
    }

    public bool IsConfigured => AppConfiguration.IsConfigured;

    public bool HasConfigurationProblem => !IsConfigured;

    public string ConfigurationMessage => AppConfiguration.ConfigurationMessage;

    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (_restoreStarted || !IsConfigured || _auth.CurrentUser is not null)
        {
            if (_auth.CurrentUser is not null)
            {
                await GoToWardsAsync();
            }

            return;
        }

        _restoreStarted = true;
        await ExecuteAsync(async cancellationToken =>
        {
            var user = await _auth.TryRestoreAsync(cancellationToken);
            if (user is not null)
            {
                await GoToWardsAsync();
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        await ExecuteAsync(async cancellationToken =>
        {
            await _auth.SignInAsync(cancellationToken);
            await GoToWardsAsync();
        });
    }

    private bool CanSignIn() => IsConfigured && !IsBusy;

    protected override void OnBusyChanged() => SignInCommand.NotifyCanExecuteChanged();

    private static Task GoToWardsAsync() =>
        MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(Routes.Wards));
}

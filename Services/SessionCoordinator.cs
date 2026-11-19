using VoterVerification.Authentication;
using VoterVerification.Helpers;

namespace VoterVerification.Services;

public interface ISessionCoordinator
{
    Task SignOutAsync();
}

public sealed class SessionCoordinator : ISessionCoordinator
{
    private readonly IEntraAuthService _auth;
    private readonly IConfirmationDialog _dialog;

    public SessionCoordinator(IEntraAuthService auth, IConfirmationDialog dialog)
    {
        _auth = auth;
        _dialog = dialog;
    }

    public async Task SignOutAsync()
    {
        var confirmed = await _dialog.ConfirmAsync(
            "Sign out",
            "Sign out of Voter Verification on this device?",
            "Sign out",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        await _auth.SignOutAsync();
        await MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync($"//{Routes.Login}"));
    }
}

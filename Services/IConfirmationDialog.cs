namespace VoterVerification.Services;

public interface IConfirmationDialog
{
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);
}

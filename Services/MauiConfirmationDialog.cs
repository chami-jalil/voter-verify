namespace VoterVerification.Services;

public sealed class MauiConfirmationDialog : IConfirmationDialog
{
    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
        {
            return Task.FromResult(false);
        }

        return page.DisplayAlertAsync(title, message, accept, cancel);
    }
}

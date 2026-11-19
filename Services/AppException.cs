namespace VoterVerification.Services;

public enum AppErrorKind
{
    Network,
    AzureUnavailable,
    NotAuthorized,
    NotFound,
    Conflict,
    SignInRequired,
    NotConfigured,
    Validation,
    Unexpected
}

public sealed class AppException : Exception
{
    public AppException(AppErrorKind kind, string userMessage, Exception? inner = null)
        : base(userMessage, inner)
    {
        Kind = kind;
    }

    public AppErrorKind Kind { get; }
}

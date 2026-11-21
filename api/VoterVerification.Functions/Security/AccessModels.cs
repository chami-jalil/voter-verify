namespace VoterVerification.Functions.Security;

public enum AppRole
{
    SuperAdmin,
    WardAdmin,
    WardOperator
}

public sealed class DirectoryUser
{
    public required string DirectoryId { get; init; }

    public required string ObjectId { get; init; }

    public string? TenantId { get; init; }

    public required AppRole Role { get; init; }

    public bool IsActive { get; init; }

    public IReadOnlySet<string> WardIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

public sealed class CustomerAccount
{
    public required string TenantId { get; init; }

    public required string Name { get; init; }

    public bool IsActive { get; init; }

    public IReadOnlySet<string> WardIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

public sealed class AccessInput
{
    public required string DirectoryId { get; init; }

    public required string ObjectId { get; init; }

    public bool IsBootstrapSuperAdmin { get; init; }

    public DirectoryUser? User { get; init; }

    public CustomerAccount? Customer { get; init; }

    public string? RequestedTenantId { get; init; }

    public string? RequestedWardId { get; init; }

    public bool WardRequired { get; init; }
}

public sealed record UserChangePlan(
    bool Allowed,
    int StatusCode,
    string Message,
    DirectoryUser? User,
    string? TargetTenantId,
    string? AssignmentWardId)
{
    public static UserChangePlan Reject(int statusCode, string message) =>
        new(false, statusCode, message, null, null, null);
}

public sealed record AccessDecision(
    bool Allowed,
    int StatusCode,
    string Message,
    string? TenantId,
    string? WardId,
    AppRole? Role,
    string ObjectId,
    string DirectoryId)
{
    public static AccessDecision Unauthenticated() =>
        new(false, 401, "Sign in again to continue.", null, null, null, "", "");
}

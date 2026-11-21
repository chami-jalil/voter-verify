namespace VoterVerification.Functions.Security;

public sealed class FunctionSettings
{
    public string StorageAccountName { get; init; } = "";

    public string? InitialTenantId { get; init; }

    public string SuperAdminObjectId { get; init; } = "";

    public string SuperAdminDirectoryId { get; init; } = "";

    public string ApiAudience { get; init; } = "";

    public string ApiClientId { get; init; } = "";

    public IReadOnlyList<string> MissingSuperAdminSettings()
    {
        var missing = new List<string>();
        if (!Guid.TryParse(SuperAdminObjectId, out _))
        {
            missing.Add(nameof(SuperAdminObjectId));
        }

        if (!Guid.TryParse(SuperAdminDirectoryId, out _))
        {
            missing.Add(nameof(SuperAdminDirectoryId));
        }

        return missing;
    }

    public bool IsBootstrapSuperAdmin(string directoryId, string objectId) =>
        MissingSuperAdminSettings().Count == 0
        && string.Equals(objectId, SuperAdminObjectId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(directoryId, SuperAdminDirectoryId, StringComparison.OrdinalIgnoreCase);
}

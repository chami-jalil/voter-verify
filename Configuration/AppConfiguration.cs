namespace VoterVerification.Configuration;

/// <summary>
/// Azure and ward settings compiled into the app. Fill the blank Entra and
/// storage values, then rebuild. Do not put account keys, client secrets, or
/// SAS tokens in this class.
/// </summary>
public static class AppConfiguration
{
    /// <summary>Entra ID directory (tenant) id.</summary>
    public const string TenantId = "";

    /// <summary>Application (client) id of the public mobile registration.</summary>
    public const string ClientId = "";

    /// <summary>Storage account name. The app uses the public table endpoint only.</summary>
    public const string StorageAccountName = "";

    public const string VotersTableName = "Voters";

    public const string VerificationsTableName = "VoterVerifications";

    public const string Ward10Id = "10";

    public const string Ward10DisplayName = "Ward 10";

    public const int Ward10ExpectedVoters = 7125;

    /// <summary>Partition key for the second ward. Leave blank until it is known.</summary>
    public const string Ward2Id = "";

    /// <summary>Name shown for the second ward. Do not hardcode the real ward name.</summary>
    public const string Ward2DisplayName = "";

    /// <summary>Expected voters for the second ward. Leave 0 until the real count is supplied.</summary>
    public const int Ward2ExpectedVoters = 0;

    public static readonly string[] StorageScopes = ["https://storage.azure.com/.default"];

    public static string TableServiceUri =>
        $"https://{StorageAccountName}.table.core.windows.net";

    public static string RedirectUri =>
        string.IsNullOrWhiteSpace(ClientId) ? "" : $"msal{ClientId}://auth";

    public static IReadOnlyList<string> MissingSettings()
    {
        var missing = new List<string>();
        if (!Guid.TryParse(TenantId, out _))
        {
            missing.Add(nameof(TenantId));
        }

        if (!Guid.TryParse(ClientId, out _))
        {
            missing.Add(nameof(ClientId));
        }

        if (string.IsNullOrWhiteSpace(StorageAccountName))
        {
            missing.Add(nameof(StorageAccountName));
        }

        return missing;
    }

    public static bool IsConfigured => MissingSettings().Count == 0;

    public static string ConfigurationMessage
    {
        get
        {
            var missing = MissingSettings();
            if (missing.Count == 0)
            {
                return "";
            }

            return "This build is missing " + string.Join(", ", missing)
                + ". Add them in Configuration/AppConfiguration.cs and rebuild the APK.";
        }
    }
}

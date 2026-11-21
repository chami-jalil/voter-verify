namespace VoterVerification.Configuration;

/// <summary>
/// Azure and ward settings compiled into the app. Fill the blank Entra and
/// storage values, then rebuild. Do not put account keys, client secrets, or
/// SAS tokens in this class.
/// </summary>
public static class AppConfiguration
{
    /// <summary>Entra ID directory (tenant) id.</summary>
    public const string TenantId = "189c732a-0bd5-47d5-a3f5-0d67115edc97";

    /// <summary>Application (client) id of the public mobile registration.</summary>
    public const string ClientId = "0d3ddae7-65fa-4914-ace4-cb9659d21ccd";

    /// <summary>Storage account name. The mobile app does not use this. The Function App reads the account name from its own setting.</summary>
    public const string StorageAccountName = "";

    /// <summary>HTTPS base URL of the Azure Functions app, with no function key.</summary>
    public const string FunctionsBaseUrl = "https://voter-verification-api-ajg6g7bme2gdfv6.southindia-01.azurewebsites.net/";

    /// <summary>Delegated scope of the Functions API, such as api://{api-client-id}/access_as_user.</summary>
    public const string ApiScope = "api://7a4a2c30-ff23-47c3-b599-15b3e6a71085/access_as_user";

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

    public static string[] ApiScopes =>
        string.IsNullOrWhiteSpace(ApiScope) ? [] : [ApiScope];

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

        if (!Uri.TryCreate(FunctionsBaseUrl, UriKind.Absolute, out var functionsUri)
            || functionsUri.Scheme != Uri.UriSchemeHttps)
        {
            missing.Add(nameof(FunctionsBaseUrl));
        }

        if (string.IsNullOrWhiteSpace(ApiScope) || !ApiScope.StartsWith("api://", StringComparison.Ordinal))
        {
            missing.Add(nameof(ApiScope));
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

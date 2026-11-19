using VoterVerification.Helpers;
using VoterVerification.Models;

namespace VoterVerification.Configuration;

public static class WardCatalog
{
    public static IReadOnlyList<WardOption> GetWards() =>
    [
        new WardOption
        {
            WardId = AppConfiguration.Ward10Id,
            DisplayName = AppConfiguration.Ward10DisplayName,
            ExpectedVoters = AppConfiguration.Ward10ExpectedVoters,
            IsConfigured = true
        },
        CreateSecondWard()
    ];

    public static WardOption? Find(string? wardId)
    {
        if (string.IsNullOrWhiteSpace(wardId))
        {
            return null;
        }

        return GetWards().FirstOrDefault(ward =>
            string.Equals(ward.WardId, wardId, StringComparison.Ordinal));
    }

    private static WardOption CreateSecondWard()
    {
        var id = AppConfiguration.Ward2Id.Trim();
        var name = AppConfiguration.Ward2DisplayName.Trim();
        var expected = AppConfiguration.Ward2ExpectedVoters;
        var configured = id.Length > 0
            && name.Length > 0
            && expected > 0
            && TableKeyRules.IsValid(id);

        if (configured)
        {
            return new WardOption
            {
                WardId = id,
                DisplayName = name,
                ExpectedVoters = expected,
                IsConfigured = true
            };
        }

        return new WardOption
        {
            WardId = "",
            DisplayName = "",
            ExpectedVoters = null,
            IsConfigured = false,
            UnavailableReason = "Set the second ward id, display name, and expected voter count in AppConfiguration, then rebuild."
        };
    }
}

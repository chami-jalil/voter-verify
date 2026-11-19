using Azure;
using VoterVerification.Configuration;
using VoterVerification.Helpers;
using VoterVerification.Models;
using VoterVerification.Services;

namespace VoterVerification.Tests;

public class WardCatalogTests
{
    [Fact]
    public void Ward10_UsesTheSpecifiedNameAndRollSize()
    {
        var ward = Assert.Single(WardCatalog.GetWards(), candidate => candidate.IsConfigured && candidate.WardId == "10");
        Assert.Equal("Ward 10", ward.DisplayName);
        Assert.Equal(7125, ward.ExpectedVoters);
        Assert.Equal("Expected voters: 7,125", ward.ExpectedText);
    }

    [Fact]
    public void SecondWard_IsNotConfiguredUntilValuesAreSupplied()
    {
        Assert.Equal("", AppConfiguration.Ward2Id);
        Assert.Equal("", AppConfiguration.Ward2DisplayName);
        Assert.Equal(0, AppConfiguration.Ward2ExpectedVoters);

        var ward = WardCatalog.GetWards().Single(candidate => !candidate.IsConfigured);
        Assert.Equal("Second ward", ward.Title);
        Assert.Equal("", ward.WardId);
        Assert.Null(ward.ExpectedVoters);
        Assert.False(string.IsNullOrWhiteSpace(ward.UnavailableReason));
    }
}

public class PhoneNumberRulesTests
{
    [Theory]
    [InlineData("9876543210", "9876543210")]
    [InlineData("98765 43210", "9876543210")]
    [InlineData("+91 98765 43210", "9876543210")]
    [InlineData("09876543210", "9876543210")]
    public void AcceptsIndianMobileNumbers(string input, string expected)
    {
        Assert.True(PhoneNumberRules.TryNormalize(input, out var normalized, out var error));
        Assert.Null(error);
        Assert.Equal(expected, normalized);
        Assert.Equal("98765 43210", PhoneNumberRules.Format(normalized));
    }

    [Fact]
    public void EmptyPhoneIsAllowed()
    {
        Assert.True(PhoneNumberRules.TryNormalize("  ", out var normalized, out var error));
        Assert.Equal("", normalized);
        Assert.Null(error);
    }

    [Fact]
    public void RejectsShortNumbers()
    {
        Assert.False(PhoneNumberRules.TryNormalize("12345", out _, out var error));
        Assert.Equal("Enter a 10-digit phone number.", error);
    }
}

public class VoterLookupTests
{
    [Theory]
    [InlineData("", VoterLookupMode.Invalid)]
    [InlineData("A", VoterLookupMode.Invalid)]
    [InlineData("42", VoterLookupMode.KeyAndHouse)]
    [InlineData("12-A", VoterLookupMode.KeyAndHouse)]
    [InlineData("abc1234567", VoterLookupMode.Epic)]
    [InlineData("ABC 1234567", VoterLookupMode.Epic)]
    [InlineData("175/42/123", VoterLookupMode.AssemblyRoll)]
    [InlineData("  175 / 42 / 123  ", VoterLookupMode.AssemblyRoll)]
    [InlineData("175-42-123", VoterLookupMode.AssemblyRoll)]
    [InlineData("175/42", VoterLookupMode.KeyAndHouse)]
    [InlineData("12/A", VoterLookupMode.KeyAndHouse)]
    [InlineData("రామ", VoterLookupMode.Name)]
    [InlineData("Rama", VoterLookupMode.Name)]
    public void ClassifiesSearchText(string query, VoterLookupMode mode)
    {
        Assert.Equal(mode, VoterLookup.Classify(query));
    }

    [Fact]
    public void AssemblyRollIsNotAnEpicOrAHouseNumber()
    {
        Assert.Equal(VoterLookupMode.Epic, VoterLookup.Classify("ABC1234567"));
        Assert.False(VoterLookup.IsAssemblyRoll("ABC1234567"));
        Assert.False(VoterLookup.IsAssemblyRoll("ABC 1234567"));

        Assert.Equal(VoterLookupMode.AssemblyRoll, VoterLookup.Classify("175 / 42 / 123"));
        Assert.NotEqual(VoterLookupMode.KeyAndHouse, VoterLookup.Classify("175-42-123"));
        Assert.Equal(VoterLookupMode.KeyAndHouse, VoterLookup.Classify("12-A"));
        Assert.Equal(VoterLookupMode.KeyAndHouse, VoterLookup.Classify("12A"));
        Assert.Equal(VoterLookupMode.KeyAndHouse, VoterLookup.Classify("A12"));
    }

    [Fact]
    public void NormalizesEpicAssemblyRollAndHouse()
    {
        Assert.Equal("ABC1234567", VoterLookup.NormalizeEpic("abc 1234567"));
        Assert.Equal("12A", VoterLookup.NormalizeHouse(" 12-a "));
        Assert.Equal("175/42/123", VoterLookup.NormalizeAssemblyRoll("  175 / 42 / 123 "));
        Assert.Equal("175/42/123", VoterLookup.NormalizeAssemblyRoll("175-42-123"));
        Assert.Equal(
            VoterLookup.NormalizeAssemblyRoll("175/42/123"),
            VoterLookup.NormalizeAssemblyRoll("175 - 42 - 123"));
    }

    [Fact]
    public void MatchesAssemblyRollAfterWhitespaceNormalization()
    {
        Assert.True(VoterTextMatch.MatchesAssemblyRoll("175/42/123", "175 / 42 / 123"));
        Assert.True(VoterTextMatch.MatchesAssemblyRoll("175 - 42 - 123", "175/42/123"));
        Assert.False(VoterTextMatch.MatchesAssemblyRoll("175/42/123", "ABC1234567"));
        Assert.False(VoterTextMatch.MatchesAssemblyRoll("12-A", "12-A"));
        Assert.False(VoterTextMatch.MatchesAssemblyRoll("175/42/124", "175/42/123"));
    }

    [Theory]
    [InlineData("రామారావు", "", "", "", "రామ")]
    [InlineData("", "Ramarao", "", "", "rama")]
    [InlineData("", "", "సీత", "", "సీత")]
    [InlineData("", "", "", "Sita Devi", "sita")]
    public void MatchesEachNameField(string telugu, string english, string relativeTelugu, string relativeEnglish, string query)
    {
        Assert.True(VoterTextMatch.MatchesName(telugu, english, relativeTelugu, relativeEnglish, query));
    }

    [Fact]
    public void RejectsADifferentName()
    {
        Assert.False(VoterTextMatch.MatchesName("రామారావు", "Ramarao", "సీత", "Sita", "Lakshmi"));
    }

    [Fact]
    public void HouseMatchKeepsExactValuesAndPrefixOnly()
    {
        Assert.True(VoterTextMatch.MatchesHouse("12-A", "12A", "12-A"));
        Assert.True(VoterTextMatch.MatchesHouse("12", "12", "12"));
        Assert.True(VoterTextMatch.MatchesHouse("12-A", "12A", "12"));
        Assert.True(VoterTextMatch.MatchesHouse("12/1", "121", "12"));
        Assert.False(VoterTextMatch.MatchesHouse("112", "112", "12"));
        Assert.False(VoterTextMatch.MatchesHouse("13", "13", "12"));
        Assert.True(VoterTextMatch.MatchesHouse("12-A", "", "12a"));
    }

    [Fact]
    public void SerialSortIsNumericWhenPossible()
    {
        var serials = new[] { "10", "2", "A1", "1" };
        var ordered = serials.OrderBy(value => value, Comparer<string>.Create(VoterTextMatch.CompareSerial)).ToArray();
        Assert.Equal(new[] { "1", "2", "10", "A1" }, ordered);
    }
}

public class VerificationUpdatePolicyTests
{
    private static readonly SignedInUser User = new()
    {
        ObjectId = "oid",
        Username = "clerk@contoso.com",
        DisplayName = "Ward Clerk",
        VerifiedBy = "Ward Clerk (clerk@contoso.com)"
    };

    [Fact]
    public void InsertDoesNotSendAnEtag()
    {
        var plan = VerificationUpdatePolicy.Create(new VerificationDraft
        {
            WardId = "10",
            SerialNumber = "15",
            PhoneNumber = "9876543210",
            IsVerified = true,
            HasExistingRow = false
        }, User, new DateTimeOffset(2026, 11, 19, 4, 0, 0, TimeSpan.Zero));

        Assert.True(plan.Insert);
        Assert.Null(plan.IfMatchETag);
        Assert.Equal("Ward Clerk (clerk@contoso.com)", plan.VerifiedBy);
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 4, 0, 0, TimeSpan.Zero), plan.VerifiedAtUtc);
    }

    [Fact]
    public void UpdateKeepsTheExistingEtagAndVerificationTime()
    {
        var verifiedAt = new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero);
        var plan = VerificationUpdatePolicy.Create(new VerificationDraft
        {
            WardId = "10",
            SerialNumber = "15",
            PhoneNumber = "9876543210",
            IsVerified = true,
            HasExistingRow = true,
            ETag = "W/\"datetime'2026-11-01T04%3A00%3A00.0000000Z'\"",
            WasVerified = true,
            PreviousVerifiedAtUtc = verifiedAt
        }, User, new DateTimeOffset(2026, 11, 19, 4, 0, 0, TimeSpan.Zero));

        Assert.False(plan.Insert);
        Assert.Equal("W/\"datetime'2026-11-01T04%3A00%3A00.0000000Z'\"", plan.IfMatchETag);
        Assert.NotEqual("*", plan.IfMatchETag);
        Assert.Equal(verifiedAt, plan.VerifiedAtUtc);
    }

    [Fact]
    public void UnverifyClearsTheVerificationTime()
    {
        var plan = VerificationUpdatePolicy.Create(new VerificationDraft
        {
            WardId = "10",
            SerialNumber = "15",
            PhoneNumber = "",
            IsVerified = false,
            HasExistingRow = true,
            ETag = "etag-1",
            WasVerified = true,
            PreviousVerifiedAtUtc = DateTimeOffset.UtcNow
        }, User, DateTimeOffset.UtcNow);

        Assert.Null(plan.VerifiedAtUtc);
        Assert.False(plan.IsVerified);
    }

    [Fact]
    public void ExistingRowWithoutEtagIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => VerificationUpdatePolicy.Create(new VerificationDraft
        {
            WardId = "10",
            SerialNumber = "15",
            PhoneNumber = "",
            IsVerified = false,
            HasExistingRow = true,
            ETag = "*"
        }, User, DateTimeOffset.UtcNow));
    }
}

public class ErrorMapperTests
{
    [Theory]
    [InlineData(401, AppErrorKind.NotAuthorized)]
    [InlineData(403, AppErrorKind.NotAuthorized)]
    [InlineData(412, AppErrorKind.Conflict)]
    [InlineData(409, AppErrorKind.Conflict)]
    [InlineData(429, AppErrorKind.AzureUnavailable)]
    [InlineData(503, AppErrorKind.AzureUnavailable)]
    public void MapsAzureStatusToAFriendlyError(int status, AppErrorKind kind)
    {
        var mapped = ErrorMapper.FromAzure(new RequestFailedException(status, "service failure", "Code", null));
        Assert.Equal(kind, mapped.Kind);
        Assert.DoesNotContain("service failure", mapped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingTableIsDistinctFromAMissingVoter()
    {
        var mapped = ErrorMapper.FromAzure(new RequestFailedException(404, "missing", "TableNotFound", null));
        Assert.Contains("Voters", mapped.Message, StringComparison.Ordinal);
        Assert.Contains("VoterVerifications", mapped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NetworkFailuresDoNotExposeExceptionText()
    {
        var message = ErrorMapper.ToUserMessage(new HttpRequestException("https://secret.table.core.windows.net phone 9876543210"));
        Assert.DoesNotContain("9876543210", message, StringComparison.Ordinal);
        Assert.Contains("network", message, StringComparison.OrdinalIgnoreCase);
    }
}

public class LogRedactorTests
{
    [Fact]
    public void MasksLongDigitRuns()
    {
        Assert.Equal("caller ********", LogRedactor.Safe("caller 9876543210"));
        Assert.Equal("serial 123", LogRedactor.Safe("serial 123"));
    }
}

public class QueryBuilderTests
{
    [Fact]
    public void EscapesQuotesInFilters()
    {
        var filter = VoterQueryBuilder.House("10", "12'A", "12A");
        Assert.Contains("HouseNumber eq '12''A'", filter, StringComparison.Ordinal);
        Assert.Contains("NormalizedHouseNumber ge '12A'", filter, StringComparison.Ordinal);
        Assert.Contains("NormalizedHouseNumber lt '12B'", filter, StringComparison.Ordinal);
        Assert.Contains("PartitionKey eq '10'", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("substringof", filter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssemblyRollFilterStaysInsideTheWardAndNotTheHouseColumn()
    {
        var filter = VoterQueryBuilder.AssemblyRoll("10", "175/42/123");
        Assert.Contains("PartitionKey eq '10'", filter, StringComparison.Ordinal);
        Assert.Contains("AssemblyRollReference eq '175/42/123'", filter, StringComparison.Ordinal);
        Assert.Contains("AssemblyRollReference eq '175-42-123'", filter, StringComparison.Ordinal);
        Assert.Contains("AssemblyRollReference eq '175 / 42 / 123'", filter, StringComparison.Ordinal);
        Assert.Contains("AssemblyRollReference eq '175 - 42 - 123'", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("HouseNumber", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("EpicNumber", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortHousePrefixUsesTheNextCharacterAsTheUpperBound()
    {
        var filter = VoterQueryBuilder.House("10", "9", "9");
        Assert.Contains("NormalizedHouseNumber ge '9'", filter, StringComparison.Ordinal);
        Assert.Contains("NormalizedHouseNumber lt ':'", filter, StringComparison.Ordinal);
        Assert.Contains("HouseNumber eq '9'", filter, StringComparison.Ordinal);
    }
}

public class ConfigurationDriftTests
{
    [Fact]
    public void ManifestSchemeComesFromTheClientIdConstant()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "sources");
        var manifest = File.ReadAllText(Path.Combine(root, "AndroidManifest.xml"));
        var project = File.ReadAllText(Path.Combine(root, "VoterVerification.csproj"));
        var configuration = File.ReadAllText(Path.Combine(root, "AppConfiguration.cs"));

        Assert.Contains("android:scheme=\"${msalScheme}\"", manifest, StringComparison.Ordinal);
        Assert.Contains("AndroidManifestPlaceholders", project, StringComparison.Ordinal);
        Assert.Contains("const string ClientId = \"", configuration, StringComparison.Ordinal);
        Assert.Contains("msalScheme=", project, StringComparison.Ordinal);
        Assert.Equal("", AppConfiguration.ClientId);
    }

    [Fact]
    public void VoterMasterDataIsNeverWritten()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sources", "AzureTableVoterDataService.cs"));
        Assert.DoesNotContain("_voters.Add", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_voters.Update", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_voters.Upsert", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_voters.Delete", source, StringComparison.Ordinal);
        Assert.Contains("_verifications.AddEntityAsync", source, StringComparison.Ordinal);
        Assert.Contains("_verifications.UpdateEntityAsync", source, StringComparison.Ordinal);
        Assert.Contains("TableUpdateMode.Replace", source, StringComparison.Ordinal);
        Assert.Contains("VoterLookupMode.AssemblyRoll", source, StringComparison.Ordinal);
        Assert.Contains("VoterQueryBuilder.AssemblyRoll", source, StringComparison.Ordinal);
        Assert.Contains("MatchesHouse", source, StringComparison.Ordinal);
        Assert.DoesNotContain("substringof", source, StringComparison.OrdinalIgnoreCase);
    }
}

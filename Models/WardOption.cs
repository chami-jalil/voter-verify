namespace VoterVerification.Models;

public sealed class WardOption
{
    public string TenantId { get; init; } = "";

    public required string WardId { get; init; }

    public required string DisplayName { get; init; }

    public int? ExpectedVoters { get; init; }

    public bool IsConfigured { get; init; }

    public string? UnavailableReason { get; init; }

    public string Title => IsConfigured ? DisplayName : "Second ward";

    public string ExpectedText => ExpectedVoters is int count
        ? $"Expected voters: {count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}"
        : "Expected voter count is not set";

    public string ActionText => IsConfigured ? "Open ward" : "Unavailable";

    public bool ShowUnavailable => !IsConfigured;
}

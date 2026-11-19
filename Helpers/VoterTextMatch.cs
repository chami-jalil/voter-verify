namespace VoterVerification.Helpers;

public static class VoterTextMatch
{
    public static bool MatchesName(
        string? nameTelugu,
        string? nameEnglish,
        string? relativeTelugu,
        string? relativeEnglish,
        string query)
    {
        var term = query.Trim();
        if (term.Length < VoterLookup.MinimumNameLength)
        {
            return false;
        }

        return Contains(nameTelugu, term)
            || Contains(nameEnglish, term)
            || Contains(relativeTelugu, term)
            || Contains(relativeEnglish, term);
    }

    public static bool MatchesAssemblyRoll(string? storedReference, string query)
    {
        if (string.IsNullOrWhiteSpace(storedReference) || !VoterLookup.IsAssemblyRoll(query))
        {
            return false;
        }

        return string.Equals(
            VoterLookup.NormalizeAssemblyRoll(storedReference),
            VoterLookup.NormalizeAssemblyRoll(query),
            StringComparison.Ordinal);
    }

    public static bool MatchesHouse(string? houseNumber, string? normalizedHouseNumber, string query)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(houseNumber)
            && string.Equals(houseNumber, trimmed, StringComparison.Ordinal))
        {
            return true;
        }

        var normalizedQuery = VoterLookup.NormalizeHouse(trimmed);
        if (normalizedQuery.Length == 0)
        {
            return false;
        }

        var storedNormalized = string.IsNullOrWhiteSpace(normalizedHouseNumber)
            ? VoterLookup.NormalizeHouse(houseNumber ?? "")
            : normalizedHouseNumber;
        return storedNormalized.StartsWith(normalizedQuery, StringComparison.Ordinal);
    }

    public static int CompareSerial(string left, string right)
    {
        var leftNumeric = long.TryParse(left, out var leftValue);
        var rightNumeric = long.TryParse(right, out var rightValue);
        if (leftNumeric && rightNumeric)
        {
            return leftValue.CompareTo(rightValue);
        }

        return string.Compare(left, right, StringComparison.Ordinal);
    }

    private static bool Contains(string? source, string term) =>
        !string.IsNullOrEmpty(source)
        && source.Contains(term, StringComparison.OrdinalIgnoreCase);
}

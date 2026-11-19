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

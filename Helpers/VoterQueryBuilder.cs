namespace VoterVerification.Helpers;

public static class VoterQueryBuilder
{
    public static string Partition(string wardId) =>
        $"PartitionKey eq '{ODataLiteral.Escape(wardId)}'";

    public static string Epic(string wardId, string epicNumber) =>
        $"{Partition(wardId)} and EpicNumber eq '{ODataLiteral.Escape(epicNumber)}'";

    public static string AssemblyRoll(string wardId, string canonicalReference)
    {
        var comparisons = AssemblyRollQueryLiterals(canonicalReference)
            .Select(literal => $"AssemblyRollReference eq '{ODataLiteral.Escape(literal)}'");
        var clause = string.Join(" or ", comparisons);
        if (clause.Contains(" or ", StringComparison.Ordinal))
        {
            clause = $"({clause})";
        }

        return $"{Partition(wardId)} and {clause}";
    }

    public static IReadOnlyList<string> AssemblyRollQueryLiterals(string canonicalReference)
    {
        var parts = canonicalReference.Split('/');
        var hyphen = canonicalReference.Replace('/', '-');
        return new[]
        {
            canonicalReference,
            hyphen,
            string.Join(" / ", parts),
            string.Join(" - ", parts)
        }.Distinct(StringComparer.Ordinal).ToArray();
    }

    // Table Storage has no startswith or contains. A prefix is expressed as a
    // string range so the service returns only candidate rows. This still scans
    // the ward partition; NormalizedHouseNumber is not a table key.
    public static string House(string wardId, string houseNumber, string normalizedHouseNumber)
    {
        var printed = $"HouseNumber eq '{ODataLiteral.Escape(houseNumber)}'";
        if (normalizedHouseNumber.Length == 0)
        {
            return $"{Partition(wardId)} and {printed}";
        }

        var upper = ExclusiveUpperBound(normalizedHouseNumber);
        var prefix =
            $"NormalizedHouseNumber ge '{ODataLiteral.Escape(normalizedHouseNumber)}' and NormalizedHouseNumber lt '{ODataLiteral.Escape(upper)}'";
        return $"{Partition(wardId)} and (({prefix}) or {printed})";
    }

    public static string ExclusiveUpperBound(string prefix)
    {
        var chars = prefix.ToCharArray();
        for (var index = chars.Length - 1; index >= 0; index--)
        {
            if (chars[index] != char.MaxValue)
            {
                chars[index]++;
                return new string(chars, 0, index + 1);
            }
        }

        return prefix + "\uFFFF";
    }

    public static string VerifiedInWard(string wardId) =>
        $"{Partition(wardId)} and IsVerified eq true";
}

namespace VoterVerification.Helpers;

public static class ODataLiteral
{
    public static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

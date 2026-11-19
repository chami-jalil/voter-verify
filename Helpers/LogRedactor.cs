using System.Text.RegularExpressions;

namespace VoterVerification.Helpers;

public static partial class LogRedactor
{
    public static string Safe(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return DigitRun().Replace(value, "********");
    }

    [GeneratedRegex(@"\d{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex DigitRun();
}

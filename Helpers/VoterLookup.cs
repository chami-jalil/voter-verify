using System.Text.RegularExpressions;

namespace VoterVerification.Helpers;

public enum VoterLookupMode
{
    Invalid,
    KeyAndHouse,
    Epic,
    AssemblyRoll,
    Name
}

public static partial class VoterLookup
{
    public const int MinimumNameLength = 2;

    public static VoterLookupMode Classify(string? query)
    {
        var trimmed = query?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed.Length > 80)
        {
            return VoterLookupMode.Invalid;
        }

        if (IsEpic(trimmed))
        {
            return VoterLookupMode.Epic;
        }

        // 175/42/123 is an assembly roll reference, including spaces and hyphens.
        // It must not fall through to house search. 12-A and 12/A stay house numbers.
        if (IsAssemblyRoll(trimmed))
        {
            return VoterLookupMode.AssemblyRoll;
        }

        // A digit means a serial or house number, including values such as 12-A.
        // Name search is for words with no digits.
        if (!trimmed.Any(char.IsDigit) && trimmed.Any(char.IsLetter))
        {
            return trimmed.Length >= MinimumNameLength ? VoterLookupMode.Name : VoterLookupMode.Invalid;
        }

        return VoterLookupMode.KeyAndHouse;
    }

    public static string NormalizeEpic(string query)
    {
        var compact = new string(query.Where(character => !char.IsWhiteSpace(character)).ToArray());
        return compact.ToUpperInvariant();
    }

    public static string NormalizeAssemblyRoll(string query)
    {
        var compact = new string(query.Where(character => !char.IsWhiteSpace(character)).ToArray());
        return compact.Replace('-', '/').ToUpperInvariant();
    }

    public static string NormalizeHouse(string value)
    {
        var characters = value.Where(char.IsLetterOrDigit).ToArray();
        return new string(characters).ToUpperInvariant();
    }

    public static bool IsEpic(string query) => EpicPattern().IsMatch(NormalizeEpic(query));

    public static bool IsAssemblyRoll(string query) => AssemblyRollPattern().IsMatch(NormalizeAssemblyRoll(query));

    public static string ValidationMessage(string? query)
    {
        var trimmed = query?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return "Enter a serial number, EPIC, assembly roll, name, or house number.";
        }

        if (trimmed.Length > 80)
        {
            return "That search is too long.";
        }

        if (trimmed.Any(char.IsLetter) && !IsEpic(trimmed) && trimmed.Length < MinimumNameLength)
        {
            return "Enter at least 2 characters to search by name.";
        }

        return "";
    }

    [GeneratedRegex("^[A-Z]{3}[0-9]{7}$", RegexOptions.CultureInvariant)]
    private static partial Regex EpicPattern();

    [GeneratedRegex("^\\d+(?:/\\d+){2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex AssemblyRollPattern();
}

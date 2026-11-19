namespace VoterVerification.Helpers;

public static class PhoneNumberRules
{
    public static bool TryNormalize(string? input, out string normalized, out string? error)
    {
        normalized = "";
        error = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        var digits = new string(input.Where(char.IsDigit).ToArray());
        if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }
        else if (digits.Length == 11 && digits.StartsWith('0'))
        {
            digits = digits[1..];
        }

        if (digits.Length != 10)
        {
            error = "Enter a 10-digit phone number.";
            return false;
        }

        normalized = digits;
        return true;
    }

    public static string Format(string? normalized)
    {
        if (string.IsNullOrEmpty(normalized) || normalized.Length != 10)
        {
            return normalized ?? "";
        }

        return $"{normalized[..5]} {normalized[5..]}";
    }
}

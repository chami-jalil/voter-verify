namespace VoterVerification.Helpers;

public static class TableKeyRules
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is '/' or '\\' or '#' or '?' || char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }
}

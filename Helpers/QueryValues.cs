namespace VoterVerification.Helpers;

public static class QueryValues
{
    public static string Read(IDictionary<string, object> query, string key)
    {
        if (!query.TryGetValue(key, out var raw) || raw is null)
        {
            return "";
        }

        var text = raw as string ?? raw.ToString() ?? "";
        try
        {
            return Uri.UnescapeDataString(text);
        }
        catch (UriFormatException)
        {
            return text;
        }
    }
}

namespace VoterVerification.Helpers;

public static class IndiaTime
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    public static string Format(DateTimeOffset? utc)
    {
        if (utc is null)
        {
            return "";
        }

        var local = TimeZoneInfo.ConvertTime(utc.Value, Zone);
        return local.ToString("d MMM yyyy, h:mm tt", System.Globalization.CultureInfo.InvariantCulture) + " IST";
    }

    private static TimeZoneInfo ResolveZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

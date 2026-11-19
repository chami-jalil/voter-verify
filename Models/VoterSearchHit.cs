namespace VoterVerification.Models;

public sealed class VoterSearchHit
{
    public required string SerialNumber { get; init; }

    public string NameTelugu { get; init; } = "";

    public string NameEnglish { get; init; } = "";

    public string RelativeEnglish { get; init; } = "";

    public string EpicNumber { get; init; } = "";

    public string HouseNumber { get; init; } = "";

    public int Age { get; init; }

    public string Gender { get; init; } = "";

    public string Title
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(NameTelugu))
            {
                return NameTelugu;
            }

            if (!string.IsNullOrWhiteSpace(NameEnglish))
            {
                return NameEnglish;
            }

            return "Unnamed voter";
        }
    }

    public string Subtitle
    {
        get
        {
            if (string.IsNullOrWhiteSpace(NameTelugu) || string.IsNullOrWhiteSpace(NameEnglish))
            {
                return string.IsNullOrWhiteSpace(RelativeEnglish) ? "" : $"Relative: {RelativeEnglish}";
            }

            return NameEnglish;
        }
    }

    public string MetaLine
    {
        get
        {
            var parts = new List<string> { $"Serial {SerialNumber}" };
            if (!string.IsNullOrWhiteSpace(EpicNumber))
            {
                parts.Add(EpicNumber);
            }

            if (!string.IsNullOrWhiteSpace(HouseNumber))
            {
                parts.Add($"House {HouseNumber}");
            }

            var person = new List<string>();
            if (Age > 0)
            {
                person.Add(Age.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(Gender))
            {
                person.Add(Gender);
            }

            if (person.Count > 0)
            {
                parts.Add(string.Join(" · ", person));
            }

            return string.Join(" · ", parts);
        }
    }
}

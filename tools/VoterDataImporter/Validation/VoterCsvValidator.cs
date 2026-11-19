using System.Globalization;
using System.Text;
using VoterDataImporter.Csv;
using VoterDataImporter.Import;
using VoterVerification.Configuration;
using VoterVerification.Data;
using VoterVerification.Helpers;

namespace VoterDataImporter.Validation;

public enum FindingSeverity
{
    Error,
    Warning
}

public sealed class RowFinding
{
    public required int RowNumber { get; init; }

    public required FindingSeverity Severity { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }
}

public sealed class ValidationReport
{
    public required int TotalRows { get; init; }

    public required int ValidRows { get; init; }

    public required int InvalidRows { get; init; }

    public required int DuplicateSerialNumbers { get; init; }

    public required int DuplicateEpicNumbers { get; init; }

    public required int MissingFields { get; init; }

    public required int Warnings { get; init; }

    public required IReadOnlyList<string> FileErrors { get; init; }

    public required IReadOnlyList<RowFinding> Findings { get; init; }

    public required IReadOnlyList<VoterTableEntity> ValidEntities { get; init; }

    public bool HasBlockingErrors => FileErrors.Count > 0 || InvalidRows > 0;

    public string Format()
    {
        var text = new StringBuilder();
        text.AppendLine($"CSV rows: {TotalRows}");
        text.AppendLine($"Valid rows: {ValidRows}");
        text.AppendLine($"Invalid rows: {InvalidRows}");
        text.AppendLine($"Duplicate serial numbers: {DuplicateSerialNumbers}");
        text.AppendLine($"Duplicate EPIC numbers: {DuplicateEpicNumbers}");
        text.AppendLine($"Missing fields: {MissingFields}");
        text.AppendLine($"Warnings: {Warnings}");
        foreach (var error in FileErrors)
        {
            text.AppendLine($"File: {error}");
        }

        foreach (var finding in Findings.OrderBy(item => item.RowNumber).ThenBy(item => item.Severity))
        {
            var severity = finding.Severity == FindingSeverity.Error ? "error" : "warning";
            text.AppendLine($"Row {finding.RowNumber} {severity}: {finding.Message}");
        }

        return text.ToString();
    }
}

public static class VoterCsvValidator
{
    public const string Missing = "Missing";
    public const string DuplicateSerial = "DuplicateSerial";
    public const string DuplicateEpic = "DuplicateEpic";
    public const int MinimumAge = 18;
    public const int MaximumAge = 120;

    private static readonly Dictionary<string, string> Genders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Male"] = "Male",
        ["M"] = "Male",
        ["Female"] = "Female",
        ["F"] = "Female",
        ["Third Gender"] = "Third Gender",
        ["Third"] = "Third Gender",
        ["TG"] = "Third Gender",
        ["పురుషుడు"] = "పురుషుడు",
        ["స్త్రీ"] = "స్త్రీ",
        ["ఇతరులు"] = "ఇతరులు"
    };

    public static ValidationReport ValidateCsv(string csv, string? wardId = null) =>
        Validate(VoterCsvReader.Read(csv), wardId ?? AppConfiguration.Ward10Id);

    public static ValidationReport ValidateEntities(IReadOnlyList<VoterTableEntity> entities, string wardId)
    {
        var rows = entities.Select((entity, index) => new VoterCsvRow
        {
            RowNumber = index + 1,
            Fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [VoterCsvColumns.WardId] = entity.WardId ?? "",
                [VoterCsvColumns.WardVoterSerialNumber] = entity.WardVoterSerialNumber ?? "",
                [VoterCsvColumns.EpicNumber] = entity.EpicNumber ?? "",
                [VoterCsvColumns.AssemblyRollReference] = entity.AssemblyRollReference ?? "",
                [VoterCsvColumns.NameTelugu] = entity.NameTelugu ?? "",
                [VoterCsvColumns.NameEnglish] = entity.NameEnglish ?? "",
                [VoterCsvColumns.RelativeNameTelugu] = entity.RelativeNameTelugu ?? "",
                [VoterCsvColumns.RelativeNameEnglish] = entity.RelativeNameEnglish ?? "",
                [VoterCsvColumns.Age] = entity.Age.ToString(CultureInfo.InvariantCulture),
                [VoterCsvColumns.Gender] = entity.Gender ?? "",
                [VoterCsvColumns.HouseNumber] = entity.HouseNumber ?? "",
                [VoterCsvColumns.NormalizedHouseNumber] = entity.NormalizedHouseNumber ?? "",
                [VoterCsvColumns.SectionNumber] = entity.SectionNumber ?? ""
            }
        }).ToList();

        return Validate(new CsvReadResult { FileErrors = [], Rows = rows }, wardId);
    }

    public static ValidationReport Validate(CsvReadResult csv, string wardId)
    {
        var drafts = csv.Rows.Select(row => Inspect(row, wardId)).ToList();
        var duplicates = MarkDuplicates(drafts);

        var findings = drafts.SelectMany(draft => draft.Findings).ToList();
        var valid = drafts.Where(draft => !draft.Findings.Any(item => item.Severity == FindingSeverity.Error)).ToList();
        return new ValidationReport
        {
            TotalRows = csv.Rows.Count,
            ValidRows = valid.Count,
            InvalidRows = drafts.Count - valid.Count,
            DuplicateSerialNumbers = duplicates.SerialGroups,
            DuplicateEpicNumbers = duplicates.EpicGroups,
            MissingFields = findings.Count(item => item.Code == Missing),
            Warnings = findings.Count(item => item.Severity == FindingSeverity.Warning),
            FileErrors = csv.FileErrors,
            Findings = findings,
            ValidEntities = valid.Select(draft => draft.Entity!).ToList()
        };
    }

    private static RowDraft Inspect(VoterCsvRow row, string wardId)
    {
        var draft = new RowDraft(row);
        if (row.Fields.ContainsKey("__extra"))
        {
            draft.Error("ExtraColumns", "The row has more columns than the header.");
        }

        if (row.Fields.Values.All(string.IsNullOrWhiteSpace) && !row.Fields.ContainsKey("__extra"))
        {
            draft.Error(Missing, "Row is blank.");
            return draft;
        }

        var ward = Field(row, VoterCsvColumns.WardId);
        var serial = Field(row, VoterCsvColumns.WardVoterSerialNumber);
        var epic = Field(row, VoterCsvColumns.EpicNumber);
        var assembly = Field(row, VoterCsvColumns.AssemblyRollReference);
        var nameTelugu = Field(row, VoterCsvColumns.NameTelugu);
        var nameEnglish = Field(row, VoterCsvColumns.NameEnglish);
        var relativeTelugu = Field(row, VoterCsvColumns.RelativeNameTelugu);
        var relativeEnglish = Field(row, VoterCsvColumns.RelativeNameEnglish);
        var ageText = Field(row, VoterCsvColumns.Age);
        var genderText = Field(row, VoterCsvColumns.Gender);
        var house = Field(row, VoterCsvColumns.HouseNumber);
        var normalizedHouse = Field(row, VoterCsvColumns.NormalizedHouseNumber);
        var section = Field(row, VoterCsvColumns.SectionNumber);

        draft.WardId = ward;
        draft.Serial = serial;
        draft.EpicKey = string.IsNullOrEmpty(epic) ? "" : VoterLookup.NormalizeEpic(epic);

        Require(draft, VoterCsvColumns.WardId, ward);
        Require(draft, VoterCsvColumns.WardVoterSerialNumber, serial);
        Require(draft, VoterCsvColumns.EpicNumber, epic);
        Require(draft, VoterCsvColumns.AssemblyRollReference, assembly);
        Require(draft, VoterCsvColumns.NameTelugu, nameTelugu);
        Require(draft, VoterCsvColumns.NameEnglish, nameEnglish);
        Require(draft, VoterCsvColumns.RelativeNameTelugu, relativeTelugu);
        Require(draft, VoterCsvColumns.RelativeNameEnglish, relativeEnglish);
        Require(draft, VoterCsvColumns.Age, ageText);
        Require(draft, VoterCsvColumns.Gender, genderText);
        Require(draft, VoterCsvColumns.HouseNumber, house);
        Require(draft, VoterCsvColumns.NormalizedHouseNumber, normalizedHouse);
        Require(draft, VoterCsvColumns.SectionNumber, section);

        if (ward.Length > 0 && !string.Equals(ward, wardId, StringComparison.Ordinal))
        {
            draft.Error("Ward", $"WardId '{ward}' does not match ward {wardId}.");
        }

        if (ward.Length > 0 && !TableKeyRules.IsValid(ward))
        {
            draft.Error("Key", "WardId cannot be used as PartitionKey.");
        }

        if (serial.Length > 0 && !TableKeyRules.IsValid(serial))
        {
            draft.Error("Key", "WardVoterSerialNumber cannot be used as RowKey.");
        }

        string? storedEpic = null;
        if (epic.Length > 0)
        {
            if (!VoterLookup.IsEpic(epic))
            {
                draft.Error("Epic", $"EpicNumber '{epic}' is not a 3-letter 7-digit EPIC.");
            }
            else
            {
                storedEpic = VoterLookup.NormalizeEpic(epic);
            }
        }

        string? storedAssembly = null;
        if (assembly.Length > 0)
        {
            if (!VoterLookup.IsAssemblyRoll(assembly))
            {
                draft.Error("Assembly", $"AssemblyRollReference '{assembly}' is not an assembly roll reference.");
            }
            else
            {
                storedAssembly = VoterLookup.NormalizeAssemblyRoll(assembly);
            }
        }

        CheckTelugu(draft, VoterCsvColumns.NameTelugu, nameTelugu);
        CheckTelugu(draft, VoterCsvColumns.RelativeNameTelugu, relativeTelugu);
        CheckEnglish(draft, VoterCsvColumns.NameEnglish, nameEnglish);
        CheckEnglish(draft, VoterCsvColumns.RelativeNameEnglish, relativeEnglish);
        RejectControlCharacters(draft, nameTelugu, nameEnglish, relativeTelugu, relativeEnglish, house, section);

        int? age = null;
        if (ageText.Length > 0)
        {
            if (ageText.Any(character => !char.IsDigit(character))
                || !int.TryParse(ageText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                || parsed < MinimumAge
                || parsed > MaximumAge)
            {
                draft.Error("Age", $"Age '{ageText}' must be a whole number from {MinimumAge} to {MaximumAge}.");
            }
            else
            {
                age = parsed;
                if (parsed >= 100)
                {
                    draft.Warning("AgeHigh", $"Age {parsed} is 100 or older. Confirm it against the roll.");
                }
            }
        }

        string? gender = null;
        if (genderText.Length > 0)
        {
            if (!Genders.TryGetValue(genderText, out var canonical))
            {
                draft.Error("Gender", $"Gender '{genderText}' must be Male, Female, or Third Gender.");
            }
            else
            {
                gender = canonical;
            }
        }

        if (house.Length > 0 && normalizedHouse.Length > 0)
        {
            var expected = VoterLookup.NormalizeHouse(house);
            if (!string.Equals(normalizedHouse, expected, StringComparison.Ordinal))
            {
                draft.Error("House", $"NormalizedHouseNumber '{normalizedHouse}' does not match HouseNumber. The normalized value is '{expected}'.");
            }
        }

        if (section.Length > 64)
        {
            draft.Error("Section", "SectionNumber is too long.");
        }

        if (draft.Findings.Any(item => item.Severity == FindingSeverity.Error))
        {
            return draft;
        }

        draft.Entity = VoterEntityFactory.Create(
            ward,
            serial,
            storedEpic ?? "",
            storedAssembly ?? "",
            nameTelugu,
            nameEnglish,
            relativeTelugu,
            relativeEnglish,
            age ?? 0,
            gender ?? "",
            house,
            normalizedHouse,
            section);
        return draft;
    }

    private static (int SerialGroups, int EpicGroups) MarkDuplicates(List<RowDraft> drafts)
    {
        var serialGroups = 0;
        var epicGroups = 0;
        foreach (var group in drafts.Where(draft => draft.Serial.Length > 0 && draft.WardId.Length > 0).GroupBy(draft => draft.WardId + "\n" + draft.Serial))
        {
            var rows = group.ToList();
            if (rows.Count < 2)
            {
                continue;
            }

            serialGroups++;
            var listed = string.Join(", ", rows.Select(draft => draft.Row.RowNumber));
            var message = $"WardVoterSerialNumber '{rows[0].Serial}' is duplicated on rows {listed}.";
            foreach (var draft in rows)
            {
                draft.Error(DuplicateSerial, message);
                draft.Entity = null;
            }
        }

        foreach (var group in drafts.Where(draft => draft.EpicKey.Length > 0 && draft.WardId.Length > 0).GroupBy(draft => draft.WardId + "\n" + draft.EpicKey))
        {
            var rows = group.ToList();
            if (rows.Count < 2)
            {
                continue;
            }

            epicGroups++;
            var listed = string.Join(", ", rows.Select(draft => draft.Row.RowNumber));
            var message = $"EpicNumber '{rows[0].EpicKey}' is duplicated on rows {listed}.";
            foreach (var draft in rows)
            {
                draft.Error(DuplicateEpic, message);
                draft.Entity = null;
            }
        }

        return (serialGroups, epicGroups);
    }

    private static void Require(RowDraft draft, string fieldName, string value)
    {
        if (value.Length == 0)
        {
            draft.Error(Missing, $"{fieldName} is missing.");
        }
    }

    private static void CheckTelugu(RowDraft draft, string fieldName, string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        if (value.Contains('\uFFFD'))
        {
            draft.Error("Telugu", $"{fieldName} contains a Unicode replacement character. Save the CSV as UTF-8.");
            return;
        }

        if (!value.Any(IsTelugu))
        {
            draft.Error("Telugu", $"{fieldName} has no Telugu text.");
        }
    }

    private static void CheckEnglish(RowDraft draft, string fieldName, string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        if (!value.Any(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
        {
            draft.Error("English", $"{fieldName} must be supplied as a Latin transliteration. Telugu text is not translated.");
            return;
        }

        if (value.Any(IsTelugu))
        {
            draft.Warning("EnglishScript", $"{fieldName} contains Telugu characters as well as Latin letters.");
        }
    }

    private static void RejectControlCharacters(RowDraft draft, params string[] values)
    {
        if (values.Any(value => value.Any(char.IsControl)))
        {
            draft.Error("Control", "A field contains a control character.");
        }
    }

    private static bool IsTelugu(char character) => character is >= '\u0C00' and <= '\u0C7F';

    private static string Field(VoterCsvRow row, string name) =>
        row.Fields.TryGetValue(name, out var value) ? value.Trim() : "";

    private sealed class RowDraft
    {
        public RowDraft(VoterCsvRow row) => Row = row;

        public VoterCsvRow Row { get; }

        public List<RowFinding> Findings { get; } = [];

        public string WardId { get; set; } = "";

        public string Serial { get; set; } = "";

        public string EpicKey { get; set; } = "";

        public VoterTableEntity? Entity { get; set; }

        public void Error(string code, string message) => Add(FindingSeverity.Error, code, message);

        public void Warning(string code, string message) => Add(FindingSeverity.Warning, code, message);

        private void Add(FindingSeverity severity, string code, string message)
        {
            Findings.Add(new RowFinding
            {
                RowNumber = Row.RowNumber,
                Severity = severity,
                Code = code,
                Message = message
            });
        }
    }
}

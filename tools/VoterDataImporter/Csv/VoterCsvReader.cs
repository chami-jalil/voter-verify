using System.Text;

namespace VoterDataImporter.Csv;

public sealed class VoterCsvRow
{
    public required int RowNumber { get; init; }

    public required IReadOnlyDictionary<string, string> Fields { get; init; }
}

public sealed class CsvReadResult
{
    public required IReadOnlyList<string> FileErrors { get; init; }

    public required IReadOnlyList<VoterCsvRow> Rows { get; init; }
}

public static class VoterCsvReader
{
    public static CsvReadResult Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CsvReadResult
            {
                FileErrors = ["The CSV is empty."],
                Rows = []
            };
        }

        var source = text.TrimStart('\uFEFF');
        if (!TryParseRecords(source, out var records, out var parseError))
        {
            return new CsvReadResult
            {
                FileErrors = [parseError ?? "The CSV could not be parsed."],
                Rows = []
            };
        }

        if (records.Count == 0)
        {
            return new CsvReadResult
            {
                FileErrors = ["The CSV is empty."],
                Rows = []
            };
        }

        var header = records[0].Select(value => value.Trim()).ToList();
        var headerError = HeaderError(header);
        if (headerError is not null)
        {
            return new CsvReadResult
            {
                FileErrors = [headerError],
                Rows = []
            };
        }

        var rows = new List<VoterCsvRow>();
        for (var index = 1; index < records.Count; index++)
        {
            var values = records[index];
            var trailingBlank = index == records.Count - 1 && values.All(value => value.Trim().Length == 0);
            if (trailingBlank)
            {
                continue;
            }

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var column = 0; column < header.Count; column++)
            {
                fields[header[column]] = column < values.Count ? values[column].Trim() : "";
            }

            if (values.Count > header.Count)
            {
                fields["__extra"] = string.Join(",", values.Skip(header.Count));
            }

            rows.Add(new VoterCsvRow
            {
                RowNumber = rows.Count + 1,
                Fields = fields
            });
        }

        var errors = new List<string>();
        if (rows.Count == 0)
        {
            errors.Add("The CSV has no data rows.");
        }

        return new CsvReadResult
        {
            FileErrors = errors,
            Rows = rows
        };
    }

    private static string? HeaderError(IReadOnlyList<string> header)
    {
        if (header.Count != header.Distinct(StringComparer.Ordinal).Count())
        {
            return "The CSV header contains a duplicate column.";
        }

        var expected = VoterCsvColumns.All.ToHashSet(StringComparer.Ordinal);
        var actual = header.ToHashSet(StringComparer.Ordinal);
        var missing = expected.Where(name => !actual.Contains(name)).ToArray();
        var extra = actual.Where(name => !expected.Contains(name)).ToArray();
        if (missing.Length == 0 && extra.Length == 0)
        {
            return null;
        }

        var parts = new List<string>();
        if (missing.Length > 0)
        {
            parts.Add("missing " + string.Join(", ", missing));
        }

        if (extra.Length > 0)
        {
            parts.Add("unexpected " + string.Join(", ", extra));
        }

        return "The CSV header is not the voter schema (" + string.Join("; ", parts) + ").";
    }

    private static bool TryParseRecords(string text, out List<List<string>> records, out string? error)
    {
        records = [];
        error = null;
        var field = new StringBuilder();
        var record = new List<string>();
        var inQuotes = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            if (character == '"')
            {
                inQuotes = true;
            }
            else if (character == ',')
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if (character == '\n')
            {
                record.Add(field.ToString());
                field.Clear();
                records.Add(record);
                record = [];
            }
            else if (character != '\r')
            {
                field.Append(character);
            }
        }

        if (inQuotes)
        {
            records = [];
            error = "The CSV has an unclosed quote.";
            return false;
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return true;
    }
}

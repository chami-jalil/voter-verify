using VoterDataImporter.Csv;
using VoterDataImporter.Import;
using VoterDataImporter.Validation;
using VoterDataImporter.Verify;
using VoterVerification.Configuration;
using VoterVerification.Data;

namespace VoterDataImporter.Tests;

public class CsvAndValidationTests
{
    [Fact]
    public void ParsesTeluguAndQuotedCommasWithoutTranslating()
    {
        var csv = Header + "\n" +
            "10,1,ABC1234567,175/42/1,\"రాము, గారు\",Ramu,సీత,Sita,34,Male,12-A,12A,1";
        var report = VoterCsvValidator.ValidateCsv(csv);

        Assert.Empty(report.FileErrors);
        var entity = Assert.Single(report.ValidEntities);
        Assert.Equal("రాము, గారు", entity.NameTelugu);
        Assert.Contains("రాము, గారు", entity.NameTelugu);
        Assert.Equal("Ramu", entity.NameEnglish);
        Assert.NotEqual(entity.NameTelugu, entity.NameEnglish);
    }

    [Fact]
    public void SampleFileIsSyntheticAndValid()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "VoterDataImporter", "samples", "ward-sample.csv"));
        var csv = File.ReadAllText(path);
        var report = VoterCsvValidator.ValidateCsv(csv);

        Assert.Equal(2, report.TotalRows);
        Assert.Equal(2, report.ValidRows);
        Assert.Equal(0, report.InvalidRows);
        Assert.Equal("రాము", report.ValidEntities[0].NameTelugu);
        Assert.Equal("లక్ష్మి", report.ValidEntities[1].NameTelugu);
    }

    [Fact]
    public void ReportsMissingFieldsAndDoesNotCreateAnEntity()
    {
        var csv = Header + "\n" + Row(english: "", telugu: "రాము");
        var report = VoterCsvValidator.ValidateCsv(csv);

        Assert.Equal(1, report.TotalRows);
        Assert.Equal(0, report.ValidRows);
        Assert.Equal(1, report.InvalidRows);
        Assert.Equal(1, report.MissingFields);
        Assert.Contains(report.Findings, finding => finding.Message.Contains("NameEnglish is missing", StringComparison.Ordinal));
        Assert.Empty(report.ValidEntities);
    }

    [Fact]
    public void RejectsEnglishThatIsOnlyTelugu()
    {
        var csv = Header + "\n" + Row(english: "రాము");
        var report = VoterCsvValidator.ValidateCsv(csv);

        Assert.Equal(0, report.ValidRows);
        Assert.Contains(report.Findings, finding => finding.Message.Contains("not translated", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsTeluguColumnWithoutTeluguText()
    {
        var csv = Header + "\n" + Row(telugu: "Ramu");
        var report = VoterCsvValidator.ValidateCsv(csv);

        Assert.Contains(report.Findings, finding => finding.Code == "Telugu" && finding.Message.Contains("NameTelugu", StringComparison.Ordinal));
        Assert.Empty(report.ValidEntities);
    }

    [Fact]
    public void DetectsDuplicateSerialsAndEpics()
    {
        var csv = Header + "\n" + Row(serial: "15", epic: "ABC1234567") + "\n" + Row(serial: "15", epic: "abc 1234567", telugu: "సీత", english: "Sita");
        var report = VoterCsvValidator.ValidateCsv(csv);

        Assert.Equal(2, report.TotalRows);
        Assert.Equal(0, report.ValidRows);
        Assert.Equal(1, report.DuplicateSerialNumbers);
        Assert.Equal(1, report.DuplicateEpicNumbers);
        Assert.Equal(2, report.Findings.Count(finding => finding.Code == VoterCsvValidator.DuplicateSerial));
        Assert.Contains("WardVoterSerialNumber '15' is duplicated", report.Format(), StringComparison.Ordinal);
        Assert.Contains("EpicNumber 'ABC1234567' is duplicated", report.Format(), StringComparison.Ordinal);
    }

    [Fact]
    public void ChecksAgeGenderHouseAndAssembly()
    {
        var tooYoung = VoterCsvValidator.ValidateCsv(Header + "\n" + Row(age: "17"));
        Assert.Contains(tooYoung.Findings, finding => finding.Code == "Age");

        var oldEnough = VoterCsvValidator.ValidateCsv(Header + "\n" + Row(age: "100"));
        Assert.Equal(1, oldEnough.ValidRows);
        Assert.Equal(1, oldEnough.Warnings);
        Assert.Equal(100, Assert.Single(oldEnough.ValidEntities).Age);

        var gender = VoterCsvValidator.ValidateCsv(Header + "\n" + Row(gender: "F"));
        Assert.Equal("Female", Assert.Single(gender.ValidEntities).Gender);

        var badGender = VoterCsvValidator.ValidateCsv(Header + "\n" + Row(gender: "Unknown"));
        Assert.Contains(badGender.Findings, finding => finding.Code == "Gender");

        var house = VoterCsvValidator.ValidateCsv(Header + "\n" + Row(normalized: "12-A"));
        Assert.Contains(house.Findings, finding => finding.Message.Contains("12A", StringComparison.Ordinal));

        var assembly = VoterCsvValidator.ValidateCsv(Header + "\n" + Row(assembly: "12-A"));
        Assert.Contains(assembly.Findings, finding => finding.Code == "Assembly");
        Assert.DoesNotContain(assembly.Findings, finding => finding.Code == "House");

        var emptyAssembly = VoterCsvValidator.ValidateCsv(Header + "\n" + Row(assembly: ""));
        Assert.Contains(emptyAssembly.Findings, finding => finding.Code == VoterCsvValidator.Missing && finding.Message.Contains("AssemblyRollReference", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildsPartitionKeyAndRowKeyFromWardAndSerial()
    {
        var report = VoterCsvValidator.ValidateCsv(Header + "\n" + Row(serial: "12-A", epic: "XYZ1234567", assembly: "175 / 42 / 9", epicSpaces: false));
        var entity = Assert.Single(report.ValidEntities);
        Assert.Equal("10", entity.PartitionKey);
        Assert.Equal("", entity.TenantId);
        Assert.Equal("10", entity.WardId);
        Assert.Equal("12-A", entity.RowKey);
        Assert.Equal("12-A", entity.WardVoterSerialNumber);
        Assert.Equal("XYZ1234567", entity.EpicNumber);
        Assert.Equal("175/42/9", entity.AssemblyRollReference);
        Assert.Equal("12A", entity.NormalizedHouseNumber);

        var otherWard = VoterCsvValidator.ValidateCsv(Header + "\n" + Row(ward: "11"));
        Assert.Contains(otherWard.Findings, finding => finding.Code == "Ward");

        var slashSerial = VoterCsvValidator.ValidateCsv(Header + "\n" + Row(serial: "175/42/1"));
        Assert.Contains(slashSerial.Findings, finding => finding.Message.Contains("RowKey", StringComparison.Ordinal));
        Assert.Empty(slashSerial.ValidEntities);
    }

    [Fact]
    public void FactoryRejectsAnIllegalKey()
    {
        Assert.Throws<InvalidOperationException>(() => VoterEntityFactory.Create(
            "10/1", "1", "ABC1234567", "175/42/1", "రాము", "Ramu", "సీత", "Sita", 34, "Male", "12-A", "12A", "1"));
    }

    [Fact]
    public void HeaderProblemsAreFileErrors()
    {
        var missing = VoterCsvValidator.ValidateCsv("WardId,NameTelugu\n10,రాము");
        Assert.Empty(missing.ValidEntities);
        Assert.Contains(missing.FileErrors, error => error.Contains("missing", StringComparison.Ordinal));

        var broken = VoterCsvReader.Read("WardId,\"unterminated");
        Assert.Contains(broken.FileErrors, error => error.Contains("unclosed", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(broken.Rows);
    }

    private const string Header =
        "WardId,WardVoterSerialNumber,EpicNumber,AssemblyRollReference,NameTelugu,NameEnglish,RelativeNameTelugu,RelativeNameEnglish,Age,Gender,HouseNumber,NormalizedHouseNumber,SectionNumber";

    private static string Row(
        string serial = "1",
        string epic = "ABC1234567",
        string assembly = "175/42/1",
        string telugu = "రాము",
        string english = "Ramu",
        string relativeTelugu = "సీత",
        string relativeEnglish = "Sita",
        string age = "34",
        string gender = "Male",
        string house = "12-A",
        string normalized = "12A",
        string section = "1",
        string ward = "10",
        bool epicSpaces = false)
    {
        if (epicSpaces)
        {
            epic = "ABC 1234567";
        }

        return string.Join(',', [ward, serial, epic, assembly, telugu, english, relativeTelugu, relativeEnglish, age, gender, house, normalized, section]);
    }
}

public class ImportBehaviorTests
{
    [Fact]
    public async Task DryRunDoesNotInsert()
    {
        var store = new MemoryVoterTableStore();
        var result = await ImportRunner.RunAsync(new ImportRequest
        {
            CsvText = TwoVoters(),
            Write = false,
            AccountLabel = "wardvoters",
            LegacyPartition = true
        }, store, _ => true, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.ConfirmationAccepted);
        Assert.Equal(0, store.InsertCalls);
        Assert.Empty(store.Rows);
        Assert.Contains("No records were written", result.Text, StringComparison.Ordinal);
        Assert.Contains("Would insert: 2", result.Text, StringComparison.Ordinal);
        Assert.True(result.AzureChecked);
    }

    [Fact]
    public async Task DryRunWithoutAStoreDoesNotContactAzure()
    {
        var result = await ImportRunner.RunAsync(new ImportRequest
        {
            CsvText = TwoVoters(),
            Write = false,
            LegacyPartition = true
        }, null, _ => true, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.AzureChecked);
        Assert.Contains("Azure was not contacted", result.Text, StringComparison.Ordinal);
        Assert.Null(result.WriteResult);
    }

    [Fact]
    public async Task TenantImportUsesASeparatePartitionAndDoesNotWriteDuringDryRun()
    {
        var store = new MemoryVoterTableStore();
        var result = await ImportRunner.RunAsync(new ImportRequest
        {
            CsvText = TwoVoters(),
            Write = false,
            TenantId = "TENANT001"
        }, store, _ => true, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, store.InsertCalls);
        Assert.All(result.Report.ValidEntities, entity =>
        {
            Assert.Equal("TENANT001|10", entity.PartitionKey);
            Assert.Equal("10", entity.WardId);
            Assert.Equal("TENANT001", entity.TenantId);
        });
    }

    [Fact]
    public async Task MissingOrCombinedPartitionOptionsWriteNothing()
    {
        var store = new MemoryVoterTableStore();
        var missing = await ImportRunner.RunAsync(new ImportRequest
        {
            CsvText = TwoVoters(),
            Write = true,
            AccountLabel = "wardvoters"
        }, store, _ => true, CancellationToken.None);
        var both = await ImportRunner.RunAsync(new ImportRequest
        {
            CsvText = TwoVoters(),
            Write = true,
            AccountLabel = "wardvoters",
            TenantId = "TENANT001",
            LegacyPartition = true
        }, store, _ => true, CancellationToken.None);

        Assert.Equal(2, missing.ExitCode);
        Assert.Equal(2, both.ExitCode);
        Assert.False(missing.AzureChecked);
        Assert.False(both.AzureChecked);
        Assert.Equal(0, store.InsertCalls);
        Assert.Empty(store.Rows);
        Assert.Contains("exactly one", missing.Text, StringComparison.Ordinal);
        Assert.Contains("exactly one", both.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyPartitionImportKeepsTheWardKeyAndDoesNotRewriteTenant()
    {
        var store = new MemoryVoterTableStore();
        var result = await ImportRunner.RunAsync(new ImportRequest
        {
            CsvText = TwoVoters(),
            Write = true,
            AccountLabel = "wardvoters",
            LegacyPartition = true
        }, store, _ => true, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, result.WriteResult!.Inserted.Count);
        Assert.All(store.Rows.Values, entity =>
        {
            Assert.Equal("10", entity.PartitionKey);
            Assert.Equal("10", entity.WardId);
            Assert.Equal("", entity.TenantId);
        });
    }

    [Fact]
    public async Task ExistingDifferentRowIsLeftUnchanged()
    {
        var store = new MemoryVoterTableStore();
        var first = VoterCsvValidator.ValidateCsv(Header + "\n" + OneVoter(english: "Kept")).ValidEntities[0];
        store.Rows[ImportPlanner.Key(first)] = Clone(first);

        var confirmed = 0;
        var result = await ImportRunner.RunAsync(new ImportRequest
        {
            CsvText = Header + "\n" + OneVoter(english: "Changed") + "\n" + SecondVoter(),
            Write = true,
            AccountLabel = "wardvoters",
            LegacyPartition = true
        }, store, _ =>
        {
            confirmed++;
            return true;
        }, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, confirmed);
        Assert.Equal("Kept", store.Rows[ImportPlanner.Key(first)].NameEnglish);
        Assert.Equal(2, store.Rows.Count);
        Assert.Single(result.Plan!.DifferentExisting);
        Assert.Contains("stored voter was kept", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecondRunInsertsNothing()
    {
        var store = new MemoryVoterTableStore();
        var csv = TwoVoters();
        var first = await ImportRunner.RunAsync(new ImportRequest
        {
            CsvText = csv,
            Write = true,
            AccountLabel = "wardvoters",
            LegacyPartition = true
        }, store, _ => true, CancellationToken.None);

        Assert.Equal(2, first.WriteResult!.Inserted.Count);
        Assert.Equal(2, store.Rows.Count);

        var second = await ImportRunner.RunAsync(new ImportRequest
        {
            CsvText = csv,
            Write = true,
            AccountLabel = "wardvoters",
            LegacyPartition = true
        }, store, _ => true, CancellationToken.None);

        Assert.Equal(0, second.ExitCode);
        Assert.Null(second.WriteResult);
        Assert.Equal(2, second.Plan!.AlreadyStored.Count);
        Assert.Empty(second.Plan.ToInsert);
        Assert.Equal(2, store.Rows.Count);
        Assert.Equal(1, store.InsertCalls);
        Assert.Contains("Nothing new to insert", second.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeclinedConfirmationWritesNothing()
    {
        var store = new MemoryVoterTableStore();
        var result = await ImportRunner.RunAsync(new ImportRequest
        {
            CsvText = TwoVoters(),
            Write = true,
            AccountLabel = "wardvoters",
            LegacyPartition = true
        }, store, _ => false, CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(0, store.InsertCalls);
        Assert.Contains("Nothing was written", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidRowsBlockTheWrite()
    {
        var store = new MemoryVoterTableStore();
        var asked = false;
        var result = await ImportRunner.RunAsync(new ImportRequest
        {
            CsvText = Header + "\n" + "10,1,ABC1234567,175/42/1,Ramu,Ramu,సీత,Sita,34,Male,12-A,12A,1",
            Write = true,
            AccountLabel = "wardvoters"
        }, store, _ =>
        {
            asked = true;
            return true;
        }, CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.False(asked);
        Assert.Equal(0, store.InsertCalls);
        Assert.Contains("Invalid rows were not imported", result.Text, StringComparison.Ordinal);
        Assert.Contains("Row 1 error:", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BatchesStayInsideOnePartitionAndTheTransactionLimit()
    {
        var submitted = new List<(string Partition, int Count)>();
        var entities = Enumerable.Range(1, 101)
            .Select(index => Entity(index.ToString(), "10"))
            .Concat(Enumerable.Range(1, 2).Select(index => Entity("b" + index, "11")))
            .ToList();

        var result = await VoterBatchInserter.InsertAsync(entities, (chunk, _) =>
        {
            submitted.Add((chunk[0].PartitionKey, chunk.Count));
            Assert.Single(chunk.Select(entity => entity.PartitionKey).Distinct());
            Assert.InRange(chunk.Count, 1, VoterBatchInserter.MaxBatchSize);
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal(103, result.Inserted.Count);
        Assert.Equal([("10", 100), ("10", 1), ("11", 2)], submitted);
    }

    [Fact]
    public async Task FailedBatchReportsTheConflictingRowAndInsertsTheOthers()
    {
        var result = await VoterBatchInserter.InsertAsync(
            [Entity("1", "10"), Entity("2", "10"), Entity("3", "10")],
            (chunk, _) =>
            {
                var index = chunk.ToList().FindIndex(entity => entity.RowKey == "2");
                if (index >= 0)
                {
                    throw new VoterBatchSubmitException(409, index);
                }

                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(["1", "3"], result.Inserted.Select(entity => entity.RowKey).OrderBy(value => value).ToArray());
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal("2", skipped.Entity.RowKey);
        Assert.Empty(result.Failed);
    }

    [Fact]
    public void AccountNameRejectsKeysAndConnectionStrings()
    {
        Assert.True(AzureVoterTableStore.IsAccountName("wardvoters"));
        Assert.False(AzureVoterTableStore.IsAccountName("DefaultEndpointsProtocol=https;AccountKey=secret"));
        Assert.False(AzureVoterTableStore.IsAccountName("https://wardvoters.table.core.windows.net"));
    }

    [Fact]
    public void ImportFlagIsExplicitAndVerificationDoesNotTakeACsv()
    {
        Assert.False(ImporterOptions.Parse(["--csv", "voters.csv", "--legacy-partition"]).Import);
        Assert.NotNull(ImporterOptions.Parse(["--csv", "voters.csv"]).Error);
        Assert.True(ImporterOptions.Parse(["--import", "--csv", "voters.csv", "--account", "wardvoters", "--legacy-partition"]).Import);
        Assert.NotNull(ImporterOptions.Parse(["--import", "--csv", "voters.csv", "--account", "wardvoters"]).Error);
        Assert.NotNull(ImporterOptions.Parse(["--import", "--csv", "voters.csv", "--tenant", "TENANT001", "--legacy-partition"]).Error);
        Assert.NotNull(ImporterOptions.Parse(["--import", "--verify", "--legacy-partition"]).Error);
        Assert.NotNull(ImporterOptions.Parse(["--account", "AccountKey=secret"]).Error);
    }

    [Fact]
    public void WriterOnlyAddsToTheVotersTable()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "VoterDataImporter"));
        var store = File.ReadAllText(Path.Combine(root, "Import", "AzureVoterTableStore.cs"));
        var program = File.ReadAllText(Path.Combine(root, "Program.cs"));
        Assert.Contains("TableTransactionActionType.Add", store, StringComparison.Ordinal);
        Assert.Contains("AppConfiguration.VotersTableName", store, StringComparison.Ordinal);
        Assert.DoesNotContain("TableTransactionActionType.Update", store, StringComparison.Ordinal);
        Assert.DoesNotContain("TableTransactionActionType.Upsert", store, StringComparison.Ordinal);
        Assert.DoesNotContain("TableTransactionActionType.Delete", store, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateEntity", store, StringComparison.Ordinal);
        Assert.DoesNotContain("UpsertEntity", store, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteEntity", store, StringComparison.Ordinal);
        Assert.DoesNotContain("VoterVerifications", store, StringComparison.Ordinal);
        Assert.Contains("DefaultAzureCredential", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AccountKey=", program, StringComparison.Ordinal);

        var maui = File.ReadAllText(Path.GetFullPath(Path.Combine(root, "..", "..", "VoterVerification.csproj")));
        Assert.Contains("Compile Remove=\"tools/**\"", maui, StringComparison.Ordinal);
    }

    private const string Header =
        "WardId,WardVoterSerialNumber,EpicNumber,AssemblyRollReference,NameTelugu,NameEnglish,RelativeNameTelugu,RelativeNameEnglish,Age,Gender,HouseNumber,NormalizedHouseNumber,SectionNumber";

    private static string TwoVoters() => Header + "\n" + OneVoter() + "\n" + SecondVoter();

    private static string OneVoter(string english = "Ramu") =>
        $"10,1,ABC1234567,175/42/1,రాము,{english},సీత,Sita,34,Male,12-A,12A,1";

    private static string SecondVoter() =>
        "10,2,DEF7654321,175/42/2,లక్ష్మి,Lakshmi,రాము,Ramu,29,Female,4/1,41,1";

    private static VoterTableEntity Entity(string serial, string ward) => new()
    {
        PartitionKey = ward,
        RowKey = serial,
        WardId = ward,
        WardVoterSerialNumber = serial
    };

    private static VoterTableEntity Clone(VoterTableEntity entity) => new()
    {
        PartitionKey = entity.PartitionKey,
        RowKey = entity.RowKey,
        WardId = entity.WardId,
        WardVoterSerialNumber = entity.WardVoterSerialNumber,
        EpicNumber = entity.EpicNumber,
        AssemblyRollReference = entity.AssemblyRollReference,
        NameTelugu = entity.NameTelugu,
        NameEnglish = entity.NameEnglish,
        RelativeNameTelugu = entity.RelativeNameTelugu,
        RelativeNameEnglish = entity.RelativeNameEnglish,
        Age = entity.Age,
        Gender = entity.Gender,
        HouseNumber = entity.HouseNumber,
        NormalizedHouseNumber = entity.NormalizedHouseNumber,
        SectionNumber = entity.SectionNumber,
        TenantId = entity.TenantId
    };
}

public class WardVerificationTests
{
    [Fact]
    public void MatchingCountAloneIsNotSuccess()
    {
        var stored = VoterCsvValidator.ValidateCsv(Sample()).ValidEntities.ToList();
        var wrongTotal = WardVerifier.Check(stored, AppConfiguration.Ward10Id, AppConfiguration.Ward10ExpectedVoters);
        Assert.False(wrongTotal.IsSuccessful);
        Assert.Equal(2, wrongTotal.Total);
        Assert.Equal(7125, wrongTotal.Expected);
        Assert.Contains("Verification failed", wrongTotal.Format(), StringComparison.Ordinal);

        stored[0].NameTelugu = "";
        var sameCount = WardVerifier.Check([stored[0]], "10", 1);
        Assert.True(sameCount.CountMatches);
        Assert.False(sameCount.IsSuccessful);
        Assert.True(sameCount.InvalidRows > 0);
        Assert.Equal(1, sameCount.MissingFields);
        Assert.Contains("Missing required fields: 1", sameCount.Format(), StringComparison.Ordinal);
    }

    [Fact]
    public void ValidStoredRowsPassAndDuplicatesDoNot()
    {
        var stored = VoterCsvValidator.ValidateCsv(Sample()).ValidEntities.ToList();
        var passed = WardVerifier.Check(stored, "10", 2);
        Assert.True(passed.IsSuccessful);
        Assert.Equal(0, passed.DuplicateSerialNumbers);
        Assert.Equal(0, passed.DuplicateEpicNumbers);
        Assert.Equal(0, passed.InvalidPartitionKeys);
        Assert.Equal(0, passed.InvalidRowKeys);

        stored[1].EpicNumber = stored[0].EpicNumber;
        var duplicated = WardVerifier.Check(stored, "10", 2);
        Assert.False(duplicated.IsSuccessful);
        Assert.Equal(1, duplicated.DuplicateEpicNumbers);
        Assert.Contains("Duplicate EPIC count: 1", duplicated.Format(), StringComparison.Ordinal);

        stored[1].EpicNumber = "DEF7654321";
        stored[1].RowKey = "9";
        var keys = WardVerifier.Check(stored, "10", 2);
        Assert.Equal(1, keys.InvalidRowKeys);
        Assert.Equal(0, keys.InvalidPartitionKeys);
        Assert.True(keys.KeyMismatches > 0);
        Assert.False(keys.IsSuccessful);
        Assert.Contains("Invalid RowKey count: 1", keys.Format(), StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultVerificationReportOmitsPersonalFields()
    {
        var stored = VoterCsvValidator.ValidateCsv(Sample()).ValidEntities.ToList();
        stored[0].PartitionKey = "10/1";
        stored[1].WardVoterSerialNumber = stored[0].WardVoterSerialNumber;
        var report = WardVerifier.Check(stored, "10", 2);
        var text = report.Format();

        Assert.False(report.IsSuccessful);
        Assert.Equal(2, report.Total);
        Assert.Equal(2, report.Expected);
        Assert.Equal(1, report.DuplicateSerialNumbers);
        Assert.Equal(1, report.InvalidPartitionKeys);
        Assert.Equal(0, report.MissingFields);
        Assert.Contains("Expected record count: 2", text, StringComparison.Ordinal);
        Assert.Contains("Actual stored record count: 2", text, StringComparison.Ordinal);
        Assert.Contains("Duplicate serial count: 1", text, StringComparison.Ordinal);
        Assert.Contains("Invalid PartitionKey count: 1", text, StringComparison.Ordinal);
        Assert.Contains("Verification failed", text, StringComparison.Ordinal);

        Assert.DoesNotContain("రాము", text, StringComparison.Ordinal);
        Assert.DoesNotContain("లక్ష్మి", text, StringComparison.Ordinal);
        Assert.DoesNotContain("సీత", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Ramu", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakshmi", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Sita", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ABC1234567", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DEF7654321", text, StringComparison.Ordinal);
        Assert.DoesNotContain("12-A", text, StringComparison.Ordinal);
        Assert.DoesNotContain("4/1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Male", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Female", text, StringComparison.Ordinal);
        Assert.DoesNotContain("34", text, StringComparison.Ordinal);
        Assert.DoesNotContain("29", text, StringComparison.Ordinal);
        Assert.DoesNotContain("10/1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Sample records", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentationDistinguishesTransliterationFromTranslation()
    {
        var readme = File.ReadAllText(Path.Combine(ImporterRoot(), "README.md"));

        Assert.Contains("NameTelugu` stores the original Telugu name exactly as supplied", readme, StringComparison.Ordinal);
        Assert.Contains("NameEnglish` stores the English-script transliteration", readme, StringComparison.Ordinal);
        Assert.Contains("RelativeNameTelugu` stores the original Telugu relative name", readme, StringComparison.Ordinal);
        Assert.Contains("RelativeNameEnglish` stores its English-script transliteration", readme, StringComparison.Ordinal);
        Assert.Contains("never translated by meaning", readme, StringComparison.Ordinal);
        Assert.Contains("Different English spellings may be valid", readme, StringComparison.Ordinal);
        Assert.Contains("manually correctable", readme, StringComparison.Ordinal);
        Assert.Contains("does not silently generate or overwrite transliterations", readme, StringComparison.Ordinal);
        Assert.Contains("Raamu", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("translated by meaning into English", readme, StringComparison.Ordinal);
    }

    private static string ImporterRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "VoterDataImporter"));

    private static string Sample() =>
        """
        WardId,WardVoterSerialNumber,EpicNumber,AssemblyRollReference,NameTelugu,NameEnglish,RelativeNameTelugu,RelativeNameEnglish,Age,Gender,HouseNumber,NormalizedHouseNumber,SectionNumber
        10,1,ABC1234567,175/42/1,రాము,Ramu,సీత,Sita,34,Male,12-A,12A,1
        10,2,DEF7654321,175/42/2,లక్ష్మి,Lakshmi,రాము,Ramu,29,Female,4/1,41,1
        """;
}

file sealed class MemoryVoterTableStore : IVoterTableStore
{
    public Dictionary<string, VoterTableEntity> Rows { get; } = new(StringComparer.Ordinal);

    public int InsertCalls { get; private set; }

    public Task<IReadOnlyList<VoterTableEntity>> ReadPartitionAsync(string wardId, CancellationToken cancellationToken)
    {
        IReadOnlyList<VoterTableEntity> rows = Rows.Values
            .Where(entity => entity.PartitionKey == wardId)
            .Select(Clone)
            .ToList();
        return Task.FromResult(rows);
    }

    public Task<BatchWriteResult> InsertAsync(IReadOnlyList<VoterTableEntity> entities, CancellationToken cancellationToken)
    {
        InsertCalls++;
        return VoterBatchInserter.InsertAsync(entities, (chunk, _) =>
        {
            var index = chunk.ToList().FindIndex(entity => Rows.ContainsKey(ImportPlanner.Key(entity)));
            if (index >= 0)
            {
                throw new VoterBatchSubmitException(409, index);
            }

            foreach (var entity in chunk)
            {
                Rows[ImportPlanner.Key(entity)] = Clone(entity);
            }

            return Task.CompletedTask;
        }, cancellationToken);
    }

    private static VoterTableEntity Clone(VoterTableEntity entity) => new()
    {
        PartitionKey = entity.PartitionKey,
        RowKey = entity.RowKey,
        WardId = entity.WardId,
        WardVoterSerialNumber = entity.WardVoterSerialNumber,
        EpicNumber = entity.EpicNumber,
        AssemblyRollReference = entity.AssemblyRollReference,
        NameTelugu = entity.NameTelugu,
        NameEnglish = entity.NameEnglish,
        RelativeNameTelugu = entity.RelativeNameTelugu,
        RelativeNameEnglish = entity.RelativeNameEnglish,
        Age = entity.Age,
        Gender = entity.Gender,
        HouseNumber = entity.HouseNumber,
        NormalizedHouseNumber = entity.NormalizedHouseNumber,
        SectionNumber = entity.SectionNumber,
        TenantId = entity.TenantId
    };
}

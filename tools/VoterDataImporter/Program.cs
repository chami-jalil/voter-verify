using Azure.Identity;
using VoterDataImporter.Import;
using VoterDataImporter.Verify;
using VoterVerification.Configuration;
using VoterVerification.Helpers;

var options = ImporterOptions.Parse(args);
if (options.Help)
{
    Console.WriteLine(ImporterOptions.Usage);
    return 0;
}

if (options.Error is not null)
{
    Console.Error.WriteLine(options.Error);
    Console.Error.WriteLine(ImporterOptions.Usage);
    return 2;
}

try
{
    if (options.Verify)
    {
        var account = RequireAccount(options);
        var expected = options.Expected
            ?? (string.Equals(options.WardId, AppConfiguration.Ward10Id, StringComparison.Ordinal)
                ? AppConfiguration.Ward10ExpectedVoters
                : throw new InvalidOperationException("Pass --expected for a ward other than Ward 10."));
        var wardStore = CreateStore(account);
        var partition = options.LegacyPartition
            ? options.WardId
            : TenantPartitions.For(options.TenantId, options.WardId);
        var stored = await wardStore.ReadPartitionAsync(partition, CancellationToken.None);
        var check = WardVerifier.Check(stored, options.WardId, expected, options.LegacyPartition ? null : options.TenantId);
        Console.WriteLine(check.Format());
        return check.IsSuccessful ? 0 : 1;
    }

    var csvPath = options.CsvPath ?? throw new InvalidOperationException("Pass --csv with the voter file.");
    var csv = await File.ReadAllTextAsync(csvPath);
    IVoterTableStore? store = null;
    var accountLabel = options.Account ?? Environment.GetEnvironmentVariable("VOTER_STORAGE_ACCOUNT") ?? "";
    if (!string.IsNullOrWhiteSpace(accountLabel))
    {
        store = CreateStore(accountLabel);
    }
    else if (options.Import)
    {
        Console.Error.WriteLine("Import needs --account or VOTER_STORAGE_ACCOUNT. Nothing was written.");
        return 2;
    }

    var result = await ImportRunner.RunAsync(
        new ImportRequest
        {
            CsvText = csv,
            WardId = options.WardId,
            Write = options.Import,
            AccountLabel = accountLabel,
            TenantId = options.TenantId,
            LegacyPartition = options.LegacyPartition
        },
        store,
        confirm: message =>
        {
            Console.WriteLine(message);
            Console.Write("> ");
            return string.Equals(Console.ReadLine()?.Trim(), "IMPORT", StringComparison.Ordinal);
        },
        CancellationToken.None);
    Console.WriteLine(result.Text);
    return result.ExitCode;
}
catch (Exception ex)
{
    Console.Error.WriteLine(SafeFailure(ex));
    return 1;
}

static string RequireAccount(ImporterOptions options)
{
    var account = options.Account ?? Environment.GetEnvironmentVariable("VOTER_STORAGE_ACCOUNT");
    if (!AzureVoterTableStore.IsAccountName(account))
    {
        throw new InvalidOperationException("Pass --account or VOTER_STORAGE_ACCOUNT with the storage account name only.");
    }

    return account!;
}

static AzureVoterTableStore CreateStore(string account)
{
    var tenant = Environment.GetEnvironmentVariable("VOTER_TENANT_ID");
    var credentialOptions = new DefaultAzureCredentialOptions
    {
        ExcludeInteractiveBrowserCredential = true
    };
    if (!string.IsNullOrWhiteSpace(tenant))
    {
        if (!Guid.TryParse(tenant, out _))
        {
            throw new InvalidOperationException("VOTER_TENANT_ID must be an Entra tenant id.");
        }

        credentialOptions.TenantId = tenant;
    }

    return new AzureVoterTableStore(account, new DefaultAzureCredential(credentialOptions));
}

static string SafeFailure(Exception exception)
{
    var message = exception.Message ?? "";
    if (message.Contains("sig=", StringComparison.OrdinalIgnoreCase)
        || message.Contains("AccountKey", StringComparison.OrdinalIgnoreCase)
        || message.Contains("SharedAccessSignature", StringComparison.OrdinalIgnoreCase))
    {
        return exception.GetType().Name;
    }

    var line = message.Split('\n', 2)[0].Trim();
    return line.Length is 0 or > 300
        ? exception.GetType().Name
        : $"{exception.GetType().Name}: {line}";
}

public sealed class ImporterOptions
{
    public const string Usage =
        """
        Voter data importer for the Voters table.

          dotnet run --project tools/VoterDataImporter -- --csv <file> --legacy-partition
          dotnet run --project tools/VoterDataImporter -- --csv <file> --account <name> --tenant <customer>
          dotnet run --project tools/VoterDataImporter -- --import --csv <file> --account <name> --legacy-partition
          dotnet run --project tools/VoterDataImporter -- --import --csv <file> --account <name> --tenant <customer>
          dotnet run --project tools/VoterDataImporter -- --verify --account <name> --legacy-partition

        The default is a dry run and does not write. --import asks you to type IMPORT
        before inserting. Pass exactly one of --tenant or --legacy-partition.
        Authentication uses DefaultAzureCredential (sign in with Azure CLI).
        Do not pass a storage key or SAS.
        """;

    public string? CsvPath { get; init; }

    public string? Account { get; init; }

    public string WardId { get; init; } = AppConfiguration.Ward10Id;

    public string TenantId { get; init; } = "";

    public bool LegacyPartition { get; init; }

    public int? Expected { get; init; }

    public bool Import { get; init; }

    public bool Verify { get; init; }

    public bool Help { get; init; }

    public string? Error { get; init; }

    public static ImporterOptions Parse(string[] args)
    {
        try
        {
            return ParseArgs(args);
        }
        catch (InvalidOperationException ex)
        {
            return new ImporterOptions { Error = ex.Message };
        }
    }

    private static ImporterOptions ParseArgs(string[] args)
    {
        string? csv = null;
        string? account = null;
        string? ward = null;
        string? tenant = null;
        int? expected = null;
        var import = false;
        var legacyPartition = false;
        var verify = false;
        var help = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help" or "-h":
                    help = true;
                    break;
                case "--import":
                    import = true;
                    break;
                case "--verify":
                    verify = true;
                    break;
                case "--csv":
                    csv = Next(args, ref index, arg);
                    break;
                case "--account":
                    account = Next(args, ref index, arg);
                    break;
                case "--ward":
                    ward = Next(args, ref index, arg);
                    break;
                case "--tenant":
                    tenant = Next(args, ref index, arg);
                    break;
                case "--legacy-partition":
                    legacyPartition = true;
                    break;
                case "--expected":
                    var raw = Next(args, ref index, arg);
                    if (!int.TryParse(raw, out var parsed) || parsed < 0)
                    {
                        return new ImporterOptions { Error = "--expected must be a whole number." };
                    }

                    expected = parsed;
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        return new ImporterOptions { Error = $"Unknown argument '{arg}'." };
                    }

                    csv ??= arg;
                    break;
            }
        }

        if (help)
        {
            return new ImporterOptions { Help = true };
        }

        if (import && verify)
        {
            return new ImporterOptions { Error = "Use either --import or --verify." };
        }

        if (verify && csv is not null)
        {
            return new ImporterOptions { Error = "Verification reads Azure. Do not pass --csv." };
        }

        var wardId = string.IsNullOrWhiteSpace(ward) ? AppConfiguration.Ward10Id : ward.Trim();
        if (!TableKeyRules.IsValid(wardId))
        {
            return new ImporterOptions { Error = "The ward id cannot be used as a table key." };
        }

        if (account is not null && !AzureVoterTableStore.IsAccountName(account))
        {
            return new ImporterOptions { Error = "--account must be the storage account name only." };
        }

        var tenantId = tenant?.Trim() ?? "";
        if (tenantId.Length > 0 && !TenantPartitions.IsCustomerTenantId(tenantId))
        {
            return new ImporterOptions { Error = "--tenant must be the customer account id, not an Entra directory id or a storage secret." };
        }

        if (legacyPartition == tenantId.Length > 0)
        {
            return new ImporterOptions { Error = "Pass exactly one of --tenant or --legacy-partition." };
        }

        return new ImporterOptions
        {
            CsvPath = csv,
            Account = account,
            WardId = wardId,
            TenantId = tenantId,
            LegacyPartition = legacyPartition,
            Expected = expected,
            Import = import,
            Verify = verify
        };
    }

    private static string Next(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
        {
            throw new InvalidOperationException($"{name} needs a value.");
        }

        index++;
        return args[index];
    }
}

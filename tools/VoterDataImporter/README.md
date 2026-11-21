# Voter data importer

Console tool that validates a UTF-8 CSV and inserts new rows into the existing `Voters` table. The Android app reads voters through Azure Functions. This tool still writes with the operator's Azure CLI credential and does not use a storage key.

The tool does not write `VoterVerifications`, delete rows, or replace an existing voter. Running the same file again inserts nothing and leaves stored rows as they are.

Do not commit real voter rolls, PDFs, phone numbers, credentials, or production exports. The sample file is fictitious.

## CSV format

The header row must contain these columns. Column order does not matter. Save the file as UTF-8.

`NameTelugu` stores the original Telugu name exactly as supplied. `NameEnglish` stores the English-script transliteration of that name. `RelativeNameTelugu` stores the original Telugu relative name. `RelativeNameEnglish` stores its English-script transliteration.

Names must be transliterated, never translated by meaning. Different English spellings may be valid. English transliterations must be manually correctable in the CSV. The importer does not silently generate or overwrite transliterations: it stores the supplied text and does not replace an existing voter row.

In the fictitious sample below, `రాము` stays `రాము`. `Ramu` is a transliteration of that name. `Raamu` is another valid spelling when a person supplies it. Replacing the name with a different English word of similar meaning is a translation, not a transliteration.

| Column | Rule |
| --- | --- |
| `WardId` | Ward being imported. The default run is ward `10`. With `--legacy-partition`, this becomes `PartitionKey`. |
| `WardVoterSerialNumber` | Unique inside the ward. This becomes `RowKey`. `/`, `\`, `#`, and `?` are not allowed. |
| `EpicNumber` | Three letters and seven digits. Spaces are removed and letters are uppercased before insert. |
| `AssemblyRollReference` | Three or more numeric parts, such as `175/42/1`. Spaces are removed and `-` is stored as `/`. |
| `NameTelugu` | Original Telugu name, stored exactly as supplied. |
| `NameEnglish` | English-script transliteration of `NameTelugu`, entered and corrected by a person. |
| `RelativeNameTelugu` | Original Telugu relative name, stored exactly as supplied. |
| `RelativeNameEnglish` | English-script transliteration of `RelativeNameTelugu`, entered and corrected by a person. |
| `Age` | Whole number from 18 to 120. 100 and older is a warning. |
| `Gender` | `Male`, `Female`, or `Third Gender`. `M`, `F`, and `TG` are accepted. Telugu labels `పురుషుడు`, `స్త్రీ`, and `ఇతరులు` are kept. |
| `HouseNumber` | As printed, for example `12-A`. |
| `NormalizedHouseNumber` | Uppercase letters and digits from the house number. `12-A` must be `12A`. A mismatch is an error. |
| `SectionNumber` | Required section. |

Surrounding whitespace is trimmed. Telugu characters are not rewritten. English name text is not generated, normalized, or overwritten.

Fictitious example, also stored at `samples/ward-sample.csv`:

```csv
WardId,WardVoterSerialNumber,EpicNumber,AssemblyRollReference,NameTelugu,NameEnglish,RelativeNameTelugu,RelativeNameEnglish,Age,Gender,HouseNumber,NormalizedHouseNumber,SectionNumber
10,1,ABC1234567,175/42/1,రాము,Ramu,సీత,Sita,34,Male,12-A,12A,1
10,2,DEF7654321,175/42/2,లక్ష్మి,Lakshmi,రాము,Ramu,29,Female,4/1,41,1
```

Invalid rows stay in the validation report. They are not dropped quietly, and they are not inserted. When any row is invalid, the import writes nothing.

Duplicate serials or duplicate EPIC numbers inside the ward make every involved row invalid.

## Authenticate

Sign in as an administrator with Azure CLI. The tool uses `DefaultAzureCredential` from the Azure storage client libraries and requests a Storage token. It never reads a storage key, SAS, or client secret.

```bash
az login --tenant <tenant-guid>
export VOTER_STORAGE_ACCOUNT=<account-name>
export VOTER_TENANT_ID=<tenant-guid>
```

`VOTER_STORAGE_ACCOUNT` is the account name only, such as `wardvoters`. You can pass `--account` instead. `VOTER_TENANT_ID` is optional.

The signed-in user needs data-plane access on the `Voters` table: entity read and entity add. The built-in role Storage Table Data Contributor includes delete; this tool does not delete. A custom role with `entities/read` and `entities/write` on `Voters` is enough. The tool always writes that table and has no argument that can point it at `VoterVerifications`.

Create the table before importing. This tool does not create it.

## Validate and dry-run

Dry run is the default. With no account, it only validates the file and does not contact Azure.

```bash
dotnet run --project tools/VoterDataImporter -- --csv path/to/voters.csv --legacy-partition
```

Pass the account when you also want existing `PartitionKey` + `RowKey` values checked. This still writes nothing.

```bash
dotnet run --project tools/VoterDataImporter -- --csv path/to/voters.csv --account <account-name> --legacy-partition
```

The report lists total rows, valid rows, invalid rows, duplicate serials, duplicate EPIC numbers, missing fields, warnings, and each row problem. It then shows how many new rows would be inserted, how many already match, and which existing rows differ and would be left unchanged.

## Import

`--import` is required before any write. The tool then asks you to type `IMPORT`. Declining writes nothing. Only validated rows that are not already stored are sent, in batches of at most 100 entities, grouped by `PartitionKey`. Inserts use table `Add`. A conflict is reported and the stored row is kept. A failed batch is split so the failing row can be reported on its own.

```bash
dotnet run --project tools/VoterDataImporter -- --import --csv path/to/voters.csv --account <account-name> --legacy-partition
```

A second run of the same file reports the rows as already stored and inserts nothing.

## Customer tenant

Every import and verification must pass exactly one partition choice. Passing neither, or passing both, stops before Azure is contacted and writes nothing.

`--legacy-partition` is the only way to add rows to an existing ward partition such as Ward 10. `PartitionKey` stays `10`, and `TenantId` stays blank. Rows already stored there are not read back and rewritten.

`--tenant TENANT001` is the customer import. The tool sets `TenantId` to that customer id and `PartitionKey` to `TENANT001|10` while `WardId` stays `10`. The customer id is an application account, not an Entra directory id. It cannot contain `|`. Dry run still writes nothing. Add-only behavior is unchanged, so a later tenant import does not move an existing `PartitionKey` `10` row into the new partition.

```bash
dotnet run --project tools/VoterDataImporter -- --import --csv path/to/voters.csv --account <account-name> --tenant TENANT001
```

The Function App associates the untouched Ward 10 partition with one customer through its `InitialTenantId` setting. Create that customer and ward in the API as well. The importer does not create customer records and does not write verifications.

## Verify

Verification reads one `Voters` partition from Azure. `--legacy-partition` reads the ward id. `--tenant` reads `{tenant}|{ward}`, and each row's `TenantId` must match. For ward `10` the expected count is 7,125. The check fails when the count differs, a serial or EPIC is duplicated, a required field is missing or invalid, or a key does not match the ward and serial. A CSV with 7,125 rows is not treated as success.

```bash
dotnet run --project tools/VoterDataImporter -- --verify --account <account-name> --legacy-partition
```

The report prints aggregate counts only: expected record count, actual stored record count, missing required fields, duplicate serial count, duplicate EPIC count, invalid PartitionKey count, invalid RowKey count, and the verification result. It does not print voter names, EPIC numbers, house numbers, ages, gender, or relative names. There is no switch that prints stored voter rows.

Ward 10 is the default. Another ward id can be passed with `--ward`, and `--expected` is required for verification of any ward other than `10`.

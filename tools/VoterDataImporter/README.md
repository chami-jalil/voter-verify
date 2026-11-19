# Voter data importer

Console tool that validates a UTF-8 CSV and inserts new rows into the existing `Voters` table. The Android app is unchanged and still only reads voter rows.

The tool does not write `VoterVerifications`, delete rows, or replace an existing voter. Running the same file again inserts nothing and leaves stored rows as they are.

Do not commit real voter rolls, PDFs, phone numbers, credentials, or production exports. The sample file is fictitious.

## CSV format

The header row must contain these columns. Column order does not matter. Save the file as UTF-8. Telugu names are stored as given; the tool does not transliterate them. Put the English spelling in the English columns.

| Column | Rule |
| --- | --- |
| `WardId` | Ward being imported. The default run is ward `10`. This becomes `PartitionKey`. |
| `WardVoterSerialNumber` | Unique inside the ward. This becomes `RowKey`. `/`, `\`, `#`, and `?` are not allowed. |
| `EpicNumber` | Three letters and seven digits. Spaces are removed and letters are uppercased before insert. |
| `AssemblyRollReference` | Three or more numeric parts, such as `175/42/1`. Spaces are removed and `-` is stored as `/`. |
| `NameTelugu` | Required Telugu text. Not translated. |
| `NameEnglish` | Required Latin transliteration, typed separately. |
| `RelativeNameTelugu` | Required Telugu text. |
| `RelativeNameEnglish` | Required Latin transliteration. |
| `Age` | Whole number from 18 to 120. 100 and older is a warning. |
| `Gender` | `Male`, `Female`, or `Third Gender`. `M`, `F`, and `TG` are accepted. Telugu labels `పురుషుడు`, `స్త్రీ`, and `ఇతరులు` are kept. |
| `HouseNumber` | As printed, for example `12-A`. |
| `NormalizedHouseNumber` | Uppercase letters and digits from the house number. `12-A` must be `12A`. A mismatch is an error. |
| `SectionNumber` | Required section. |

Surrounding whitespace is trimmed. Telugu characters are not rewritten.

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
dotnet run --project tools/VoterDataImporter -- --csv path/to/voters.csv
```

Pass the account when you also want existing `PartitionKey` + `RowKey` values checked. This still writes nothing.

```bash
dotnet run --project tools/VoterDataImporter -- --csv path/to/voters.csv --account <account-name>
```

The report lists total rows, valid rows, invalid rows, duplicate serials, duplicate EPIC numbers, missing fields, warnings, and each row problem. It then shows how many new rows would be inserted, how many already match, and which existing rows differ and would be left unchanged.

## Import

`--import` is required before any write. The tool then asks you to type `IMPORT`. Declining writes nothing. Only validated rows that are not already stored are sent, in batches of at most 100 entities, grouped by `PartitionKey`. Inserts use table `Add`. A conflict is reported and the stored row is kept. A failed batch is split so the failing row can be reported on its own.

```bash
dotnet run --project tools/VoterDataImporter -- --import --csv path/to/voters.csv --account <account-name>
```

A second run of the same file reports the rows as already stored and inserts nothing.

## Verify

Verification reads the `Voters` partition from Azure. For ward `10` the expected count is 7,125. The check fails when the count differs, a serial or EPIC is duplicated, a required field is missing or invalid, or a key does not match the ward and serial. A CSV with 7,125 rows is not treated as success.

```bash
dotnet run --project tools/VoterDataImporter -- --verify --account <account-name>
```

The command prints sample rows (serial, EPIC, Telugu name, English name, house, age, gender, assembly reference) for a manual comparison with the roll.

Ward 10 is the default. Another ward id can be passed with `--ward`, and `--expected` is required for verification of any ward other than `10`.

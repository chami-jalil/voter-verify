# Voter Verification

Android app for authorized staff to look up ward voter-list records and record an administrative verification. The phone signs in with Microsoft Entra ID and calls an Azure Functions API. The Function App reads and writes Azure Table Storage with its own managed identity. The APK does not contain a storage key, SAS token, function key, or client secret.

Voter master rows are read-only. Verification is stored in a separate table and updated with the row ETag so a later save cannot silently overwrite someone else's change. Customer, ward, and role checks run in the API. The phone cannot grant itself another customer or ward by sending a different id.

## Solution structure

```
VoterVerification.slnx
VoterVerification.csproj          .NET MAUI Android app
api/VoterVerification.Functions/  .NET 10 isolated Azure Functions API
Configuration/                    Entra IDs, Functions URL, API scope
Authentication/                   MSAL public-client sign-in
Models/                           Voter, verification, ward, and search models
Data/                             Azure Table entities and mapping
Services/                         HTTP client used by the app
Helpers/                          Search, phone, ETag, tenant partitions, and OData rules
ViewModels/                       Screen state and commands
Views/                            Login, wards, dashboard, search, voter detail
Platforms/Android/                Manifest, MSAL redirect, activity
tools/VoterDataImporter/          Administrative CSV import
tests/VoterVerification.Tests/    Rules that do not need a device
```

Screens, in order:

1. Login — Microsoft Entra ID sign-in.
2. Ward selection — only the wards the API assigns to the signed-in account.
3. Dashboard — expected roll size, voters on file, and verified count.
4. Voter search — serial number, EPIC, assembly roll reference, Telugu or English name, or house number.
5. Voter details and verification — roll fields are read-only; phone and verified state are saved separately.

## Azure values you must supply

Set these constants in `Configuration/AppConfiguration.cs` and rebuild. Do not put a storage account key, client secret, function key, or SAS token in the app.

| Constant | What to enter |
| --- | --- |
| `TenantId` | Entra ID directory (tenant) id of the organization that signs the user in |
| `ClientId` | Application (client) id of the public mobile app registration |
| `FunctionsBaseUrl` | HTTPS base URL of the Function App, with no code or key in the query string |
| `ApiScope` | Delegated scope `api://{functions-api-client-id}/access_as_user` |

`StorageAccountName` in this file is not used by the phone. The Function App reads the account name from its own setting. The existing storage account is `voterlistdatastg` in resource group `voter-list-rg` (South India).

Ward 10's existing rows stay on partition key `10`. Expected voters for that initial ward remain `7,125` when the Function App's `InitialTenantId` matches the caller. Other wards come from the API, not from constants in the APK.

The Android redirect scheme is generated from `ClientId` at build time (`msal{client-id}://auth`). You do not edit the manifest by hand.

`VotersTableName` and `VerificationsTableName` default to `Voters` and `VoterVerifications`.

## Entra ID and Azure

The phone is a public client. The Function App is the protected API and uses a system-assigned managed identity for table data. Exact registration steps, supported account types, the API scope, admin consent, bootstrap SuperAdmin object id, and the role assignment are in `api/VoterVerification.Functions/README.md`.

The app requests `ApiScope`, not `https://storage.azure.com/.default`. The signed-in user's display name and username are stored in `VerifiedBy` on a verification row. Passwords and access tokens are not written to application preferences. The MSAL cache is kept in MAUI SecureStorage, which uses the Android Keystore.

Signing in does not grant access. The API accepts the token only after it checks the issuer, audience, signature, and lifetime, then looks up an active application user. A SuperAdmin is the Entra object id configured on the Function App. A role sent by the phone is ignored.

## Tables

An administrator creates the tables. The app will not create them.

```bash
az storage table create --account-name <account> --name Voters --auth-mode login
az storage table create --account-name <account> --name VoterVerifications --auth-mode login
```

### Voters

| Property | Contents |
| --- | --- |
| PartitionKey | Ward id for existing rows. New customer rows use `{TenantId}\|{WardId}` |
| RowKey | Ward voter serial number, kept as a string |
| TenantId | Customer account. Blank on existing ward rows |
| WardId, WardVoterSerialNumber | Ward id and serial. Ward id is not taken from a composite partition key |
| EpicNumber | EPIC |
| AssemblyRollReference | Assembly roll reference |
| NameTelugu, NameEnglish | Voter name |
| RelativeNameTelugu, RelativeNameEnglish | Relative name |
| Age | Integer |
| Gender | As printed on the roll |
| HouseNumber | As printed |
| NormalizedHouseNumber | Uppercase letters and digits, no spaces or hyphens |
| SectionNumber | Section |

### VoterVerifications

| Property | Contents |
| --- | --- |
| PartitionKey | Same partition as the voter row that was read |
| RowKey | Same serial number as the voter row |
| TenantId, WardId | Customer and ward resolved by the API |
| PhoneNumber | 10 digits, or blank when not verified |
| IsVerified | Boolean |
| VerifiedAtUtc | Set when the row is verified |
| VerifiedBy | Signed-in user's name and username |
| UpdatedAtUtc | Last save time |

House search still removes spaces and hyphens and uppercases the typed value. An exact printed house number is included. A partial match is a prefix of `NormalizedHouseNumber`, written as a string range (`ge` / `lt`) inside the ward partition. Azure Table Storage has no `contains` or `startswith`, and `NormalizedHouseNumber` is not a table key, so this range still scans that partition and only avoids downloading rows outside the prefix. A house such as `112` does not match a search for `12`. Import `NormalizedHouseNumber` with the same letter-and-digit normalization.

Assembly roll search is separate from EPIC and from house number. A value of three or more numeric parts, such as `175/42/123`, is matched with an equality filter on `AssemblyRollReference` in the same ward. Spaces are removed and `-` is treated as `/`. The query also checks the hyphen form and a single space around the separators, for example `175-42-123` and `175 / 42 / 123`. Other stored spacing is not matched.

## Ward data import

The mobile app only reads `Voters`. A separate console tool, `tools/VoterDataImporter`, validates a CSV and inserts new rows. It does not write `VoterVerifications`, delete rows, or overwrite an existing voter. Real voter rolls, PDFs, and credentials stay out of this repository. The CSV format, sign-in, dry run, import, and verification steps are in `tools/VoterDataImporter/README.md`.

## Run locally

Install the .NET 10 SDK, the MAUI Android workload, and an Android SDK (API 36) with JDK 17:

```bash
dotnet workload install maui-android
```

Point `JAVA_HOME` at JDK 17 and `ANDROID_HOME` at the Android SDK, then:

```bash
dotnet test
dotnet build -f net10.0-android -c Debug
```

The debug APK is signed with the debug keystore:

`bin/Debug/net10.0-android/com.voterverification.internal-Signed.apk`

The project targets `android-arm64`. Add `android-arm` to `RuntimeIdentifiers` in the project file for 32-bit devices.

Telugu names use the bundled Noto Sans Telugu font (SIL Open Font License, see `Licenses/NotoSansTelugu-OFL.txt`). Open Sans ships with the MAUI template under the same license.

## Behavior notes

- Sign-in is disabled until the Entra directory id, mobile client id, Functions HTTPS URL, and `api://` scope are set.
- If the device has no network, actions stop before calling Azure.
- Azure errors are shown as short messages. Storage account names and phone numbers are not written to logs.
- A failed ETag check reloads the verification and asks the operator to review it before saving again.
- Marking a voter verified requires a 10-digit phone number.

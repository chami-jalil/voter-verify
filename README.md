# Voter Verification

Temporary internal Android app for authorized staff to look up ward voter-list records and record an administrative verification. The app talks to Azure Table Storage directly over HTTPS. It does not include a web API, SQL database, or Firebase.

Voter master rows are read-only. Verification is stored in a separate table and updated with the row ETag so a later save cannot silently overwrite someone else's change.

## Solution structure

```
VoterVerification.sln
VoterVerification.csproj          .NET MAUI Android app
Configuration/                    Entra IDs, storage account, ward settings
Authentication/                   MSAL sign-in and Table Storage token credential
Models/                           Voter, verification, ward, and search models
Data/                             Azure Table entities and mapping
Services/                         IVoterDataService and the table implementation
Helpers/                          Search, phone, ETag, and OData rules
ViewModels/                       Screen state and commands
Views/                            Login, wards, dashboard, search, voter detail
Platforms/Android/                Manifest, MSAL redirect, activity
tests/VoterVerification.Tests/    Rules that do not need a device
```

Screens, in order:

1. Login — Microsoft Entra ID sign-in.
2. Ward selection — Ward 10, plus a second ward that stays unavailable until it is configured.
3. Dashboard — expected roll size, voters on file, and verified count.
4. Voter search — serial number, EPIC, Telugu or English name, or house number.
5. Voter details and verification — roll fields are read-only; phone and verified state are saved separately.

## Azure values you must supply

Set these constants in `Configuration/AppConfiguration.cs` and rebuild. Do not put a storage account key, client secret, or SAS token in the app.

| Constant | What to enter |
| --- | --- |
| `TenantId` | Entra ID directory (tenant) id |
| `ClientId` | Application (client) id of the mobile app registration |
| `StorageAccountName` | Storage account name only, for `https://{name}.table.core.windows.net` |
| `Ward2Id` | Partition key for the second ward |
| `Ward2DisplayName` | Name shown for the second ward |
| `Ward2ExpectedVoters` | Expected voter count for the second ward |

Ward 10 is fixed: partition key `10`, display name `Ward 10`, expected voters `7,125`. Leave the Ward 2 constants blank until the real name, id, and count are known. The app will not invent them.

The Android redirect scheme is generated from `ClientId` at build time (`msal{client-id}://auth`). You do not edit the manifest by hand.

`VotersTableName` and `VerificationsTableName` default to `Voters` and `VoterVerifications`.

## Entra ID app registration

Create a public client registration. Do not create a client secret.

- Platform: Mobile and desktop applications.
- Redirect URI: `msal{client-id}://auth`.
- Allow public client flows.
- Delegated API permission: **Azure Storage** → `user_impersonation`.
- Grant admin consent for that permission.

The app requests scope `https://storage.azure.com/.default`. The signed-in user's display name and username are stored in `VerifiedBy`. Passwords and access tokens are not written to application preferences. The MSAL cache is kept in MAUI SecureStorage, which uses the Android Keystore.

## Data permissions

Assign a **data** role on the storage account, or on the two tables. Do not assign Owner, Contributor, Storage Account Contributor, or any subscription-wide management role. Those roles do not grant table data access, and this app does not need them.

The app reads `Voters` and inserts or replaces rows in `VoterVerifications`. It does not delete rows and it does not create tables.

Built-in role that works, and still includes delete:

- **Storage Table Data Contributor** (`0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3`), scoped to this storage account.

Tighter custom role, without delete:

- `Microsoft.Storage/storageAccounts/tableServices/tables/entities/read`
- `Microsoft.Storage/storageAccounts/tableServices/tables/entities/write`

`entities/write` covers insert and conditional replace. Scope read to the `Voters` table and read/write to the `VoterVerifications` table when a per-table assignment is practical.

Role changes can take several minutes to apply.

## Tables

An administrator creates the tables. The app will not create them.

```bash
az storage table create --account-name <account> --name Voters --auth-mode login
az storage table create --account-name <account> --name VoterVerifications --auth-mode login
```

### Voters

| Property | Contents |
| --- | --- |
| PartitionKey | Ward id |
| RowKey | Ward voter serial number, kept as a string |
| WardId, WardVoterSerialNumber | Same values as the keys |
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
| PartitionKey | Ward id |
| RowKey | Same serial number as the voter row |
| PhoneNumber | 10 digits, or blank when not verified |
| IsVerified | Boolean |
| VerifiedAtUtc | Set when the row is verified |
| VerifiedBy | Signed-in user's name and username |
| UpdatedAtUtc | Last save time |

House search compares the typed value with `NormalizedHouseNumber` after removing spaces and hyphens and uppercasing it. Import the voter list with that same normalization.

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

- Sign-in is disabled until tenant id, client id, and storage account name are set.
- If the device has no network, actions stop before calling Azure.
- Azure errors are shown as short messages. Storage account names and phone numbers are not written to logs.
- A failed ETag check reloads the verification and asks the operator to review it before saving again.
- Marking a voter verified requires a 10-digit phone number.

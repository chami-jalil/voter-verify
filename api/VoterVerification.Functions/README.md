# Voter Verification API

.NET 10 isolated Azure Functions app. The Android app sends a Microsoft Entra access token. The Function App checks that token, resolves the application user, and reads or writes Azure Table Storage with its system-assigned managed identity.

No storage account key, SAS token, function key, or client secret belongs in this project or in the APK. HTTP functions use `AuthorizationLevel.Anonymous` because the bearer token is the credential. Do not add a function key to the mobile app.

Resource names already known:

| Resource | Value |
| --- | --- |
| Resource group | `voter-list-rg` |
| Storage account | `voterlistdatastg` |
| Region | South India |
| Existing tables | `Voters`, `VoterVerifications` |

This document does not invent a subscription id, Function App name, or Entra application id. Replace the placeholders with values from your own Azure and Entra portals.

## Function App settings

Copy `local.settings.example.json` to `local.settings.json` for local runs. `local.settings.json` is gitignored. `AzureWebJobsStorage` is only the Functions host storage. Voter data uses `StorageAccountName` and `DefaultAzureCredential`.

| Setting | Value |
| --- | --- |
| `StorageAccountName` | `voterlistdatastg` |
| `InitialTenantId` | Customer id that may read the existing Ward 10 partition `10`. Example: `TENANT001`. Leave blank until that customer is created. |
| `SuperAdminObjectId` | Object id of the Entra user who is the platform SuperAdmin. Required. |
| `SuperAdminDirectoryId` | Directory (tenant) id of that same user. Required. An object id match from another directory is not SuperAdmin. |
| `EntraApiAudience` | Application ID URI of the API registration, such as `api://{api-client-id}` |
| `EntraApiClientId` | Application (client) id of the API registration. Tokens whose audience is either value are accepted. |

The host refuses to start when either SuperAdmin id is missing. SuperAdmin is that object id in that directory only. A role, customer id, or email sent by the phone cannot create a SuperAdmin. Do not put an email address in these settings.

## Managed identity

Create the Function App in `voter-list-rg` (South India is the storage region). Turn on a system-assigned managed identity, then grant it table data access on this storage account only:

```bash
az functionapp identity assign --resource-group voter-list-rg --name <function-app-name>
az role assignment create \
  --assignee <principal-id> \
  --role "Storage Table Data Contributor" \
  --scope /subscriptions/<subscription-id>/resourceGroups/voter-list-rg/providers/Microsoft.Storage/storageAccounts/voterlistdatastg
```

`<principal-id>` is the `principalId` printed by the identity command. Do not assign Owner, Contributor, or Storage Account Contributor for this data path. Role assignment can take several minutes.

The importer still uses the operator's Azure CLI user. That user needs entity read and add on `Voters`. It does not need a key.

## Tables

`Voters` and `VoterVerifications` already exist. Do not delete or rewrite them. Create only the account tables:

```bash
az storage table create --account-name voterlistdatastg --name Customers --auth-mode login
az storage table create --account-name voterlistdatastg --name CustomerWards --auth-mode login
az storage table create --account-name voterlistdatastg --name AppUsers --auth-mode login
az storage table create --account-name voterlistdatastg --name UserWardAssignments --auth-mode login
```

### Existing voter rows

Rows already imported for Ward 10 keep `PartitionKey` `10` and `RowKey` equal to the serial number. Their `TenantId` is blank. The API reads that partition only when the caller's customer id equals `InitialTenantId`. A row in that partition is visible only when its `TenantId` is blank or equal to that customer. Another customer never queries partition `10`.

New imports pass `--tenant` to the importer. Those rows use `PartitionKey` `{TenantId}|{WardId}` plus `TenantId` and `WardId` properties. `|` is a legal table-key character and is reserved as this separator. Customer ids cannot contain `|`.

Verification rows are written into the same partition as the voter that was read. An existing Ward 10 verification stays on partition `10`. The API sets `WardId` and `TenantId` on the verification. It does not update the voter row. History is still the single current verification row, replaced with its ETag.

Create a `Customers` row and a `CustomerWards` row for Ward 10 even though the voter keys stay `10`. Set `InitialTenantId` to that customer id or the existing roll stays hidden.

### Account tables

| Table | PartitionKey | RowKey | Properties |
| --- | --- | --- | --- |
| `Customers` | `customer` | customer `TenantId` | `CustomerName`, `Status` (`Active` or `Inactive`), `CreatedAtUtc`, `SubscriptionNote` |
| `CustomerWards` | customer `TenantId` | ward id | `DisplayName`, `ExpectedVoters`, `Status` |
| `AppUsers` | Entra directory id | Entra object id | `TenantId`, `Role`, `Status`, `CreatedAtUtc` |
| `UserWardAssignments` | `{directoryId}\|{objectId}` | `{tenantId}\|{wardId}` | `TenantId`, `WardId`, `Status` |

`TenantId` here is the customer account (`TENANT001`), not the Entra directory id. `SubscriptionNote` is stored and unused. There is no billing.

Roles are `SuperAdmin`, `WardAdmin`, and `WardOperator`. A WardAdmin can manage users of their own customer and cannot assign `SuperAdmin`. A WardOperator can search and save verification for an assigned ward only.

## Entra app registrations

Create two registrations. Neither one gets a client secret.

### 1. Mobile public client

- Name: choose your own. The client id is copied into `Configuration/AppConfiguration.cs` as `ClientId`.
- Supported account types: **Accounts in any organizational directory** (any Microsoft Entra ID tenant — multitenant). Personal Microsoft accounts are not required.
- Platform: Mobile and desktop applications.
- Redirect URI: `msal{client-id}://auth`. The Android manifest fills this from `ClientId`.
- Allow public client flows.
- API permission: delegated permission `access_as_user` from the API registration below.
- Grant admin consent in your directory. A customer directory that signs users in must also consent before those users can request a token. Consent does not by itself grant access to voter data.

The Android app is a public client and uses MSAL with the platform broker cache in MAUI SecureStorage. It renews tokens silently and sends `Authorization: Bearer`.

### 2. Functions API

- Name: choose your own.
- Supported account types: the same organizational multitenant setting, so a customer directory can issue a token for this API.
- Application ID URI: `api://{api-client-id}` (or another HTTPS application ID URI you control). Put that value in `EntraApiAudience`.
- Expose a delegated scope named `access_as_user`. Who can consent: admins.
- Do not create an app role that the phone uses for authorization. Application roles in this API are stored in `AppUsers`.
- No client secret and no certificate. The Function App validates the access token itself. It does not call Microsoft Graph and does not use on-behalf-of.

Put the API's client id in `EntraApiClientId` and the mobile scope `api://{api-client-id}/access_as_user` in `AppConfiguration.ApiScope`. Put the Function App's HTTPS origin in `AppConfiguration.FunctionsBaseUrl`.

### What the API checks

For every request:

1. Require a bearer token. Missing, unreadable, expired, badly signed, wrong audience, non-Entra issuer, missing delegated scope `access_as_user`, or an application-only token (`idtyp` `app`, or a `roles` claim without `scp`) returns HTTP 401.
2. Accept issuers `https://login.microsoftonline.com/{directory-id}/v2.0` and `https://sts.windows.net/{directory-id}`.
3. Read `oid` and `tid`. Those identify the user. The API does not trust `TenantId`, `WardId`, or `Role` from the phone.
4. Load `AppUsers` by directory id and object id. An inactive user returns HTTP 403. A user who is not in the table returns HTTP 403 unless both `SuperAdminObjectId` and `SuperAdminDirectoryId` match the token. A signing key the API does not know causes one metadata refresh and a second validation. A token that still fails that validation is rejected.
5. For a ward call, the customer must be active and must own that ward. A WardAdmin or WardOperator must also have an active assignment for that ward. A different `X-Customer-Tenant` header returns HTTP 403.
6. A SuperAdmin must send `X-Customer-Tenant` before a ward operation. The API then checks that customer and ward. It does not use a ward id from the phone as proof.

External users can authenticate only after an administrator has created their `AppUsers` row and ward assignment. Multitenant sign-in does not onboard them.

## HTTP API

All routes are under `/api`. Send `Authorization: Bearer {access-token}`. Send `X-Customer-Tenant` with the customer id when a SuperAdmin calls a ward route. The phone stores that id from the ward list returned by the API.

| Method | Route | Who |
| --- | --- | --- |
| GET | `/api/me` | Signed-in user profile |
| GET | `/api/wards` | Wards visible to the caller |
| GET | `/api/wards/{wardId}/dashboard` | Counts for one allowed ward |
| GET | `/api/wards/{wardId}/voters?q=` | Search |
| GET | `/api/wards/{wardId}/voters/{serial}` | One voter |
| GET | `/api/wards/{wardId}/voters/{serial}/verification` | Current verification, as a one-item history list |
| PUT | `/api/wards/{wardId}/voters/{serial}/verification` | Save verification |
| POST | `/api/admin/customers` | SuperAdmin creates a customer and optional ward |
| POST | `/api/admin/users` | SuperAdmin assigns a user |
| POST | `/api/tenant/users` | WardAdmin assigns a user in their own customer |

Customer body: `tenantId`, `customerName`, `active`, optional `wardId`, `wardName`, `expectedVoters`.

User body: `directoryId`, `objectId`, `tenantId`, `role`, `active`, optional `wardId`. A WardAdmin's `tenantId` is replaced with their own customer. `role` cannot be `SuperAdmin` unless the caller is SuperAdmin.

Search accepts the same queries as the Android app: serial number, EPIC, assembly roll reference, Telugu or English name, relative name, and house number. Logs record the operation, object id, customer id, ward id, and success or failure. They do not record voter names, EPIC numbers, or phone numbers. The phone receives a short error, not an exception message.

## Local run

```bash
dotnet run --project api/VoterVerification.Functions
```

Sign in with Azure CLI to a principal that has Storage Table Data Contributor on `voterlistdatastg` before calling a data route. The Functions host still needs `AzureWebJobsStorage`. The sample file points that at the local storage emulator, which is separate from the voter tables.

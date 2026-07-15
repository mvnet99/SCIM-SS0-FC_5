# Configuration

Everything this API needs to run, where it comes from, and what you see when it's wrong.

**If startup fails or the first request 500s, read this page first.** It's almost always a missing secret.

---

## How configuration loads

```
1. Container starts
2. Read the KeyVaultUrl environment variable   <- injected by SRE
3. If it has a value: read ~10 secrets from that Key Vault using DefaultAzureCredential
4. Push them into IConfiguration via AddInMemoryCollection
5. appsettings.json fills in logging levels
```

Every lookup uses a constant from `FlexCount.Scim.Api/ScimAppConstants.cs`. There are no magic strings in the code — if you're hunting for where a setting is used, search that file first.

**Two different names for the same thing.** A secret has a *Key Vault name* (what SRE creates) and a *config key* (how the code reads it). They're deliberately different. Both are in the tables below.

---

## The environment variable

| Name | Set by | Example |
|---|---|---|
| `KeyVaultUrl` | SRE, as a Container App environment variable. Locally: `launchSettings.json` | `https://kv-fc-dev4-ue.vault.azure.net/` |

**This is the only environment variable.** Everything else comes from the vault.

> **If it's missing:** the app starts normally and logs `Start Up Key Vault URL:` with nothing after it. Then the **first request** fails with `InvalidOperationException: CustomerApi:BaseUrl is not configured`. It looks like a code bug. It isn't. Check this variable.

`ASPNETCORE_ENVIRONMENT` also matters — it decides which `appsettings.*.json` loads, which decides whether raw payloads get logged. See the bottom of this page.

---

## Key Vault secrets — inbound (Target Entra → us)

| Key Vault name | Config key | What it's for |
|---|---|---|
| `ScimToken` | `Scim:Token` | The static bearer token. Must match exactly what Target has in their Entra provisioning settings. |
| `ScimServiceEmail` | `Scim:ServiceEmail` | The service account email sent on every outbound call as `X-Service-Account`. |

### `ScimToken`

This is a shared password between Target's Entra and us. Long random string, no expiry, no rotation schedule.

- **Wrong value** → every SCIM call returns 401. Entra's Test Connection shows `CredentialValidationFailure`. Logs show `SCIM_AUDIT event=AuthFailed reason=InvalidToken`.
- **Missing** → startup throws `secretKey 'ScimToken' not found in Key Vault`. Container won't start.
- **To rotate:** change it in Key Vault *and* in Target's Entra provisioning settings, in that order, and expect a brief window of 401s in between. There is no overlap mechanism.

### `ScimServiceEmail` — read this one carefully

This must be **`svc-flexcount-provisioning@target.com`**, and that user must exist in the `Corporate_User` table. `PS_SSO_Config.sql` seeds it.

Here's why it matters so much. This email is sent as `X-Service-Account` on every outbound call. The Customer API's `TokenHelper` looks it up in the database and uses **that user's `IdCustomer`** for the whole request. So this one string decides which customer every SCIM-provisioned user gets written to.

- **Set to the wrong email** (say, a developer's own account) → users get provisioned into **the wrong customer**. No error. No warning. It just quietly writes Target's employees into somebody else's tenant.
- **Set to an email that doesn't exist in `Corporate_User`** → every call fails with `UserNotFoundException` from the Customer API.
- **Set to a user with no `User_Selected_Customer_Link` row** → `IdCustomer` resolves to `0` and every write goes nowhere. This is why `PS_SSO_Config.sql` section 4c exists.

Check this value first whenever provisioning "works" but nothing appears in the portal.

---

## Key Vault secrets — outbound (us → Customer API)

| Key Vault name | Config key | What it's for | New or existing? |
|---|---|---|---|
| `CustomerApiBaseUrl` | `CustomerApi:BaseUrl` | The APIM URL of the Customer API | **New** for SCIM |
| `AzureAdB2CSCIMClientId` | `CustomerApi:ClientId` | Our app registration's client ID | **New** for SCIM |
| `AzureAdB2CSCIMSecret` | `CustomerApi:ClientSecret` | Our app registration's secret | **New** for SCIM |
| `AzureAdB2CTenantId` | `CustomerApi:TenantId` | Tenant we ask for a token from | Existing (shared with Customer API) |
| `AzureAdB2CDomain` | *(used to build the scope)* | First half of the OAuth scope | Existing |
| `AzureAdB2CClientId` | *(used to build the scope)* | The **Customer API's** client ID — second half of the scope | Existing |
| `AzureAdB2CInstance` | *(read, then unused)* | — | Existing |
| `JwtSecret` | `CustomerApi:JwtSecret` | Only used by dead code | **New**, and removable |

### The scope is built, not stored

There is no `CustomerApiScope` secret. It's assembled at startup:

```csharp
var customerApiScope = $"https://{b2cDomain}/{b2cClientId}/.default";
```

So `AzureAdB2CDomain` and `AzureAdB2CClientId` together *are* the scope. If either is wrong, the token request to Microsoft fails and every outbound call dies.

> The comment in `ScimAppConstants.cs` says the scope is built as `https://{domain}.onmicrosoft.com/{clientId}/.default`. **The code doesn't add `.onmicrosoft.com`.** So `AzureAdB2CDomain` must already contain the full domain. If someone "fixes" the comment by changing the secret to just `flexcountdev`, outbound auth breaks. Gap logged.

### Watch the two client IDs

This catches people out. There are **two different client IDs** in play:

- `AzureAdB2CSCIMClientId` — **us**. Who's asking for the token.
- `AzureAdB2CClientId` — **the Customer API**. What we're asking for a token *to*.

Swap them and you'll get a token that the Customer API rejects, with an error that doesn't mention either.

### `JwtSecret` is dead

It's loaded and pushed into config, and `CustomerApiService` throws at construction if it's absent — but the only thing that reads it is `SetAuthHeader()`, a private method **nothing calls**. It's leftover from an earlier design where we signed our own tokens.

Don't delete the secret without also removing the `?? throw` in the constructor, or the app stops starting. See [06-known-gaps.md](./06-known-gaps.md).

---

## Full checklist for a new environment

Ask SRE for all of these before you deploy:

**Container App environment variable**
- [ ] `KeyVaultUrl`

**Key Vault secrets — must be created**
- [ ] `ScimToken`
- [ ] `ScimServiceEmail` = `svc-flexcount-provisioning@target.com`
- [ ] `CustomerApiBaseUrl`
- [ ] `AzureAdB2CSCIMClientId`
- [ ] `AzureAdB2CSCIMSecret`
- [ ] `JwtSecret` (any value — it's unused, but the constructor requires it)

**Key Vault secrets — should already exist from the Customer API**
- [ ] `AzureAdB2CTenantId`
- [ ] `AzureAdB2CDomain`
- [ ] `AzureAdB2CClientId`
- [ ] `AzureAdB2CInstance`

**Also needed, outside Key Vault**
- [ ] The Container App's managed identity has **Get** on Key Vault secrets
- [ ] `PS_SSO_Config.sql` has been run against that environment's database
- [ ] The app registration in `AzureAdB2CTenantId` has permission on the Customer API's scope
- [ ] Target has the matching `ScimToken` in their Entra provisioning settings

> **Environment gotcha:** `PS_SSO_Config.sql` decides Dev vs Prod by checking whether `@@SERVERNAME` contains `prod`. There is no QA branch — a QA server gets **Dev** group names seeded. If Entra's QA app sends `-QA` group names, they won't be found. See [06-known-gaps.md](./06-known-gaps.md).

---

## appsettings

`appsettings.json` and `appsettings.Development.json` hold logging config only. Everything with a secret in it is blank in both files — that's on purpose, so nothing sensitive is ever committed.

### `LoggingOptions:EmitRawPayloads`

| Environment | Value | Effect |
|---|---|---|
| `appsettings.json` (all non-Dev) | `false` | Off |
| `appsettings.Development.json` | `true` | Logs full request bodies |

When it's `true` you get `RAW SCIM POST /Users payload={...}` — the entire unredacted body: names, emails, phone numbers.

**It must stay `false` outside local development.** It's controlled by `ASPNETCORE_ENVIRONMENT`. If someone sets that to `Development` in a deployed environment, personal data starts flowing into Datadog.

### Serilog levels

| | Default | `FlexCount.Scim` | `Microsoft` | `Microsoft.AspNetCore` | `System` |
|---|---|---|---|---|---|
| Production | Warning | Warning | Error | Warning | Error |
| Development | Information | Information | Warning | Warning | Warning |

Production runs at **Warning**. That's why the audit lines are written at `LogWarning` rather than `LogInformation` — it's deliberate, so they survive. Don't "tidy" them down to Information; you'll lose your audit trail.

---

## Local development

`launchSettings.json` sets exactly two things:

```json
"ASPNETCORE_ENVIRONMENT": "Development",
"KeyVaultUrl": "https://kv-fc-dev4-ue.vault.azure.net/"
```

Everything else comes from the Dev vault. You need:

1. `az login` with an account that has **Get** on that vault's secrets (`DefaultAzureCredential` picks up your CLI login)
2. Network access to the vault

It listens on `https://localhost:65529`, Swagger at `/swagger`.

> **There are commented-out blocks in `launchSettings.json` containing a real-looking client secret.** They're historical. Don't uncomment them, don't copy them, and treat that secret as compromised — it's in git history. Logged in [06-known-gaps.md](./06-known-gaps.md).

### Pointing at a local Customer API

There's a commented line in `Program.cs`:

```csharp
// string customerHardcodedBaseUrl = "http://localhost:13211/customerapprestapi";
```

That's how someone was debugging against a locally-running Customer API. If you need to do the same, override `CustomerApi:BaseUrl` — but push it through configuration or an environment variable rather than editing `Program.cs`, so it can't be committed by accident.

---

## Quick troubleshooting

| What you see | Where to look |
|---|---|
| Container won't start, `secretKey 'X' not found in Key Vault` | That secret is missing, or the managed identity can't read the vault |
| First request: `CustomerApi:BaseUrl is not configured` | `KeyVaultUrl` env var is empty |
| Every SCIM call 401s | `ScimToken` doesn't match what Target has |
| Every outbound call 401s | `AzureAdB2CSCIMClientId` / `Secret` wrong, or the scope is malformed |
| Outbound fails with a token error | Check the scope: `AzureAdB2CDomain` + `AzureAdB2CClientId` |
| Provisioning returns 200 but nothing appears in the portal | `ScimServiceEmail` resolves to the wrong customer, or the service account has no `User_Selected_Customer_Link` row |
| Regional users get created with no regions | The group name isn't in `Customer_Groups` for that customer. Run `PS_SSO_Config.sql`. |
| `UserNotFoundException` on every call | `ScimServiceEmail` isn't in `Corporate_User` |
| Personal data showing up in Datadog | `ASPNETCORE_ENVIRONMENT` is `Development` somewhere it shouldn't be |

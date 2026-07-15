# Known Gaps

**Internal only. Do not send this to Target.**

Things the code gets wrong, or does in a way that will surprise you. Everything here was found by reading the repos, not by running them — so a few may already be fixed, and a couple may be intentional decisions nobody wrote down. Check before you act.

**Why this file exists:** so the next person doesn't spend a day rediscovering something we already knew. If you fix one, delete the entry and update whichever doc describes the behaviour.

**Some entries are in other repos.** They're here because they change how *this* API behaves. The repo is named in each one.

| | Meaning |
|---|---|
| **Critical** | Security or data loss. Fix before the next release. |
| **High** | Will cause a real incident or silently wrong data. |
| **Medium** | Wrong, but survivable. Fix when you're nearby. |
| **Low** | Tidy-up. Do it when you touch the file. |

---

## Critical

### G-C1 — The `X-Service-Account` headers are an unauthenticated way in
**Repo:** `WIS_CustomerApp_RestAPI`

`AuthorizationFilterAttribute.OnAuthorization` returns early if two headers are present:

```csharp
bool isScimS2S = !string.IsNullOrEmpty(serviceAccountEmail) && !string.IsNullOrEmpty(customerIdHeader);
if (isScimS2S) { return; }
```

`TokenHelper.GetTokenUserDetails` does the same check, **before** it looks at `accessToken.User.Identity.IsAuthenticated`. And `UsersController` has no `[Authorize]` attribute, and `AddAuthorization` sets a `DefaultPolicy` but no `FallbackPolicy` — so ASP.NET doesn't enforce anything on that endpoint either.

Net effect: this works, with **no `Authorization` header at all**, against a publicly-routed host:

```http
POST https://<host>/customerapprestapi/api/users/AddUser
X-Service-Account: <any email in dbo.Corporate_User>
X-Customer-Id: x
```

The headers *are* the credential. They're unsigned and guessable, and the default one — `svc-flexcount-provisioning@target.com` — is in this repo, in `PS_SSO_Config.sql`, and in `UsersController.ExtractClaims()`. It affects all 19 controllers using that filter, not just SCIM.

**Fix:** add `[Authorize]` (or a `FallbackPolicy`) so a validated token is required *before* the filter runs, then make the S2S branch check the authenticated principal is actually the SCIM app registration — `appid` / `oid` against an allowlist. Headers should carry context, never identity.

**Note:** this is not about the service account. A privileged S2S identity is fine and necessary. It's about how the caller proves it's us.

---

### G-C2 — A live bearer token is committed to the repo
**Repo:** `WIS_SCIMApp_RestAPI`

`FlexCount.Scim.Tests/Postman/FlexCount_SCIM_Env_Dev.json` (and QA, and Stage):

```json
{ "key": "scim_token",
  "value": "ovecRd0o3G2rN4BfzvJyGkY5d/rHSxF6ju13UJkPJVZUt1u1M3hJmHlbiP3bK526",
  "description": "Bearer token — same across all environments" }
```

Cleartext, in git, and described as identical across every environment. One token for Dev, QA, Stage and Prod means a Dev leak is a Prod compromise.

**Fix:** rotate it, purge it from history, give each environment its own, and never store the value in a committed file. The collection in [`postman/`](./postman/) already uses a blank `{{scim_token}}`.

---

## High

### G-01 — An unknown group silently keeps the old regions
**Repo:** `WIS_SCIMApp_RestAPI` + `flexcount-database-services`

Move a user to a group that isn't in `Customer_Groups`:

1. `ResolveRoles` accepts it — the regex is `^Group(\d+)-`, it doesn't care if the group is real
2. `SearchCustomerGroups` finds nothing → Customer API returns 404
3. `SearchCustomerGroupAsync` turns 404 into an empty list
4. Regions come out empty
5. The guard in `UpdateUserCommandHandler` sees Regional + no regions → **skips**
6. **200 OK.** The user keeps their old access. Entra logs a success.

The only trace is `no matching groups found` at Information level.

**Why it will happen:** Target creates a group before we've seeded it. Or `PS_SSO_Config.sql` hasn't run in that environment. Or someone typos a group name.

**Fix:** in `UsersController`, return 400 when the resolved type is Regional and `SearchCustomerGroupAsync` returns zero groups. A requested group that doesn't exist is an error, not a no-op.

**Test:** TC-N3 in [04-testing.md](./04-testing.md).

---

### G-02 — The seed script has no QA branch
**Repo:** `WIS_Database`

```sql
DECLARE @IsProdEnv BIT = CASE WHEN LOWER(@@SERVERNAME) LIKE '%prod%' THEN 1 ELSE 0 END;
```

Prod or Dev. That's it. But the CI/CD pipelines have Dev, QA, Stage, Intg, PerfStage, PerfDev and Prod, and the old test guides say the suffix is "Dev, QA, or Prod".

A QA server gets **Dev** group names seeded. Entra's QA app sends `-QA` names. Nothing matches → G-01 fires → every Regional provisioning call silently no-ops with a 200.

**Fix:** proper environment detection, and seed the environments you actually run. Pairs with G-01 — fix that one and this becomes a loud 400 instead of silence.

---

### G-03 — `GET /Users` returns an empty role
**Repo:** `WIS_SCIMApp_RestAPI`

`UserDetailResponse` (Customer API) has no `UserType` property. `FlexCountUser.UserType` defaults to `""` and never gets filled. So `ToScimUser` emits:

```json
"roles": [{ "value": "", "primary": "true" }]
```

Entra compares that to what it wants, sees a mismatch, and **sends a redundant roles PATCH on every 40-minute cycle, forever.** Users stay correct — it's wasted traffic, not corruption.

The older test guide claims this returns `"Admin"`. It doesn't, and never has against this code.

**Fix:** `ToScimUser` already receives `UserRoleCustomerLink.IdRole`. Use `GetUserTypeFromIdRole()` — the PATCH path already does exactly that. Better still, map back to the entitlement string so Entra stops re-patching.

---

### G-04 / G-05 — `meta.created`, `meta.lastModified` and `meta.version` are always wrong
**Repo:** `WIS_SCIMApp_RestAPI`

| Our model | Customer API returns | Result |
|---|---|---|
| `CreatedAt` | `CreatedDate` | null → `DateTime.UtcNow` |
| `UpdatedAt` | `UpdatedDate` | null → `DateTime.UtcNow` |

So every GET says the user was created and modified *this instant*, and `meta.version` is `W/""` — while `ServiceProviderConfig` advertises `etag: { supported: true }`.

Three RFC 7643 §3.1 violations. Target's conformance review will find them quickly.

**Fix:** add `[JsonPropertyName("createdDate")]` / `("updatedDate")` to `FlexCountUser`, or rename the properties.

---

### G-06 — The deactivate response is nearly empty
**Repo:** `WIS_SCIMApp_RestAPI`

The deactivate branch in `PatchUser` builds a throwaway object:

```csharp
var deactivated = new FlexCountUser {
    Email = email, FirstName = string.Empty, LastName = string.Empty,
    UserType = string.Empty, Status = "InActive", UpdatedAt = DateTime.UtcNow
};
```

The response has empty name and role, and `meta.created` jumps to now (because `CreatedAt` is null). RFC 7644 §3.5.2 says a 200 must return the full resource.

The **database keeps everything** — this is a response-shape problem only. Entra reads `active` and nothing else, so it works today.

**Fix:** GET the user, deactivate, then return the real resource with `active: false`.

---

### G-07 — Phone buckets disagree between create and patch
**Repo:** `WIS_SCIMApp_RestAPI`

- **Create** (`MapPhones`): `FirstOrDefault(p => p.Primary)` → the `primary` flag decides
- **Patch** (`ProcessPhoneOperation`): `type eq "work"` → primary, `type eq "mobile"` → secondary

So a user created with a mobile marked `primary: true` has that mobile as their primary phone — until the first patch, which replaces primary phone with the *work* number.

Worse: the **type never updates**. Entra sends `phoneNumbers[type eq "work"].value` — the path ends `.value`, not `.type`:

```csharp
if (pathLower.EndsWith(".type")) update.PrimaryPhoneType = MapPhoneType(finalVal);
else                             update.PrimaryPhone = finalVal;   // type left null
```

The bucket is identified from the filter but the type is never *set* from it. Result: primary phone holds the work number while primary phone type still says `Mobile`. The next GET reports them inverted, so Entra patches again. Loop.

**Fix:** derive the type from the filter path in the same branch that picks the bucket. One line.

---

### G-08 — PII goes to logs unmasked
**Repos:** `WIS_SCIMApp_RestAPI`, `WIS_CustomerApp_RestAPI`, `flexcount-database-services`

`LogSanitizer` exists for exactly this and stops at the network boundary.

`CustomerApiService` — five places interpolate raw emails:
```csharp
_logger.LogInformation($"CustomerAPI GET user email={email} customer={customerId}");
```

`EnsureSuccessAsync` logs the full downstream error body, unsanitized and untruncated.

`UsersService` (Customer API) is worse — the whole payload, both write paths:
```csharp
_logger.LogInformation("Adding user with details {details} for customer {CustomerId}",
    System.Text.Json.JsonSerializer.Serialize(userDetailsRequest), tokenUserDetails.IdCustomer);
```

Name, email, both phone numbers, cleartext, to Datadog, on every create and update. `SearchCustomerGroupsQueryHandler` does the same with the query object.

**Fix:** `LogSanitizer.MaskEmail()` on the SCIM side; stop serialising whole request objects on the others.

---

### G-09 — `ValidateAudience = false` on the SCIM auth scheme
**Repo:** `WIS_CustomerApp_RestAPI`

```csharp
authBuilder.AddJwtBearer(ScimS2SScheme, options => {
    options.TokenValidationParameters = new TokenValidationParameters {
        ValidateIssuer = true,
        ValidIssuer = $"https://login.microsoftonline.com/{scimTenantId}/v2.0",
        ValidateAudience = false,   // <--
        ...
```

Any token from that tenant, for any resource, is accepted. Every app registration in the tenant is implicitly trusted.

**Fix:** set `ValidAudience` to the Customer API's client ID.

---

### G-10 — One bad B2C policy locks out every user
**Repo:** `WIS_CustomerApp_RestAPI`

`IssuerSigningKeyResolver` on `B2CHumanScheme` fetches three metadata endpoints — standard, Target, RIT sandbox — inside **one** try/catch:

```csharp
catch (Exception ex) {
    Log.Error("[B2CHuman] Key resolution failed: {Error}", ex.Message);
    return Enumerable.Empty<SecurityKey>();
}
```

If any one fails, all three are lost and **every** B2C token fails validation. Not just Target's — internal WIS employees and local B2C customers too. A test-only policy is a single point of failure for production login.

Made worse by these:

```csharp
try  { b2cInternalSandboxPolicy = KeyVaultSecretManager.GetSecretValueBySecretKey(...); }
catch (Exception ex) { b2cInternalSandboxPolicy = "B2C_1A_RIT-SIGNUPORSIGNIN_HRD"; }
```

A missing secret is swallowed and replaced with a policy name that may not exist in that tenant → metadata 404 → resolver dies → total auth outage, reported as "key resolution failed" rather than "secret missing". `ex` isn't even logged.

**Fix:** one try/catch per `GetConfigurationAsync`, concat what succeeded. And let the Key Vault read throw at startup — fail fast, fail loud, fail before traffic.

---

### G-11 — The seed script's group lookup isn't scoped to a customer
**Repo:** `WIS_Database`

```sql
SELECT @IdRG195 = Id_Customer_Group FROM dbo.Customer_Groups WHERE Customer_Group_Name = @RegionalGroup195;
```

No `Id_Customer` filter — while the `INSERT` above it has one, and the table's unique constraint is `UNIQUE (Id_Customer, Customer_Group_Name)`, i.e. the same name across two customers is explicitly allowed. `SELECT @var = col` over multiple rows silently takes the last one with no error.

Onboard a second SCIM customer reusing any of these names and this script writes Target's region hierarchy onto their group.

**Fix:** `AND Id_Customer = @IdCustomer` on all three SELECTs.

---

### G-12 — `Is_Active` on groups and regions is a dead switch
**Repo:** `flexcount-database-services`

`Customer_Groups.Is_Active` and `Customer_Group_Regions.Is_Active` both exist and are both seeded to `1`. Neither `SearchCustomerGroups` nor `GetCustomerGroups` filters on them:

```sql
WHERE cg.id_Customer = @CustomerId AND cg.Customer_Group_Name IN @CustomerGroupNames
```

Setting `Is_Active = 0` to retire a group or drop a market **does nothing**. If anyone believes that column is the off switch, they're wrong, and they'll find out during an audit.

**Fix:** two `AND ... Is_Active = 1` clauses.

---

## Medium

### G-13 — No environment gating for Admin and Corporate
`ResolveRoles` parses `environment` out of the group name and never uses it. `resolved.Environment` is set and discarded.

Regional users are gated by accident — group names are looked up in a per-environment database. **Admin and Corporate never touch the database**, so `APP-FlexCount-Corporate-User-HQ-Dev` presented to the Prod endpoint grants Admin.

**Fix:** compare `resolved.Environment` to the running environment and reject a mismatch.

---

### G-14 — Paged enumeration isn't implemented
`GET /Users` with no filter returns an empty list. `GetUsersAsync` exists in `CustomerApiService` and its call site in `UsersController` is commented out.

Entra's periodic full sync therefore reconciles against nothing. Drift never gets corrected.

**Fix:** uncomment and wire it. The Customer API's `GetUsers` endpoint exists — though note it ignores `startIndex`/`count` and returns everyone.

---

### G-15 — The OAuth token call shares the circuit breaker
`GetOrRefreshTokenAsync` posts to `login.microsoftonline.com` using the same `_httpClient` that has the Polly retry and circuit-breaker policies attached for the Customer API.

If the Customer API fails 5 times and opens the circuit, **we also can't fetch a token** — from a completely unrelated service.

**Fix:** a separate, plain `HttpClient` for token acquisition.

---

### G-16 — Dead code
| What | Where | Note |
|---|---|---|
| `RegionGroupMapping` dictionary | `ScimTransformer` | Superseded by `SearchCustomerGroups`. The class XML comment still describes it as live — that's wrong too. |
| `ParseEntitlement()` | `ScimTransformer` | Superseded by `ResolveRoles`. Doesn't handle stringified JSON. Only tests call it. Its error message is what the old test guide expects — that's why the guide's expectations don't match reality. |
| `SetAuthHeader()` (no `Async`) | `CustomerApiService` | Signs an HS256 JWT. Nothing calls it. |
| `BuildSoftDelete()` | `ScimTransformer` | Nothing calls it. |
| `MaskNamePatterns()` | `LogSanitizer` | Commented out at the call site. |
| `MockScimService` | `FlexCount.Scim.Api/Mock` | Not registered in `Program.cs`. 37 tests exercise it. |
| `AzureAdB2CInstance` | `Program.cs` | Read from Key Vault, never used. |

**Fix:** delete. But see G-17 first.

---

### G-17 — `JwtSecret` is dead but still required to start
`CustomerApiService`'s constructor throws if `CustomerApi:JwtSecret` is missing — but the only thing that reads it is the dead `SetAuthHeader()`.

So the secret must exist in every environment for an unused code path.

**Fix:** remove `SetAuthHeader`, `_jwtSecret`, `_jwtIssuer`, `_jwtAudience` and the `?? throw`, then delete the secret. Do it as one change or you break startup.

---

### G-18 — `scimType` is inconsistent
`UsersController` returns `"invalidRoles"`. `ScimExceptionMiddleware` returns `"invalidValue"` for the same class of problem. RFC 7644 §3.12 says `invalidValue` — `invalidRoles` isn't in the list.

The controller's explicit `BadRequest` wins, so callers see `invalidRoles`. Entra only reads the status code.

**Fix:** two string literals in `UsersController`.

---

### G-19 — Startup writes hardcoded fake secrets into log lines
```csharp
string jwtSecret1 = "SomeSuperSecret";
var jwtSecret = jwtSecret1; //KeyVaultSecretManager.GetSecretValueBySecretKey("JwtSettingsSecret");
string jwtIssuer1 = "SomeIssuer";
string jwtAudience1 = "SomeAudience";
```

Only used in a `LogSanitizer.IsSet()` call at startup. Harmless today; lands in a security scanner report tomorrow.

**Fix:** delete with G-17.

---

### G-20 — The scope comment contradicts the code
`ScimAppConstants.cs`:
```
// NOTE: CustomerApiScope is NOT stored in Key Vault.
//       It is constructed at runtime as: https://{AzureAdB2CDomain}.onmicrosoft.com/{AzureAdB2CClientId}/.default
```

`Program.cs`:
```csharp
var customerApiScope = $"https://{b2cDomain}/{b2cClientId}/.default";
```

No `.onmicrosoft.com`. So `AzureAdB2CDomain` must already contain the full domain. If someone "fixes" the secret to match the comment, outbound auth breaks.

The same file also documents `AzureAdB2CInstance` as `https://login.microsoftonline.com/tfp/` — while `Startup.cs` in the Customer API routes tokens by `issuer.Contains("login.microsoftonline.com")` on the assumption B2C uses `b2clogin.com`. Two repos, contradictory expectations for the same secret.

**Fix:** correct the comments, and check the actual vault values before touching either.

---

### G-21 — Token comparison isn't constant-time
```csharp
if (string.IsNullOrEmpty(expectedToken) || incomingToken != expectedToken)
```

`!=` on strings short-circuits on the first differing character. Theoretically timeable over a network. Low practical risk, but it's the kind of thing Target's security review flags.

**Fix:** `CryptographicOperations.FixedTimeEquals` on the UTF-8 bytes.

---

### G-22 — Swagger is on in production
`app.UseSwagger()` and `UseSwaggerUI()` are unconditional. `HomeController.Index` redirects `/` to `/swagger`.

**Fix:** wrap in `if (app.Environment.IsDevelopment())`, or accept it as a decision and write it down.

---

### G-23 — Deactivate runs before the existence check
In `PatchUser`, the `IsDeactivation` branch fires **before** the user is fetched. Deactivating an unknown user returns **200** with a made-up body instead of 404.

`DELETE /Users/{id}` returns 204 unconditionally, whether or not the user exists.

**Fix:** fetch first, 404 if absent.

---

### G-24 — `AddUser` ignores `IsActive`
`AddUserDetailsAsync` (Customer API) hardcodes `Status = UserStatus.Active.ToString()`. A `POST` with `"active": false` creates an **Active** user.

Entra does send this shape when a disabled user is first assigned.

**Fix:** honour `userDetailsRequest.IsActive`.

---

### G-25 — `GetUserRoleByType` throws a bare `Exception`
```csharp
_ => throw new Exception($"Invalid user type: {userType}. Allowed values are Admin, Corporate, Regional.")
```

Surfaces as a 500, so SCIM maps it to a 500 for Entra. It should be a 400. Unreachable today because `ResolveRoles` gates upstream — but it's the second line of defence and it's shaped wrong.

**Fix:** `ArgumentException`.

---

## Low

### G-26 — Three constants for one status value
| Where | Value |
|---|---|
| `Enums.UserStatus.InActive.ToString()` (Customer API) | `"InActive"` |
| `WisApp.InactiveStatus` (db services) | `"Inactive"` |
| `ScimTransformer.ApplyPatch` | `"Inactive"` |
| `UsersController` deactivate response | `"InActive"` |

It works by coincidence: only the SCIM value reaches `Status` on the update path, deactivation goes through `Delete()` which uses the db-services constant, and `ToScimUser` only tests `== "Active"`.

**Fix:** one shared constant.

---

### G-27 — `EditAccessforLiveEvents` vs `EditAccessForLiveEvents`
Lowercase `f` on `ScimEditUserDetailsRequest` (Customer API), uppercase on the SCIM sender. Survives only on `PropertyNameCaseInsensitive`.

**Fix:** rename before someone turns that off.

---

### G-28 — Dead customer identifiers
| Where | Value | Read by anything? |
|---|---|---|
| `StaticBearerAuthHandler` claim | `"TARGETCORP"` | No |
| `CustomerApiService.CreateUserAsync` header | `"TARGET001"` | Presence only |
| `ToCreateRequest` body | `CustomerId = 1` | No — service uses the token's `IdCustomer` |
| `SearchCustomerGroupAsync` | `customerIdString = "47"` | No — controller uses the token's `IdCustomer` |
| `ExtractClaims()` fallback | `"svc-flexcount-provisioning@target.com"` | Unreachable — the handler always sets claims |

All five are dead. Four different values for one concept is a trap for the next reader.

**Fix:** delete them, or add a comment saying they're ignored downstream.

---

### G-29 — `ScimEditUserDetailsRequest.IdCustomer` / `IdUser` are always zero
The comment says *"customerid only will come from token"* and the service agrees.

**Fix:** delete the properties.

---

### G-30 — Interface nullability mismatch
```csharp
// interface
Task<List<CustomerGroupResponse?>> SearchCustomerGroupAsync(...);
// implementation
public async Task<List<CustomerGroupResponse>> SearchCustomerGroupAsync(...)
```
Compiles with a warning.

---

### G-31 — `Region_Value` length disagrees
`[StringLength(30)]` on `ScimEditUserRegionGroup` vs `NVARCHAR(60)` in the table. The API is stricter than the database. Harmless for 2–3 digit codes.

---

### G-32 — The seed script only adds, never reconciles
`PS_SSO_Config.sql` uses `WHERE NOT EXISTS` throughout, so it's safe to re-run. But remove a district from the spreadsheet and re-run — the old row stays.

Fine for seeding. Wrong if anyone treats it as the source of truth for the mapping.

---

### G-33 — Secrets in commented-out `launchSettings.json`
Two commented blocks contain a real-looking B2C client secret. Commented out isn't the same as absent — it's in git history.

**Fix:** rotate that secret, delete the blocks.

---

## Test coverage gaps

| Gap | Why it matters |
|---|---|
| **De-duplication is never tested.** Every multi-group test uses 195 + 394, which don't overlap. | The only real overlap is 394 + 398 (both have market 47 and region 300). See TC-N1. |
| **The region guard is never tested.** TC-04 does a name-only patch on an *Admin* — no regions to lose. | TC-N2 is the test that would catch a production data-loss bug. |
| **Ordering is asserted.** The old guides assert `Region4 = [42, 63, 32, 33, 34, 47, 71]` in that order. | The SQL has no `ORDER BY`. Assert on the set. |
| **`UsersControllerTests` is thin** — 20 assertions for a controller with a lot of branching. | Compare with 106 for `ScimTransformer`. |
| **No test for an unknown group** (G-01). | The most likely silent failure has no test. |

---

## Documentation that's wrong

Not code, but it costs people days.

| Document | Claim | Reality |
|---|---|---|
| `FlexCount_SCIM_Test_Guide_v2_1_Staging.docx` | GET returns `roles[0].value = "Admin"` | Returns `""` (G-03) |
| same | "The `id` field is now captured by the code" | It's parsed past and discarded. `ScimRole` has no `Id`. The same document contradicts itself in TC-17b. |
| same | TC-07: "All user details remain on record" | True of the database, **not** of the response body (G-06) |
| `SCIM_Target_Test_Guide_Updated.docx` | PATCH uses `op: "replace"` with plain objects | Entra sends `op: "Add"` with stringified JSON. The code handles both, but this guide is describing the older shape. |
| same | TC-20 detail: `"Missing or unrecognized entitlement role: ..."` | That's dead `ParseEntitlement`'s message. Live path says `"Error resolving roles: No valid entitlement roles found."` |
| same | TC-26 detail: `"roles array must contain at least one valid entitlement value."` | Doesn't exist anywhere in the code. |
| same | TC-23: "value 47 appears in Group398 only" | 394 also has 47. The note contradicts its own table. |

**Both Word guides should be retired.** [04-testing.md](./04-testing.md) replaces them and was written against the code.

---

## Suggested order

**Before the next release**
1. G-C2 — rotate the committed token
2. G-C1 — close the header bypass
3. G-10 — split the try/catch so one policy can't take down login

**Next sprint**
4. G-01 + G-02 — the fail-open and the missing QA seed. Do them together.
5. G-03 — fix the empty role. Stops the redundant PATCH.
6. G-07 — phone types
7. G-08 — stop logging PII

**When you're nearby**
8. G-04/05/06 — the RFC compliance set. Do them together before Target's review.
9. G-16 + G-17 + G-19 — the dead-code sweep. One PR.
10. G-12, G-18, G-21 through G-33 — small and self-contained.

**Needs a decision, not a fix**
- G-13 — is environment gating actually wanted?
- G-14 — do we need full sync, or is delta enough?
- G-22 — Swagger in production: yes or no?

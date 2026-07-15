# How It Works

This page explains the moving parts in plain English, then walks through what actually happens on each kind of request.

Read [README.md](./README.md) first if you haven't.

---

## 1. The idea

Target keeps the list of who works for them. We don't. So instead of us asking "is this person still employed?", Target's Entra ID **pushes** changes to us.

Entra runs a job roughly every 40 minutes. It compares two things:

- **What it wants** — who is assigned to the FlexCount app, and which groups they're in
- **What we have** — it finds this out by calling our `GET /Users` endpoint

If they match, it does nothing. If they don't, it sends us the difference: a `POST` to create someone, a `PATCH` to change them, or a `PATCH` with `active: false` to switch them off.

The user is not involved. They might be asleep. Their account just quietly becomes correct.

---

## 2. Entra doesn't send "groups". It sends "roles".

This trips everyone up, so it's worth being explicit.

You might expect a SCIM `/Groups` endpoint. We don't have one, and we don't need one. Entra is configured so that **each group assigned to the FlexCount app arrives as an entry in the user's `roles` array**. The group name *is* the role.

A user in one group:

```json
"roles": [
  { "value": "APP-FlexCount-Group195-Prod", "displayName": "APP-FlexCount-Group195-Prod", "primary": "false" }
]
```

A user in two groups gets two entries. That's the whole mechanism.

---

## 3. The three roles

There are exactly three kinds of FlexCount user. The Entra group name tells us which one.

| Entra group name | FlexCount role | `IdRole` in the database |
|---|---|---|
| `APP-FlexCount-Corporate-User-HQ-{env}` | Admin | 1 |
| `APP-FlexCount-Corporate-User-{env}` | Corporate | 2 |
| `APP-FlexCount-Group{number}-{env}` | Regional | 3 |

`{env}` is `Dev` or `Prod`. (See [06-known-gaps.md](./06-known-gaps.md) — there is no `QA` group seeded, and the environment suffix is not enforced by the code.)

**Watch the first two.** `Corporate-User-HQ-Dev` and `Corporate-User-Dev` differ by three characters. The code checks for `HQ` **first**, deliberately, because `Corporate-User-` is a prefix of `Corporate-User-HQ-`. Get that order wrong and every Admin silently becomes a Corporate user.

### What each role can do

These two flags are set by this API. Everything else comes from `IdRole`.

| | Admin | Corporate | Regional |
|---|---|---|---|
| Edit Access for Live Events | Yes | Yes | Yes |
| Override Event Closeout Requirements | **Yes** | No | No |
| Has regions? | No | No | **Yes** |

In the database these are rows in `User_Feature_Permission_Mapping`: Feature 23 is Edit Access for Live Events, Feature 24 is Override Event Closeout. Permission 6 means enabled, 7 means disabled.

---

## 4. When a user is in more than one group

Entra lets Target put one person in several groups at once. We have to pick a single role. The rule is a strict pecking order:

**Admin beats Corporate beats Regional.**

| Groups the user is in | What we create | Regions |
|---|---|---|
| Admin + Corporate | Admin | none |
| Admin + Regional | Admin | none |
| Corporate + Regional | Corporate | none |
| Regional only, one group | Regional | that group's regions |
| Regional only, several groups | Regional | all groups' regions, merged |

Two things to note:

- The `primary: true` flag on a role is **ignored**. The code looks at every entry in the array, not just the primary one. Order doesn't matter either.
- Admin and Corporate have **no regions at all**. If someone is Admin + Regional, the Regional group is thrown away and no region lookup happens.

This lives in `ScimTransformer.ResolveRoles()`.

---

## 5. Regions — what they are and where they come from

A Regional user can only see part of Target's business. "Which part" is described by four lists of numbers.

| | Means | Example (Group195) |
|---|---|---|
| Region 1 | Region | `100` |
| Region 2 | Group | `195` |
| Region 3 | Districts | `134, 137, 138` |
| Region 4 | Markets | `42, 63` |

The full mapping today:

| Group | Region 1 | Region 2 | Region 3 | Region 4 |
|---|---|---|---|---|
| **195** | 100 | 195 | 134, 137, 138 | 42, 63 |
| **394** | 300 | 394 | 334, 335, 336, 346, 369 | 32, 33, 34, 47, 71 |
| **398** | 300 | 398 | 340 | 47 |

**These numbers are not in the code.** They live in the database, in `Customer_Groups` and `Customer_Group_Regions`, seeded by `WIS_Database/Scripts/PS_SSO_Config.sql`. This API asks for them at runtime by calling `POST /api/users/SearchCustomerGroups` with the full group names.

> There *is* a hardcoded copy of this mapping in `ScimTransformer.RegionGroupMapping`. **It is dead code.** Nothing reads it. Don't update it and expect anything to happen. See [06-known-gaps.md](./06-known-gaps.md).

### Merging

If a user is in Group195 **and** Group394, we ask for both in one call and merge the answers:

- Region 1 becomes `[100, 300]`
- Region 2 becomes `[195, 394]`
- Region 3 becomes `[134, 137, 138, 334, 335, 336, 346, 369]`
- Region 4 becomes `[42, 63, 32, 33, 34, 47, 71]`

Duplicates are removed **within** each list, not across lists. Group394 and Group398 both contain market `47` — merge those two and you get `47` once.

---

## 6. Who's allowed to call us

Two different mechanisms, pointing in two different directions. Don't mix them up.

### Inbound: Target Entra → this API

A **static bearer token**. One long random string, configured in Entra's provisioning settings and stored in our Key Vault as `ScimToken`. Every request must carry `Authorization: Bearer <that string>`. If it doesn't match, 401.

That's it. No OAuth, no expiry, no rotation schedule. It's a shared password.

`StaticBearerAuthHandler` does the check and, on success, attaches two claims to the request:

- `serviceEmail` — read from the `ScimServiceEmail` secret
- `customerId` — the literal string `"TARGETCORP"` (a placeholder; nothing downstream reads it)

### Outbound: this API → Customer API

**OAuth 2.0 client credentials.** We ask Microsoft for a token using our app registration's client ID and secret, then put it on every outbound call.

```
POST https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token
grant_type=client_credentials
client_id={AzureAdB2CSCIMClientId}
client_secret={AzureAdB2CSCIMSecret}
scope=https://{AzureAdB2CDomain}/{AzureAdB2CClientId}/.default
```

The token is cached in a static field and refreshed 5 minutes before it expires. A `SemaphoreSlim` stops twenty concurrent requests all fetching a token at once. The token is set on each individual `HttpRequestMessage`, never on `DefaultRequestHeaders` — that's deliberate, because `HttpClient` is shared and `DefaultRequestHeaders` isn't thread-safe.

> There's a second, unused method called `SetAuthHeader` in `CustomerApiService` that signs its own HS256 JWT. **Nothing calls it.** It's leftover from an earlier design. See [06-known-gaps.md](./06-known-gaps.md).

### The two headers that carry identity

Every outbound call also sends:

```
X-Customer-Id: TARGET001
X-Service-Account: svc-flexcount-provisioning@target.com
```

The Customer API's `TokenHelper` sees these, looks up the service account email in the `Corporate_User` table, and uses **that user's** `IdCustomer`, `IdUser` and `IdRole` for the rest of the request.

**Why a service account at all?** Because the database requires it. `User_Region` has foreign keys on `Created_By` and `Updated_By` pointing at `Corporate_User(id_User)`. You physically cannot insert a region row without a real user ID behind the call. So `PS_SSO_Config.sql` seeds `svc-flexcount-provisioning@target.com` as a real Admin user on the TARGET FLEXCOUNT customer. Everything SCIM writes is attributed to them.

**The `X-Customer-Id` value doesn't matter.** It's checked for *being present*, never read. The real customer comes from the service account's own database row.

---

## 7. What happens on each request

### `POST /scim/v2/Users` — create

1. Read `customerId` and `serviceEmail` from the claims
2. `ResolveRoles()` — work out Admin / Corporate / Regional. **Throws if no valid group** → 400
3. `GetUserByEmailAsync()` — does this person already exist? If yes → **409 Conflict**
4. If Regional: `SearchCustomerGroupAsync()` with every group name in one call, then `MergeRegions()`
5. `ToCreateRequest()` — build the FlexCount payload
6. `CreateUserAsync()` → `POST /api/users/AddUser`
7. Return **201** with a `Location` header

Admin and Corporate skip step 4 entirely. No database call for regions.

### `PATCH /scim/v2/Users/{id}` — update, role change, deactivate

The `{id}` is the user's email, URL-encoded (`admin.user%40target.com`).

1. `ApplyPatch()` walks every operation in the request and builds **one** update object
2. If a role operation failed validation → **400**, and nothing is applied
3. If `active: false` was seen → call the deactivate endpoint, return 200, stop here
4. If the patch contained roles and the result is Regional → look up and merge regions
5. Fetch the existing user and fill in anything the patch didn't mention (first name, last name, `IdRole`, user type, status)
6. `PUT /api/users/UpdateUserDetails`
7. Fetch the user again and return the fresh copy

Step 5 matters. Entra only sends what changed. If it sends a new last name and nothing else, we have to supply everything else ourselves or the Customer API's `[Required]` validation rejects it.

### `PATCH` with `active: false` — deactivate

Routes to `POST /api/users/DeleteUserByEmail`. Despite the name, **nothing is deleted**. The database sets `Status = 'Inactive'` and `Is_Active = 0` on the role link. The user's rows, regions and history all stay.

Reactivation is `active: true` plus the roles array, in the same PATCH. It works — no duplicate row is created.

### `DELETE /scim/v2/Users/{id}`

Same as deactivate. Returns 204. Entra doesn't actually use this.

### `GET /scim/v2/Users?filter=userName eq "..."`

Entra calls this before every create to check whether the user already exists, and during **Test Connection** with a made-up GUID instead of an email.

- `userName eq "<value>"` → look the value up in the Customer API. Not found → empty list, 200.
- `externalId eq "..."` or `id eq "..."` → always returns an empty list, no downstream call
- Anything else → 400 `invalidFilter`
- No filter at all → returns an empty list. **Paged enumeration is not implemented.**

### The discovery endpoints

`GET /scim/v2/ServiceProviderConfig`, `/ResourceTypes`, `/Schemas` — Entra reads these to learn what we support. **They need no authentication.** That's by design; Entra reads them before it authenticates.

---

## 8. What Entra actually sends (the two formats)

This is the single biggest source of confusion, so here it is directly.

**On `POST`, roles are plain objects:**

```json
"roles": [
  { "value": "APP-FlexCount-Corporate-User-HQ-Dev",
    "displayName": "APP-FlexCount-Corporate-User-HQ-Dev",
    "primary": "false" }
]
```

Note `primary` is the **string** `"false"`, not the boolean `false`. That's why `BooleanStringConverter` exists in `ScimModels.cs` — it accepts `"true"`, `"1"`, `1`, `true` and `false`.

**On `PATCH`, roles are stringified JSON inside a `value` field:**

```json
{
  "op": "Add",
  "path": "roles",
  "value": [
    { "value": "{\"id\":\"0efbaeb9-bac6-4e58-908b-1b55278c0a9f\",\"value\":\"APP-FlexCount-Group195-Dev\",\"displayName\":\"APP-FlexCount-Group195-Dev\"}" }
  ]
}
```

Yes — a JSON string inside a JSON object. That's Entra's `AppRoleAssignmentsComplex` behaviour. `ResolveRoles()` handles it: if the role value starts with `{`, it parses it and pulls out the inner `value`.

The `id` GUID is Entra's app role assignment ID. **We parse past it and throw it away.** `ScimRole` has no `Id` property.

**Phones on PATCH use filter paths:**

```json
{ "op": "Add", "path": "phoneNumbers[type eq \"work\"].value", "value": "1-612-619-9001" }
```

`work` → primary phone. `mobile` → secondary phone.

> On `POST` the buckets are decided by the `primary` flag instead, not by type. These two rules disagree with each other. See [06-known-gaps.md](./06-known-gaps.md).

---

## 9. What we send back

`ToScimUser()` builds the response. Today it returns:

```json
{
  "schemas": ["urn:ietf:params:scim:schemas:core:2.0:User"],
  "id": "admin.user@target.com",
  "userName": "admin.user@target.com",
  "name": { "givenName": "John", "familyName": "Smith" },
  "emails": [{ "value": "admin.user@target.com", "type": "work", "primary": "true" }],
  "active": true,
  "roles": [{ "value": "", "primary": "true" }],
  "meta": { "resourceType": "User", "created": "<now>", "lastModified": "<now>", "version": "W/\"\"" }
}
```

Three of those are wrong and you should know why before you go looking for a bug:

- `roles[0].value` is **empty**. The Customer API's response object has no `UserType` field, so ours never gets filled in.
- `meta.created` and `meta.lastModified` are always **right now**. The Customer API returns `CreatedDate`/`UpdatedDate`; our model expects `CreatedAt`/`UpdatedAt`. The names don't match, so both stay null and fall back to `DateTime.UtcNow`.
- `meta.version` is always `W/""` for the same reason.

All three are in [06-known-gaps.md](./06-known-gaps.md). The empty role is what makes Entra send a redundant roles PATCH every single cycle — it never sees its own value reflected back, so it assumes the update didn't take.

---

## 10. Errors

`ScimExceptionMiddleware` catches exceptions and turns them into SCIM error JSON.

| Exception | HTTP | `scimType` |
|---|---|---|
| `ArgumentException` | 400 | `invalidValue` |
| `KeyNotFoundException` | 404 | `notFound` |
| `UnauthorizedAccessException` | 401 | — |
| `HttpRequestException` (503) | 503 | — (plus `Retry-After: 60`) |
| `HttpRequestException` (409) | 409 | `uniqueness` |
| anything else | 500 | — |

The controllers also return their own errors directly (`BadRequest`, `Conflict`, `NotFound`), and those win — the middleware only sees what escapes.

> The controller returns `scimType: "invalidRoles"` for bad roles. The middleware returns `invalidValue` for the same class of problem. RFC 7644 says `invalidValue`. Entra only looks at the HTTP status code, so it doesn't break anything today. Gap logged.

---

## 11. Resilience

Outbound calls to the Customer API go through Polly (a retry library):

- **Retry**: 3 attempts, exponential backoff with jitter — roughly 2s, 4s, 8s. Only on transient errors and 503.
- **Circuit breaker**: 5 consecutive failures opens the circuit for 30 seconds.
- **Timeout**: 3 minutes on the `HttpClient`.

Polly is pinned to **version 7.x**. Do not upgrade to 8 — the API changed completely and nothing here will compile.

> The OAuth token call uses the same `HttpClient`, so it goes through the same circuit breaker. If the Customer API goes down hard enough to trip the breaker, we also can't fetch a token. Gap logged.

---

## 12. Logging

Serilog, JSON to stdout, picked up by Datadog (the container runs behind `datadog-init`).

`LogSanitizer` exists to keep personal data out of logs. `MaskEmail("john.doe@target.com")` gives `j***@target.com`.

Log lines worth knowing:

- `SCIM_AUDIT event=UserCreated|UserUpdated|UserDeactivated|UserDeleted|AuthFailed` — logged at **Warning** on purpose, so they survive production log levels
- `RAW SCIM ...` — full request payloads, only when `LoggingOptions:EmitRawPayloads` is `true`. It's `true` in Development, `false` everywhere else. **Leave it false in production** — it prints unmasked personal data.

> `CustomerApiService` interpolates raw emails straight into log messages and bypasses `LogSanitizer` entirely. Gap logged.

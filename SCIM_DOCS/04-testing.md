# Testing the SCIM API

How to test this API, and every test case with the expected result.

**Every expectation on this page was verified against the code**, not against the older Word test guides. Where the code does something surprising, that's noted and linked to [06-known-gaps.md](./06-known-gaps.md). If you see the code do something different from this page, the page is wrong — fix it.

---

## Three ways to test

| Way | Speed | What it proves | Use it when |
|---|---|---|---|
| **Unit tests** (`dotnet test`) | seconds | The transformer logic is right | Every change |
| **Postman** | seconds | The API behaves correctly end to end | Every change to a controller or the outbound client |
| **Entra** (real provisioning) | ~40 min per cycle | Entra and we agree on the wire format | Before a release, and when Target asks |

Start with Postman. Only go to Entra when you need to prove the real thing works, because each round trip costs 40 minutes.

---

## 1. Unit tests

```bash
dotnet test
```

~312 assertions across 15 files. The ones that matter most:

| File | Covers |
|---|---|
| `ScimTransformerTests.cs` | Role resolution, region merge, create/patch mapping |
| `ScimTransformerEdgeCaseTests.cs` | Odd payloads, stringified JSON roles |
| `UsersControllerTests.cs` | Controller branching |
| `StaticBearerAuthHandlerTests.cs` | Inbound auth |
| `BooleanStringConverterTests.cs` | Entra's `"true"` / `"false"` strings |
| `CustomerApiServiceTests.cs` | Outbound calls, token caching |

If you change `ScimTransformer`, the tests will tell you. If you change a controller, they're thinner — check `UsersControllerTests.cs` before you trust a green run.

---

## 2. Postman

Everything below is in [`postman/`](./postman/) as a runnable collection.

### Setting it up

1. Import `FlexCount_SCIM.postman_collection.json`
2. Import the environment file for wherever you're testing (`..._Dev`, `..._QA`, `..._Stage`)
3. **Fill in `scim_token` yourself.** It ships blank on purpose.
4. Pick the environment, top right
5. Run

### Where to get the token

Key Vault, secret name `ScimToken`, in that environment's vault. Ask SRE if you don't have access.

**Do not commit it back.** The old collection had a live token in the file. That token should be treated as compromised. If you save your environment with a real value in it, don't push it.

### Variables

| Variable | What it is |
|---|---|
| `scim_base_url` | Base URL for that environment |
| `scim_token` | Your bearer token — **you fill this in** |
| `env_suffix` | `Dev`, or `Prod` in production. Used to build group names. |
| `admin_email`, `corporate_email`, `regional_email`, `multi_*_email` | Test users |

Group names are built as `APP-FlexCount-Group195-{{env_suffix}}`, so the same collection works everywhere.

### Running the whole thing

Order matters — the create tests make the users the update tests need.

```bash
newman run postman/FlexCount_SCIM.postman_collection.json \
  -e postman/FlexCount_SCIM_Dev.postman_environment.json \
  --env-var "scim_token=<paste it here>"
```

---

## 3. Testing through Entra

Slow but it's the only thing that proves the wire format.

1. Go to the `Flex-Count-SCIM-{env}` enterprise app in Entra → **Users and Groups**
2. Make your change (add a user to a group, edit a name, remove an assignment)
3. **Wait ~40 minutes** for the provisioning cycle
4. Check the FlexCount database and portal
5. Check Datadog for the `SCIM_AUDIT` lines

To go faster: **Provisioning → Restart provisioning** forces a cycle. Careful — it triggers a full sync, not just your change.

### Test Connection

Entra's **Test Connection** button fires three requests with made-up GUIDs:

```
GET /scim/v2/Users?filter=userName eq "de849d81-1aed-4721-8345-f554005c5000"
GET /scim/v2/Users?filter=externalId eq "f1ada000-b1e8-4691-80a2-c558210ad387"
GET /scim/v2/Users?filter=id eq "de849d81-1aed-4721-8345-f554005c5000"
```

All three must return **200 with an empty list**. Any error blocks provisioning entirely.

The `externalId` and `id` ones return empty immediately with no downstream call. The `userName` one goes all the way to the Customer API, which accepts a GUID via `IsEmailValidOrGuid()` and returns 404 → we turn that into an empty list.

---

## The group names

| Role | Group |
|---|---|
| Admin | `APP-FlexCount-Corporate-User-HQ-{env}` |
| Corporate | `APP-FlexCount-Corporate-User-{env}` |
| Regional 195 | `APP-FlexCount-Group195-{env}` |
| Regional 394 | `APP-FlexCount-Group394-{env}` |
| Regional 398 | `APP-FlexCount-Group398-{env}` |

## The region values

| Group | Region 1 | Region 2 | Region 3 | Region 4 |
|---|---|---|---|---|
| 195 | 100 | 195 | 134, 137, 138 | 42, 63 |
| 394 | 300 | 394 | 334, 335, 336, 346, 369 | 32, 33, 34, 47, 71 |
| 398 | 300 | 398 | 340 | 47 |

---

# Test cases

## Create

### TC-01 — Create Admin

**Send**
```http
POST /scim/v2/Users
Authorization: Bearer {{scim_token}}
Content-Type: application/scim+json

{
  "schemas": ["urn:ietf:params:scim:schemas:core:2.0:User"],
  "userName": "admin.user@target.com",
  "name": { "givenName": "John", "familyName": "Smith" },
  "emails": [{ "value": "admin.user@target.com", "type": "work", "primary": "true" }],
  "phoneNumbers": [{ "value": "1-612-619-0001", "type": "mobile", "primary": "true" }],
  "active": true,
  "roles": [{ "value": "APP-FlexCount-Corporate-User-HQ-Dev", "displayName": "APP-FlexCount-Corporate-User-HQ-Dev", "primary": "false" }]
}
```

**Expect** — 201, `Location` header present

**In the database**
- `User_Role_Customer_Link.Id_Role` = **1**
- `User_Feature_Permission_Mapping`: Feature 23 → Permission 6 (enabled), Feature 24 → Permission 6 (enabled)
- `Corporate_User.Status` = `Active`
- **No** `User_Region` rows
- `Primary_Phone` = `1-612-619-0001`, `Primary_Phone_Type` = `Mobile` (the `primary: true` flag decides the bucket on create, not the type)

**In the response** — `roles[0].value` is **`""`**, not `"Admin"`. `meta.created` is right now. `meta.version` is `W/""`. All three are known gaps, not bugs you've just found.

---

### TC-02 — Create Corporate

Same as TC-01 with `"roles": [{ "value": "APP-FlexCount-Corporate-User-Dev", ... }]` and `corporate.user@target.com`.

**Expect** — 201

**In the database**
- `Id_Role` = **2** — not 1. This is the prefix-collision test. If you get 1, the `HQ` check has been broken.
- Feature 23 → Permission 6 (enabled), Feature 24 → Permission **7** (disabled)
- No regions

---

### TC-03 — Create Regional, one group

```json
"phoneNumbers": [
  { "value": "1-612-619-0003", "type": "mobile", "primary": "true" },
  { "value": "1-612-619-0004", "type": "work", "primary": "false" }
],
"roles": [{ "value": "APP-FlexCount-Group195-Dev", "displayName": "APP-FlexCount-Group195-Dev", "primary": "false" }]
```

**Expect** — 201

**In the database**
- `Id_Role` = **3**
- `User_Region` rows: `(1,100) (2,195) (3,134) (3,137) (3,138) (4,42) (4,63)` — 7 rows
- Feature 23 → 6, Feature 24 → **7**
- `Primary_Phone` = `1-612-619-0003` (the mobile one — because `primary: true`), `Secondary_Phone` = `1-612-619-0004`

> Note the phone buckets. On create, `primary: true` wins. On patch, `type eq "work"` wins. They disagree — see [06-known-gaps.md](./06-known-gaps.md).

---

## Updates

### TC-04 — Change name and phone (Admin)

```json
{
  "schemas": ["urn:ietf:params:scim:api:messages:2.0:PatchOp"],
  "Operations": [
    { "op": "replace", "path": "name.givenName",  "value": "Jonathan" },
    { "op": "replace", "path": "name.familyName", "value": "Smithson" },
    { "op": "Add", "path": "phoneNumbers[type eq \"work\"].value",   "value": "1-612-619-9001" },
    { "op": "Add", "path": "phoneNumbers[type eq \"mobile\"].value", "value": "1-612-619-9002" }
  ]
}
```

**Expect** — 200 with the full resource

**In the database**
- Name changed
- `Primary_Phone` = `1-612-619-9001` (the **work** one — patch uses type, not the flag)
- `Secondary_Phone` = `1-612-619-9002`
- `Primary_Phone_Type` is **unchanged** — patch never updates the type. Known gap.
- Role unchanged, status unchanged

---

### TC-05 — Corporate becomes Regional (394)

```json
{
  "Operations": [
    { "op": "Add", "path": "roles",
      "value": [
        { "value": "{\"id\":\"0efbaeb9-bac6-4e58-908b-1b55278c0a9f\",\"value\":\"APP-FlexCount-Group394-Dev\",\"displayName\":\"APP-FlexCount-Group394-Dev\"}" }
      ]
    }
  ]
}
```

**Expect** — 200

**In the database**
- `Id_Role` goes 2 → **3**
- Regions appear: `(1,300) (2,394) (3,334) (3,335) (3,336) (3,346) (3,369) (4,32) (4,33) (4,34) (4,47) (4,71)` — 12 rows
- Feature 24 stays disabled

This is also the test that proves stringified-JSON role parsing works. If it fails with a 400, look at the `TrimStart().StartsWith("{")` block in `ResolveRoles`.

---

### TC-06 — Regional changes group (195 → 394)

Same shape as TC-05 on `regional.user@target.com`.

**Expect** — 200. Old 195 regions gone, 394 regions in their place. Full replace, not a merge.

---

## Deactivate

### TC-07 / TC-08 / TC-09 — deactivate Admin / Corporate / Regional

```json
{ "Operations": [{ "op": "replace", "path": "active", "value": "false" }] }
```

**Expect** — 200 with `"active": false`

**In the database**
- `Corporate_User.Status` = `Inactive`
- `User_Role_Customer_Link.Is_Active` = `0`
- The user row, the regions, the permissions — **all still there**

> **The response body is nearly empty.** `name.givenName`, `familyName` and `roles[0].value` all come back as `""`, because the code builds a fresh throwaway object rather than fetching the real user. The *database* keeps everything; the *response* doesn't show it. Known gap. Entra only reads `active`, so it doesn't break anything today.

---

## Reactivate

### TC-10 / TC-11 / TC-12

```json
{
  "Operations": [
    { "op": "replace", "path": "active", "value": "true" },
    { "op": "Add", "path": "roles", "value": [ { "value": "{\"id\":\"...\",\"value\":\"APP-FlexCount-Corporate-User-HQ-Dev\",\"displayName\":\"...\"}" } ] }
  ]
}
```

**Expect** — 200, `"active": true`

**In the database** — `Status` back to `Active`, `Is_Active` back to `1`, role restored, **no duplicate row**.

The order of operations matters: `active` first, then roles. If a patch has `active: false` anywhere in it, the deactivate branch wins and the roles are ignored.

---

## Role transitions

| TC | From → To | New group | Check |
|---|---|---|---|
| **TC-13** | Admin → Corporate | `Corporate-User-Dev` | `Id_Role` 1→2, Feature 24 goes 6→**7** |
| **TC-14** | Corporate → Regional | `Group398-Dev` | `Id_Role` 2→3, regions `(1,300) (2,398) (3,340) (4,47)` |
| **TC-15** | Regional → Corporate | `Corporate-User-Dev` | `Id_Role` 3→2, **all `User_Region` rows deleted** |
| **TC-16** | Corporate → Admin | `Corporate-User-HQ-Dev` | `Id_Role` 2→1, Feature 24 goes 7→**6** |

All four use the stringified-JSON PATCH format from TC-05.

**TC-15 is the one to watch.** It's the case where the region guard *doesn't* fire — because the user is no longer Regional, empty regions mean wipe, which is correct. Compare it with TC-N2 below.

---

## Multi-operation

### TC-17 — name + phone + role in one patch

```json
{
  "Operations": [
    { "op": "replace", "path": "name.givenName",  "value": "Updated" },
    { "op": "replace", "path": "name.familyName", "value": "Name" },
    { "op": "Add", "path": "phoneNumbers[type eq \"work\"].value", "value": "1-612-619-7777" },
    { "op": "Add", "path": "roles", "value": [ { "value": "{\"id\":\"...\",\"value\":\"APP-FlexCount-Group195-Dev\",\"displayName\":\"...\"}" } ] }
  ]
}
```

**Expect** — 200, everything applied together.

`ApplyPatch` builds a single update object from all four operations and sends one `PUT`. There's no way to half-apply it.

---

## Multi-role

### TC-21 — Admin + Corporate → Admin wins

```json
"roles": [
  { "value": "APP-FlexCount-Corporate-User-HQ-Dev", "primary": "true"  },
  { "value": "APP-FlexCount-Corporate-User-Dev",    "primary": "false" }
]
```

**Expect** — 201, `Id_Role` = **1**, Feature 24 enabled, no regions.

The Corporate entry is silently ignored. Note `primary` is set on the Admin one here — but swap the flags and the answer is the same, because `primary` isn't read.

---

### TC-22 — Corporate + Regional → Corporate wins

```json
"roles": [
  { "value": "APP-FlexCount-Corporate-User-Dev", "primary": "true"  },
  { "value": "APP-FlexCount-Group195-Dev",       "primary": "false" }
]
```

**Expect** — 201, `Id_Role` = **2**, **no regions**, and **`SearchCustomerGroups` is never called**.

Check the Datadog trace for this one. If you see a `SearchCustomerGroups` call, the priority logic has regressed.

---

### TC-23 — two Regional groups, merged (195 + 394)

```json
"roles": [
  { "value": "APP-FlexCount-Group195-Dev", "primary": "true"  },
  { "value": "APP-FlexCount-Group394-Dev", "primary": "false" }
]
```

**Expect** — 201, `Id_Role` = 3

**Regions** — 19 rows:
- R1: 100, 300
- R2: 195, 394
- R3: 134, 137, 138, 334, 335, 336, 346, 369
- R4: 42, 63, 32, 33, 34, 47, 71

One `SearchCustomerGroups` call with both names, not two calls.

> **Don't assert on ordering.** The merge follows whatever order the database returns rows in, and nothing in the SQL says `ORDER BY`. Check the *set*, not the sequence.

---

### TC-24 — add a second group to an existing Regional user

PATCH with **both** groups in the roles array. Entra sends the full desired state, not a delta.

**Expect** — 200, same 19 regions as TC-23.

---

### TC-25 — remove one group

PATCH with **only** the remaining group (195).

**Expect** — 200. Down to 7 rows — Group394's values are gone entirely.

This proves `AddOrUpdateUserRegions` is wipe-and-replace, not additive.

---

## Negative cases

### TC-18 — Test Connection probe (must succeed)

Three GETs with GUIDs. All three → **200 + empty list**. Covered above.

---

### TC-19 — bad token

Any SCIM call with `Authorization: Bearer WRONG_TOKEN_VALUE`.

**Expect** — **401**. Nothing else runs. Log line: `SCIM_AUDIT event=AuthFailed reason=InvalidToken`.

---

### TC-20 — invalid role on create

```json
{ "userName": "test.invalid@target.com", "active": true,
  "roles": [{ "value": "INVALID-ROLE-VALUE", "primary": "false" }] }
```

**Expect** — 400

```json
{ "status": 400, "scimType": "invalidRoles",
  "detail": "Error resolving roles: No valid entitlement roles found." }
```

**No user is created.** `ResolveRoles` runs before the duplicate check and before any write.

> `scimType` is `"invalidRoles"`. RFC 7644 says it should be `"invalidValue"`. Entra only reads the status code so it doesn't matter operationally. Known gap.

---

### TC-26 — empty roles array on patch

```json
{ "Operations": [{ "op": "Add", "path": "roles", "value": [] }] }
```

**Expect** — 400

```json
{ "status": 400, "scimType": "invalidRoles",
  "detail": "Role validation failed: No valid entitlement roles found in the provided in roles array." }
```

(Yes, "in the provided in roles array" — the message has a typo. It's copied here exactly so a string comparison matches.)

**The user is unchanged.**

---

### TC-27 — unrecognised role on patch, alongside a valid name change

```json
{
  "Operations": [
    { "op": "replace", "path": "name.givenName", "value": "ShouldNotUpdate" },
    { "op": "Add", "path": "roles", "value": [{ "value": "{\"id\":\"abc123\",\"value\":\"APP-UnknownSystem-SomeRole-Dev\",\"displayName\":\"APP-UnknownSystem-SomeRole-Dev\"}" }] }
  ]
}
```

**Expect** — 400. **The name is not changed.**

The role fails validation, the controller returns before any downstream call, so nothing is applied. That's the atomicity guarantee — it comes from the early return, not from a transaction.

---

# Extra cases (not in the old guides)

These three find real bugs. Add them to your regression run.

### TC-N1 — merge two groups that actually overlap (394 + 398)

```json
"roles": [
  { "value": "APP-FlexCount-Group394-Dev", "primary": "true"  },
  { "value": "APP-FlexCount-Group398-Dev", "primary": "false" }
]
```

**Why this matters:** every existing multi-group test uses **195 + 394**, which have **zero overlap**. So the de-duplication code has never once been exercised. 394 and 398 both contain Region1 `300` and Region4 `47`.

**Expect** — 201, `Id_Role` = 3

**Regions** — market `47` appears **once**, region `300` appears **once**:
- R1: `300` (once, not twice)
- R2: `394, 398`
- R3: `334, 335, 336, 346, 369, 340`
- R4: `32, 33, 34, 47, 71` (`47` once)

If you see duplicates, `MergeRegions`' `GroupBy` has broken.

---

### TC-N2 — name-only patch on a **Regional** user

Take the Regional user from TC-03 (7 region rows). Send:

```json
{ "Operations": [{ "op": "replace", "path": "name.familyName", "value": "Changed" }] }
```

**Why this matters:** TC-04 does the same thing but on an **Admin**, who has no regions to lose. This is the only test that proves the region guard works.

**Expect** — 200, name changed, and **all 7 region rows still there**.

The patch has no roles, so `ParsedGroupNames` is empty, so region lookup is skipped, so `Region1..4` go out as empty arrays. The guard in `UpdateUserCommandHandler` catches it:

```csharp
if (!becomingRegional || hasIncomingRegions)   // false || false = false -> skip
```

**If the regions vanish, that guard has been removed or broken.** That's a production incident — every Regional user would lose their market access on the next name change.

---

### TC-N3 — patch with a group that isn't in the database

```json
{ "Operations": [{ "op": "Add", "path": "roles",
    "value": [{ "value": "{\"id\":\"x\",\"value\":\"APP-FlexCount-Group999-Dev\",\"displayName\":\"APP-FlexCount-Group999-Dev\"}" }] }] }
```

`Group999` matches the `Group\d+` regex, so it's accepted as Regional — but there's no row for it in `Customer_Groups`.

**What actually happens today** — **200 OK**, and the user **keeps their old regions**.

`SearchCustomerGroups` returns 404 → we turn that into an empty list → empty regions → the guard skips → nothing changes. It's logged at Information as "no matching groups found" and nothing else happens.

**This is a fail-open.** A user moved to a group that doesn't exist keeps their old access, and Entra records a success. It's the most likely way this system silently does the wrong thing. See [06-known-gaps.md](./06-known-gaps.md).

Document the current behaviour in your test, and when the gap is fixed, change the expectation to 400.

---

# Checking the database

```sql
-- The user and their role
SELECT cu.Id_User, cu.Email, cu.First_Name, cu.Last_Name, cu.Status,
       cu.Primary_Phone, cu.Primary_Phone_Type, cu.Secondary_Phone, cu.Secondary_Phone_Type,
       urc.Id_Role, r.Name AS RoleName, urc.Is_Active
FROM dbo.Corporate_User cu
JOIN dbo.User_Role_Customer_Link urc ON cu.Id_User = urc.Id_User
JOIN dbo.Role r ON urc.Id_Role = r.Id_Role
WHERE cu.Email = 'regional.user@target.com';

-- Their regions
SELECT ur.Region_Number, ur.Region_Value
FROM dbo.User_Region ur
JOIN dbo.Corporate_User cu ON ur.id_User = cu.Id_User
WHERE cu.Email = 'regional.user@target.com'
ORDER BY ur.Region_Number, ur.Region_Value;

-- Their two feature permissions (23 = Edit Live Events, 24 = Override Closeout)
SELECT ufpm.Id_Feature, ufpm.Id_Permission
FROM dbo.User_Feature_Permission_Mapping ufpm
JOIN dbo.Corporate_User cu ON ufpm.Id_User = cu.Id_User
WHERE cu.Email = 'regional.user@target.com';

-- What groups exist for this customer (if regions aren't resolving, check here first)
SELECT cg.Customer_Group_Name, cg.Id_Role, cg.Is_Active,
       cgr.Region_Number, cgr.Region_Value
FROM dbo.Customer_Groups cg
LEFT JOIN dbo.Customer_Group_Regions cgr ON cg.Id_Customer_Group = cgr.Id_Customer_Group
WHERE cg.Id_Customer = (SELECT Id_Customer FROM dbo.Customer WHERE [Name] = 'TARGET FLEXCOUNT')
ORDER BY cg.Customer_Group_Name, cgr.Region_Number, cgr.Region_Value;
```

---

# Checking the logs

In Datadog, filter on `Application:FlexCount.Scim.Api`.

| Search for | Tells you |
|---|---|
| `SCIM_AUDIT event=UserCreated` | A user was created |
| `SCIM_AUDIT event=UserUpdated` | A user was updated |
| `SCIM_AUDIT event=UserDeactivated` | A user was switched off |
| `SCIM_AUDIT event=AuthFailed` | A bad token hit us |
| `no matching groups found` | **A group wasn't found — TC-N3 territory** |
| `Customer API retry` | Polly is retrying — the Customer API is unhealthy |
| `OAuth token acquired` | We got an outbound token |

Emails are masked as `j***@target.com`. If you need the full address, look in the database with the timestamp.

---

# Before you release

- [ ] `dotnet test` green
- [ ] Postman collection green against Dev
- [ ] **TC-N2 passes** — regions survive a name-only patch on a Regional user
- [ ] **TC-N1 passes** — no duplicates when 394 and 398 merge
- [ ] TC-18 passes — Entra's Test Connection is green
- [ ] `PS_SSO_Config.sql` has been run against the target environment
- [ ] Every secret in [03-configuration.md](./03-configuration.md) exists in that environment's vault
- [ ] `EmitRawPayloads` is `false` (i.e. `ASPNETCORE_ENVIRONMENT` is not `Development`)
- [ ] One real end-to-end run through Entra, with a 40-minute wait, on at least one user

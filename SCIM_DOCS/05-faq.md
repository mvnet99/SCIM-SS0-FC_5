# FAQ

The questions that come up, and the "why is it like that?" ones.

---

## The basics

### Why does provisioning take 40 minutes?

That's Entra's default provisioning cycle. It's Target's setting, not ours. You can force one immediately with **Provisioning → Restart provisioning** in the enterprise app, but that runs a full sync of everyone, not just your change.

### Can we push a change to Target instead of waiting?

No. SCIM is one-directional. Entra calls us; we never call Entra. If a user's data is wrong in FlexCount, the fix is in Entra, and it lands on the next cycle.

### What happens if this API is down when Entra runs?

Entra retries on its own schedule and eventually quarantines the connection if we stay down. Nothing is lost — the next successful cycle sends the full difference. There's no queue on our side and we don't need one.

### Does this API have a database?

No. It holds nothing. Everything goes to the Customer API and out again. Restart it whenever you like.

---

## Roles and groups

### Why is there no `/Groups` endpoint?

Because Entra doesn't send groups as a separate thing. Each group assigned to the FlexCount app comes through as an entry in the user's `roles` array. The group name *is* the role. We only ever need `/Users`.

### Why is `Corporate-User-HQ-` checked before `Corporate-User-`?

Because `Corporate-User-` is a prefix of `Corporate-User-HQ-`. Check the shorter one first and every Admin quietly becomes a Corporate user — no error, no exception, just wrong. The order in `ResolveRoles()` is load-bearing.

### Why doesn't the code use the `primary: true` flag on roles?

Because Entra doesn't set it reliably when someone's in several groups. `ResolveRoles` reads every entry in the array and applies the pecking order (Admin > Corporate > Regional). Which one is marked primary makes no difference, and neither does the order they arrive in.

### Why do Admin and Corporate have no regions?

Product rule — they see everything, so scoping them to a region would be meaningless. The code skips the whole `SearchCustomerGroups` call for them, which is also a nice performance win: an Admin create is two HTTP calls, a Regional create is three.

### There's a `RegionGroupMapping` dictionary in `ScimTransformer` with all the region numbers in it. Is that used?

**No. It's dead code.** Regions come from the database at runtime, via `SearchCustomerGroups`. The dictionary is a leftover from an earlier version where the mapping was hardcoded. The XML comment above the class still describes it as the live mapping — that comment is also wrong.

Update the *database* (`PS_SSO_Config.sql`), never the dictionary.

### How do I add a new regional group, say Group412?

Two steps, in this order:

1. Add rows to `Customer_Groups` and `Customer_Group_Regions` for `APP-FlexCount-Group412-Prod`, following the pattern in `PS_SSO_Config.sql`
2. Ask Target to create the matching group in Entra and assign the app to it

**No code change.** The regex `^Group(\d+)-` picks up any group number automatically.

But be careful about the order. If Target creates the Entra group before the database rows exist, provisioning returns **200 OK** and silently does nothing — see the fail-open below.

---

## The things that look like bugs

### Why does `GET /Users` return `roles[0].value` as an empty string?

Because the Customer API's response object (`UserDetailResponse`) has no `UserType` field. Our `FlexCountUser.UserType` defaults to `""` and nothing ever fills it. So `ToScimUser` puts `""` in the role.

It's a real gap ([06-known-gaps.md](./06-known-gaps.md), G-03). The fix is to derive it from `UserRoleCustomerLink.IdRole`, which *is* returned — the PATCH path already does exactly that.

### Why is `meta.created` always right now?

Name mismatch. The Customer API returns `CreatedDate` / `UpdatedDate`. Our model expects `CreatedAt` / `UpdatedAt`. They don't bind, both stay null, and `ToScimUser` falls back to `DateTime.UtcNow`. Same reason `meta.version` is always `W/""`. Gaps G-04 and G-05.

### Why does Entra send a roles PATCH every single cycle even when nothing changed?

Because of the empty role above. Entra reads our `GET`, sees `roles[0].value = ""`, compares it to what it wants (`APP-FlexCount-Group195-Prod`), decides they don't match, and sends a PATCH to fix it. Every 40 minutes. Forever.

The user stays correct — the PATCH is processed properly each time. It's wasted traffic, not a data problem. Fixing G-03 stops it.

### Why does the deactivate response come back with an empty name and role?

The deactivate branch builds a brand-new throwaway `FlexCountUser` with only the email and status filled in, rather than fetching the real one. The **database keeps everything** — name, role, regions, history. It's only the HTTP response that's hollow. Entra only reads `active`, so nothing breaks. Gap G-06.

### Why do the phone numbers swap between create and patch?

Two different rules:

- **Create** uses the `primary` flag. `primary: true` → primary phone.
- **Patch** uses the type filter. `type eq "work"` → primary phone.

So a user created with a mobile marked `primary: true` gets that mobile as their primary phone. Then the first patch cycle overwrites primary phone with the *work* number. And the phone *type* never updates at all, because Entra sends `.value` paths, not `.type` paths.

Gap G-07. It's genuinely confusing and it's on the list.

### Why is `scimType` `"invalidRoles"` when the RFC says `"invalidValue"`?

Someone picked a more descriptive name. RFC 7644 §3.12 has a fixed list and `invalidRoles` isn't on it. Entra only looks at the HTTP status code, so it's cosmetic — but it's still wrong, and the middleware already returns `invalidValue` for the same class of problem. Two paths, two answers. Gap G-08.

### There's a `SetAuthHeader` method that signs a JWT. Why isn't it used?

Leftover. An earlier design had this API sign its own HS256 token to call the Customer API. We moved to OAuth client credentials and the method was never deleted. `SetAuthHeaderAsync` (with the `Async`) is the live one.

The `JwtSecret` secret it depends on is still loaded at startup, and `CustomerApiService`'s constructor still throws if it's missing — so you can't just delete the secret. Gap G-09.

---

## Auth

### Why a static token for inbound instead of OAuth?

It's what Entra's SCIM provisioning supports most simply, and it's what Target's team configured. Entra can do OAuth bearer, but the shared-secret path is the standard setup and it's what was agreed.

### Where does the token come from and how do I rotate it?

Key Vault, secret `ScimToken`. To rotate: change it in Key Vault, then change it in Target's Entra provisioning settings. There's a gap between the two where every call 401s. There's no overlap mechanism — no "accept old and new for an hour". Plan it.

### What is `X-Customer-Id` for?

Almost nothing. The Customer API checks it's *present* and never reads the value. The real customer comes from looking up the `X-Service-Account` email in the database.

The SCIM API sends three different values in different places — `TARGETCORP` in the claim, `TARGET001` on create, `1` in the create body, `47` on group search. **None of them are read.** They're all dead. Gap G-10 covers cleaning that up.

### Why does the service account need to be an Admin?

It mostly doesn't. Nothing on the SCIM path reads `tokenUserDetails.IdRole` — there's no role policy on `UsersController`.

What it genuinely needs is to **exist as a real user row**. `User_Region` has foreign keys on `Created_By` and `Updated_By` pointing at `Corporate_User(id_User)`. You physically cannot insert a region row without a real user ID behind the call. So the service account is a database constraint, not a permission decision. `Id_Role = 1` is belt-and-braces.

Worth knowing so nobody assumes demoting it would restrict anything.

### Why did `PS_SSO_Config.sql` need `User_Selected_Customer_Link`?

Because `GetUserDetailsByEmail` resolves `IdCustomer` through `UserRoleCustomerLinkIdUserNavigations.SelectMany(x => x.UserSelectedCustomerLinks)`. No link row → `IdCustomer` resolves to `0` → every SCIM write goes nowhere, with no error.

That's what the `-- 4c. Selected Customer Link (The Missing Table)` comment in the script is about. It's the linchpin of the whole S2S path.

---

## Regions

### Why does a name-only change not wipe a Regional user's regions?

There's a guard in `UpdateUserCommandHandler` (in `flexcount-database-services`):

```csharp
bool becomingRegional  = updateUserCommand.UserRoleCustomerLink?.IdRole == 3;
bool hasIncomingRegions = updateUserCommand.Regions != null && updateUserCommand.Regions.Any();

if (updateUserCommand.Regions != null)
    if (!becomingRegional || hasIncomingRegions)
        existingUser.AddOrUpdateUserRegions([...]);
```

A name-only patch sends empty region arrays (they're non-nullable lists, so they serialise as `[]`). `AddOrUpdateUserRegions` is wipe-and-replace — an empty list would delete everything. The guard catches it: Regional + no regions → skip.

**Never remove that guard.** TC-N2 in [04-testing.md](./04-testing.md) is the test that protects it.

### What if a user is moved to a group that doesn't exist in the database?

Today: **nothing happens, and we return 200 OK.**

`SearchCustomerGroups` finds no rows → the Customer API returns 404 → we treat that as an empty list → empty regions → the guard skips → the user keeps their old regions. It's logged at Information as "no matching groups found" and the cycle reports success.

**This is the most likely way this system silently does the wrong thing.** It matters most when Target adds a group before we've seeded it, or on an environment where `PS_SSO_Config.sql` hasn't run. Gap G-01 — the fix is to return 400 when a Regional entitlement resolves to zero groups.

### Why do the merged region values come back in a strange order?

`MergeRegions` iterates groups in whatever order the database returned them, and the SQL has no `ORDER BY`. De-duplication keeps first-seen order. So the sequence is essentially arbitrary.

**Assert on the set, not the order.** The older test guides assert specific orderings — those assertions are testing the database's mood.

---

## Environments

### Why is there no QA group?

`PS_SSO_Config.sql` decides the environment like this:

```sql
DECLARE @IsProdEnv BIT = CASE WHEN LOWER(@@SERVERNAME) LIKE '%prod%' THEN 1 ELSE 0 END;
```

Prod or Dev. Nothing else. So a QA server gets **Dev** group names seeded. If Entra's QA app sends `-QA` group names, no rows match, and every Regional provisioning call silently no-ops with a 200 (see above). Gap G-02.

### Does the code check the `{env}` suffix?

**No.** `ResolveRoles` parses the environment out of the group name and then never uses it. `resolved.Environment` is set and discarded.

Regional users get environment-gated by accident, because group names are looked up in a per-environment database. Admin and Corporate never touch the database at all — so `APP-FlexCount-Corporate-User-HQ-Dev` presented to the Prod endpoint would grant Admin. Gap G-11.

### Can I run this locally against a local Customer API?

Yes. There's a commented-out line in `Program.cs` pointing at `http://localhost:13211/customerapprestapi`. Override `CustomerApi:BaseUrl` through configuration rather than editing the file — that way it can't get committed.

---

## Making changes

### I need to add a new customer, not just Target. What changes?

More than you'd hope. Today the code has Target-shaped constants scattered around:

- `StaticBearerAuthHandler` hardcodes the claim `customerId = "TARGETCORP"`
- `UsersController.ExtractClaims()` falls back to `svc-flexcount-provisioning@target.com`
- `CustomerApiService.CreateUserAsync` hardcodes the header value `"TARGET001"`
- `ToCreateRequest` hardcodes `CustomerId = 1`
- `SearchCustomerGroupAsync` hardcodes `customerIdString = "47"`

The good news: **none of those values are actually read downstream.** Customer identity comes entirely from `ScimServiceEmail` → database lookup. So in principle a second customer needs a second token, a second service account, and their own `Customer_Groups` rows — no code change.

The bad news: there's one `ScimToken` and one `ScimServiceEmail`, so you can't have two customers at once without making both of those a lookup. That's a design change, not a config change. Talk it through before you promise anyone a date.

### Can I upgrade Polly to v8?

No. It's pinned to 7.2.4 in all three `.csproj` files and the comment says so in capitals. The v8 API is a complete rewrite — `HttpPolicyExtensions`, `WaitAndRetryAsync`, `CircuitBreakerAsync` all change shape. It's a project, not a version bump.

### Where do I change the business rules?

| Rule | Where |
|---|---|
| Which group means which role | `ScimTransformer.ResolveRoles()` |
| Which flags each role gets | `ScimTransformer.ToCreateRequest()` and `ApplyRoleResolutionToUpdate()` |
| Which regions a group has | **The database** — `PS_SSO_Config.sql` |
| Which `IdRole` a role maps to | `ScimTransformer.GetIdRole()` and Customer API's `Enums.Roles` — **both**, keep them in step |
| Which feature IDs the flags write to | Customer API's `Enums.Features` (23, 24) and `Enums.Permissions` (6, 7) |

### How do I know my change worked without waiting 40 minutes?

Postman. Everything Entra sends is in the collection. Reach for a real Entra cycle only when you're testing the wire format itself, or before a release.

---

## Ops

### The container won't start

Look for `secretKey 'X' not found in Key Vault` in the logs. That's a missing secret or a managed identity that can't read the vault. [03-configuration.md](./03-configuration.md) has the full list.

### It started but every request 500s with "CustomerApi:BaseUrl is not configured"

The `KeyVaultUrl` environment variable is empty. The app skipped the whole Key Vault block at startup and nothing noticed until the first request. Easy fix, confusing symptom.

### Everything is slow / lots of retries

Check `Customer API retry` in Datadog. Polly retries three times with backoff, so a struggling Customer API turns into 8+ second SCIM calls. If 5 in a row fail, the circuit breaker opens for 30 seconds and everything fails fast.

Worth knowing: the OAuth token call shares the same `HttpClient` and therefore the same circuit breaker. If the Customer API is down hard enough to trip it, we also can't fetch a token. Gap G-12.

### Can I see what Entra actually sent?

Only if `EmitRawPayloads` is on, and it should never be on outside local dev — it prints unmasked personal data. In production you get the masked audit lines and the shape of the request, not the body.

To see a real payload, reproduce it locally with `ASPNETCORE_ENVIRONMENT=Development` and look for `RAW SCIM`.

### Where's the audit trail?

Datadog, `SCIM_AUDIT event=...`, written at Warning so it survives production log levels. Emails are masked to `j***@target.com`. Cross-reference the timestamp with the database if you need the full address.

# FlexCount SCIM API — Start Here

**Repo:** `WIS_SCIMApp_RestAPI`
**Runs as:** a .NET 8 container in Azure Container Apps
**Talks to:** Target's Entra ID (inbound) and WIS_CustomerApp_RestAPI (outbound)

---

## What this service does, in one paragraph

Target's IT team manages their staff in Microsoft Entra ID (the product formerly called Azure AD). When a Target employee needs access to FlexCount, Target adds them to an Entra group. Roughly every 40 minutes, Entra wakes up, notices the change, and calls **this API** to tell us about it. This API translates what Entra sends into the shape FlexCount understands, and passes it down the chain until the user exists in our database with the right role and the right regions. When that person later logs into the FlexCount web portal, the account is already waiting for them.

That's it. This service is a translator. It stores nothing.

---

## Why it exists

Before this, someone at WIS created Target users by hand. That doesn't scale to ~63 users across three regional groups, and it means a Target employee who leaves the company still has a working FlexCount login until somebody remembers to remove it.

SCIM fixes both. Target owns the list. We just receive it.

---

## The word "SCIM"

SCIM stands for *System for Cross-domain Identity Management*. It's an open standard (RFC 7643 and RFC 7644) that defines a REST API for one system to create, update, and deactivate user accounts in another system. Entra ID speaks it natively. We had to build the receiving end.

If you've never seen SCIM before, you only need to know three things:

1. It's plain REST — `POST /Users`, `PATCH /Users/{id}`, `GET /Users?filter=...`
2. Users are JSON objects with a fixed set of fields (`userName`, `name`, `emails`, `active`, `roles`)
3. Entra decides when to call. We never call Entra.

---

## The five-second version of the flow

```
Target Entra ID
      │  every ~40 min, if something changed
      ▼
FlexCount SCIM API          ← you are here
      │
      ▼
WIS_CustomerApp_RestAPI
      │
      ▼
flexcount-database-services
      │
      ▼
SQL Server (WIS_Database)
      │
      ▼
FlexCount web portal — the user logs in and their access is already correct
```

---

## What's in this folder

Read them in this order if you're new:

| File | What it gives you |
|---|---|
| **README.md** | This page |
| **[01-how-it-works.md](./01-how-it-works.md)** | The parts, in plain English, and what happens on each kind of request |
| **[02-architecture.md](./02-architecture.md)** | Diagrams — infrastructure, sequences, decision logic |
| **[03-configuration.md](./03-configuration.md)** | Every setting and secret, what it does, and what breaks without it |
| **[04-testing.md](./04-testing.md)** | How to test this API, and the full list of test cases |
| **[05-faq.md](./05-faq.md)** | "Why is it built like that?" |
| **[06-known-gaps.md](./06-known-gaps.md)** | Bugs and rough edges we know about. **Internal only.** |
| **[postman/](./postman/)** | A runnable Postman collection for every test case |

---

## The one rule for this documentation

**These docs describe what the code does. Not what it was supposed to do.**

There are older Word documents floating around describing this integration. They are out of date and they contradict each other. If a doc and the repo disagree, **the repo is right** — and the doc is a bug. Fix it.

Anything the code does wrong is listed in [06-known-gaps.md](./06-known-gaps.md), not quietly corrected here.

---

## The three projects in this repo

| Project | What lives in it |
|---|---|
| `FlexCount.Scim.Api` | Controllers, authentication, middleware, Key Vault reads, logging helpers |
| `FlexCount.Scim.Domain` | The models, the translator (`ScimTransformer`), and the outbound HTTP client (`CustomerApiService`) |
| `FlexCount.Scim.Tests` | xUnit tests (~312 assertions) and the Postman collection |

If you're looking for the business rules — which Entra group means which FlexCount role — they're in `FlexCount.Scim.Domain/Transformers/ScimTransformer.cs`. That's the most important file in the repo.

---

## Running it locally

1. Open `WIS_SCIMApp_RestAPI.sln` in Visual Studio 2022 (or `dotnet run` from `FlexCount.Scim.Api`)
2. `Properties/launchSettings.json` sets one environment variable: `KeyVaultUrl`. Everything else is pulled from Key Vault at startup.
3. You need Azure CLI logged in (`az login`) with read access to the vault — the code uses `DefaultAzureCredential`.
4. It listens on `https://localhost:65529`. Swagger is at `/swagger`.

If startup fails, read [03-configuration.md](./03-configuration.md) first. It's almost always a missing secret.

---

## Who to ask

| Topic | Who |
|---|---|
| Entra groups, provisioning schedule, Target-side config | Target IT / the Entra provisioning team |
| Key Vault secrets, Container App env vars, APIM | WIS SRE |
| Business rules (roles, regions, permissions) | FlexCount product owner |
| This code | The FlexCount engineering team |

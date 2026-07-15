# Architecture

Diagrams, each with an explanation next to it.

**A note on the syntax:** Azure DevOps renders Mermaid, but it's pinned to an old version with limited syntax support. That means `graph` (not `flowchart`), no icons, no `architecture-beta`. Everything here sticks to what actually renders. The infrastructure diagram uses draw.io instead, because that's the one where Azure icons genuinely help.

---

## 1. System context — who talks to whom

The 30-second picture. If you only look at one diagram, look at this one.

```mermaid
graph LR
    A[Target Entra ID]
    B[FlexCount SCIM API]
    C[WIS_CustomerApp_RestAPI]
    D[flexcount-database-services]
    E[(SQL Server<br/>WIS_Database)]
    F[FlexCount Web Portal]
    G[Target Employee]

    A -->|SCIM 2.0 over HTTPS<br/>static bearer token<br/>every ~40 min| B
    B -->|OAuth client credentials<br/>+ X-Service-Account header<br/>via APIM| C
    C -->|internal service client| D
    D -->|Dapper / EF| E
    G -->|logs in via Azure B2C| F
    F -->|reads role + regions| C
    E -.->|account is already correct| F
```

**Reading it:** the top line is the provisioning path — it runs on a timer, nobody's watching. The bottom line is the login path — it runs when a human shows up. They never touch each other. They meet in the database.

The important thing to understand: **by the time a Target employee clicks "Log in", their role and regions were already written by a job that ran up to 40 minutes ago.** If their access looks wrong, the problem is almost never in the login path.

---

## 2. Azure infrastructure

The runtime picture — what's actually deployed.

> **Source file:** [`diagrams/scim-azure-infrastructure.drawio`](./diagrams/scim-azure-infrastructure.drawio)
> Open it at [app.diagrams.net](https://app.diagrams.net) (or the VS Code draw.io extension), edit, then export a PNG next to it as `scim-azure-infrastructure.png`.
> The `.drawio` file is the source of truth. The PNG is just for people reading the wiki.

What's in it:

| Component | What it is | Notes |
|---|---|---|
| **Azure Container Apps** | Where this API runs | .NET 8 container, built from the repo `Dockerfile` |
| **Azure Key Vault** | All secrets | Read at startup via `DefaultAzureCredential`. Vault URL comes from the `KeyVaultUrl` env var, injected by SRE |
| **Microsoft Entra ID** (`login.microsoftonline.com`) | Issues our outbound OAuth token | We call `/oauth2/v2.0/token` with client credentials |
| **Azure API Management** | Front door for the Customer API | `CustomerApiBaseUrl` points here |
| **AKS** | Where the Customer API runs | Behind an Application Gateway ingress at `/customerapprestapi/*` |
| **Azure SQL** | `WIS_Database` | Only `flexcount-database-services` talks to it |
| **Datadog** | Logs and traces | The container image wraps the app in `datadog-init` |

**Deployment:** `azure-pipeline-ci.yml` builds, `azure-pipeline-cd.yml` releases. Both are thin — the real work is in templates from the `DevOpsPipelineTemplates` repo, on the `release` branch. If you need to change how this deploys, that's where you go, not here.

---

## 3. Create a user — `POST /scim/v2/Users`

The fullest path. Everything else is a subset of this.

```mermaid
sequenceDiagram
    participant E as Target Entra ID
    participant S as SCIM API
    participant T as ScimTransformer
    participant C as Customer API
    participant D as DB Services
    participant Q as SQL

    E->>S: POST /scim/v2/Users<br/>Bearer token + roles[]
    S->>S: StaticBearerAuthHandler<br/>compare token
    Note over S: no match to ScimToken -> 401

    S->>T: ResolveRoles(roles)
    Note over T: Admin > Corporate > Regional<br/>no valid group -> 400

    S->>C: GET /api/users/GetUserByEmail
    C-->>S: 404 (not found)
    Note over S: 200 here means the user exists -> 409

    alt Regional only
        S->>C: POST /api/users/SearchCustomerGroups<br/>["APP-FlexCount-Group195-Prod"]
        C->>D: SearchCustomerGroupsQuery
        D->>Q: SELECT Customer_Groups + Customer_Group_Regions
        Q-->>D: rows
        D-->>C: CustomerGroupDto[]
        C-->>S: Region1..4 values
        S->>T: MergeRegions() - union + dedupe
    end

    S->>T: ToCreateRequest()
    S->>C: POST /api/users/AddUser<br/>X-Service-Account header
    C->>C: TokenHelper - resolve service account<br/>-> IdCustomer, IdUser, IdRole
    C->>D: CreateUserCommand
    D->>Q: INSERT Corporate_User
    D->>Q: INSERT User_Role_Customer_Link
    D->>Q: INSERT User_Region (x N)
    D->>Q: INSERT User_Feature_Permission_Mapping (x2)
    Q-->>D: ok
    D-->>C: UserDto
    C-->>S: UserDetailResponse
    S-->>E: 201 Created + Location header
```

**Things worth noticing:**

- The duplicate check is a real HTTP call. Every create costs two round trips minimum.
- Admin and Corporate skip the whole `SearchCustomerGroups` block. No region lookup at all.
- Multiple groups = **one** call, not one per group. `SearchCustomerGroups` takes an array.
- The two `User_Feature_Permission_Mapping` rows are always written — Feature 23 and Feature 24, enabled or disabled based on the role.

---

## 4. Change a role — `PATCH /scim/v2/Users/{id}`

The one with the most branches.

```mermaid
sequenceDiagram
    participant E as Target Entra ID
    participant S as SCIM API
    participant T as ScimTransformer
    participant C as Customer API
    participant D as DB Services

    E->>S: PATCH /scim/v2/Users/user%40target.com<br/>Operations[]
    S->>T: ApplyPatch() - walk every op,<br/>build ONE update object

    alt role validation failed
        S-->>E: 400 invalidRoles - nothing applied
    end

    alt active = false
        S->>C: POST /api/users/DeleteUserByEmail
        C->>D: DeleteUserCommand
        Note over D: Status = 'Inactive'<br/>Is_Active = 0<br/>rows preserved
        S-->>E: 200 (hollow body - see known gaps)
    end

    alt patch contained roles AND result is Regional
        S->>C: POST /api/users/SearchCustomerGroups
        C-->>S: group regions
        S->>T: MergeRegions()
    end

    S->>C: GET /api/users/GetUserByEmail
    C-->>S: existing user
    Note over S: fill the blanks Entra didn't send:<br/>FirstName, LastName, IdRole,<br/>UserType, Status

    S->>C: PUT /api/users/UpdateUserDetails
    C->>D: UpdateUserCommand
    Note over D: region guard:<br/>if Regional AND no regions sent<br/>-> keep existing regions
    S->>C: GET /api/users/GetUserByEmail
    C-->>S: fresh user
    S-->>E: 200 + full resource
```

**The region guard is the important bit.** In `UpdateUserCommandHandler`:

```csharp
bool becomingRegional  = updateUserCommand.UserRoleCustomerLink?.IdRole == 3;
bool hasIncomingRegions = updateUserCommand.Regions != null && updateUserCommand.Regions.Any();

if (updateUserCommand.Regions != null)
    if (!becomingRegional || hasIncomingRegions)
        existingUser.AddOrUpdateUserRegions([...]);
```

`AddOrUpdateUserRegions` is **wipe-and-replace** — anything not in the incoming list gets deleted. So the guard matters:

| Role after the patch | Regions sent? | What happens |
|---|---|---|
| Regional | yes | replace with the new set |
| Regional | **no** | **skip — keep what they had** |
| Admin / Corporate | no | wipe (correct — they shouldn't have any) |
| Admin / Corporate | yes | replace (shouldn't happen) |

That second row is why a name-only change doesn't destroy a Regional user's market access. If you ever touch this handler, that's the line to be careful with.

---

## 5. How a role is decided

The single most important piece of logic in the repo. `ScimTransformer.ResolveRoles()`.

```mermaid
graph TD
    A[roles array from Entra] --> B{empty or null?}
    B -->|yes| Z[throw ArgumentException<br/>400 to Entra]
    B -->|no| C[for each role]

    C --> D{value starts with '{' ?}
    D -->|yes| E[parse JSON,<br/>take inner 'value']
    D -->|no| F[use value as-is]
    E --> G
    F --> G{starts with<br/>APP-FlexCount- ?}

    G -->|no| H[skip this one]
    G -->|yes| I{which pattern?}

    I -->|Corporate-User-HQ-| J[hasAdmin = true]
    I -->|Corporate-User-| K[hasCorporate = true]
    I -->|Group + digits| L[add to groupNames]

    H --> M{more roles?}
    J --> M
    K --> M
    L --> M
    M -->|yes| C
    M -->|no| N{hasAdmin?}

    N -->|yes| O[Admin - no groups]
    N -->|no| P{hasCorporate?}
    P -->|yes| Q[Corporate - no groups]
    P -->|no| R{any groups?}
    R -->|yes| S[Regional + groupNames]
    R -->|no| Z
```

**Read the order.** `Corporate-User-HQ-` is checked **before** `Corporate-User-`, because the second is a prefix of the first. Swap them and every Admin becomes a Corporate user with no error anywhere.

Also note: `primary` is never looked at. Every role in the array is considered. That's intentional — Entra doesn't reliably set `primary` on multi-group users.

---

## 6. Where the data lands

What a create actually writes.

```mermaid
graph TD
    A[SCIM POST /Users] --> B[Corporate_User<br/>Email, First_Name, Last_Name,<br/>Status, Primary_Phone...]
    A --> C[User_Role_Customer_Link<br/>Id_Role 1/2/3, Id_Customer,<br/>Is_Active]
    A --> D[User_Region<br/>one row per region value<br/>Regional only]
    A --> E[User_Feature_Permission_Mapping<br/>Feature 23 + Feature 24]

    F[(Customer_Groups)] -.->|read at runtime| G[SearchCustomerGroups]
    H[(Customer_Group_Regions)] -.->|read at runtime| G
    G -.-> D

    B --> I[TokenHelper.GetAccessToken<br/>at login]
    C --> I
    D --> I
    E --> I
    I --> J[HS256 JWT with claims:<br/>RoleId, Permissions, UserRegions]
    J --> K[Web portal renders<br/>the right menus + regions]
```

**The join between the two halves.** SCIM writes `Id_Role` and `User_Region` rows. Hours later, at login, `TokenHelper.GetAccessToken` reads those same rows back and packs them into the token the web app decodes. That's how an Entra group assignment ends up deciding which buttons a person can see.

`Customer_Groups` and `Customer_Group_Regions` are **read-only** from SCIM's point of view. We never write them. They're seeded by `PS_SSO_Config.sql`.

---

## 7. Startup

What happens when the container boots. Useful when it doesn't.

```mermaid
graph TD
    A[Container starts] --> B[Serilog bootstrap logger]
    B --> C{KeyVaultUrl env var set?}
    C -->|no| D[skip Key Vault<br/>all CustomerApi settings stay empty]
    C -->|yes| E[read 10 secrets via DefaultAzureCredential]
    E --> F{secret missing?}
    F -->|yes| G[throw - container fails to start<br/>look for 'not found in Key Vault']
    F -->|no| H[build scope:<br/>https://domain/clientId/.default]
    H --> I[AddInMemoryCollection<br/>push secrets into IConfiguration]
    D --> J
    I --> J[register StaticBearer auth]
    J --> K[register HttpClient + Polly]
    K --> L[Swagger, controllers, middleware]
    L --> M[app.Run - listening]

    D -.->|first request| N[CustomerApiService constructor<br/>throws InvalidOperationException<br/>'CustomerApi:BaseUrl is not configured']
```

**The failure mode to know:** if `KeyVaultUrl` is empty, the app **starts fine** and then fails on the first request with a config error. It looks like a runtime bug. It's a missing environment variable. See [03-configuration.md](./03-configuration.md).

---

## 8. Which layer owns what

| Question | Answered by |
|---|---|
| Is this caller allowed in? | `StaticBearerAuthHandler` (SCIM API) |
| Which FlexCount role does this Entra group mean? | `ScimTransformer.ResolveRoles` (SCIM API) |
| Which regions does Group195 have? | `Customer_Groups` / `Customer_Group_Regions` (database) |
| Which customer are we writing to? | `TokenHelper` (Customer API), from the service account row |
| Should these regions be replaced or kept? | `UpdateUserCommandHandler` (DB services) |
| What can this user see in the portal? | `TokenHelper.GetAccessToken` claims (Customer API), at login |

If you're about to add logic, check this table first. Most "where should this go?" questions answer themselves once you know region values live in the database and role mapping lives in code.

# PEWO — Complete End-to-End Package
## Post-Event Workflow Orchestrator — Production Ready

---

## Package Contents

```
01_WIS_Database/StoredProcedures/     ← 10 SP scripts (CREATE OR ALTER — rerunnable)
02_flexcount-database-services/       ← All C# entities, handlers, service, controller, wrapper
03_WIS_WebApp_RestAPI/                ← All C# models, interfaces, services, controller, filter
04_Seeds/                             ← 6 rerunnable DACPAC post-deployment SQL files
05_Tests/                             ← PewoControllerTest, PewoWorkerServiceTest, PewoStepServiceTest
06_Postman/                           ← DB Services API collection + Web REST API collection
```

---

## Run Order (Do This Once Per Environment)

### Step 1 — Database: Run SPs

Run all 10 SP scripts in `01_WIS_Database/StoredProcedures/` against your target database.
All use `CREATE OR ALTER` — safe to rerun. Order does not matter for SPs.

**Critical SP: `usp_Pewo_GetDueJobs.sql`** — contains the fan-out logic that automatically
creates GM_PRC_DELIVERY child runs when GM_TOTALS_CHECK completes. Run this even if the SP
already exists — it adds the fan-out INSERT that makes Day 2 work automatically.

### Step 2 — Database: Run Seed Files

```
1. 04_Seeds/PEWO_Seed_StepKind.sql
2. 04_Seeds/PEWO_Seed_TARGET_GM_TOTALS_CHECK.sql
3. 04_Seeds/PEWO_Seed_TARGET_GM_PRC_DELIVERY.sql       ← sourceContainer = output-files
4. 04_Seeds/PEWO_Seed_TARGET_GM_PRC_DELIVERY_SUMMARY.sql
5. 04_Seeds/PEWO_Seed_TARGET_MEO_DAILY.sql
6. 04_Seeds/PEWO_Seed_TARGET_MEO_WEEKLY.sql
```

All seeds resolve `id_Customer` dynamically:
```sql
SELECT id_Customer FROM dbo.Customer WHERE Name = 'TARGET FLEXCOUNT' AND is_Deleted = 0
```

### Step 3 — flexcount-database-services: Drop in files

| Folder | Files |
|---|---|
| `WS.FC.Dapper.Domain/Entities/` | PewoEntities.cs, PewoSpResultEntities.cs |
| `WS.FC.Dapper.Shared/DTOs/Pewo/` | PewoDtos.cs |
| `WS.FC.Dapper.Shared/Queries/Pewo/` | PewoQueries.cs |
| `WS.FC.Dapper.Shared/Commands/Pewo/` | PewoCommands.cs |
| `WS.FC.Dapper.Domain/Interfaces/Services/` | IPewoDataService.cs |
| `WS.FC.Dapper.Application/Handlers/Pewo/` | All 12 handler files |
| `WS.FC.Dapper.Application/Services/` | PewoDataService.cs |
| `WS.FC.Dapper.WebAPI/Controllers/` | PewoDataController.cs |
| `WS.FC.DatabaseService.Wrapper/Interfaces/` | IPewoDataServiceClient.cs |
| `WS.FC.DatabaseService.Wrapper/ServiceClients/` | PewoDataServiceClient.cs |

**Two line additions to existing files (see Registrations/ folder):**
```csharp
// WS.FC.Dapper.Application/Configurations/ServiceRegistration.cs
services.AddTransient<IPewoDataService, PewoDataService>();

// WS.FC.DatabaseService.Wrapper/Configuration/ServiceRegistration.cs
services.ConfigureServiceUrl<IPewoDataServiceClient, PewoDataServiceClient>(dataServiceBaseUrl, "PewoData");
```

### Step 4 — WIS_WebApp_RestAPI: Drop in files

| Folder | Files |
|---|---|
| `Domain/ApiModels/Pewo/` | PewoModels.cs |
| `Domain/Services/Interfaces/Pewo/` | IPewoServices.cs |
| `Domain/Services/Pewo/` | PewoWorkerService.cs, PewoStepService.cs, PewoDataServices.cs |
| `API/Controllers/` | PewoController.cs |
| `API/Filters/` | PewoApiKeyFilter.cs |

**Additions to existing files:**

`Domain/Constants/WISAppConstants.cs` — add one constant:
```csharp
public const string PewoApiKey = "KeyVaultSettings:PewoApiKey";
```

`API/Configurations/ServicesConfiguration.cs` — add 4 lines:
```csharp
services.AddTransient<IPewoWorkerService,  PewoWorkerService>();
services.AddTransient<IPewoStepService,    PewoStepService>();
services.AddTransient<IPewoJobDataService, PewoJobDataService>();
services.AddTransient<IPewoLogService,     PewoLogService>();
```

`Domain.csproj` — add NuGet:
```xml
<PackageReference Include="Cronos" Version="0.8.4" />
```

**No changes to IAzureBlobStorageHelper or AzureBlobStorageHelper.**

### Step 5 — Key Vault: One new secret

| Secret | Value |
|---|---|
| `PewoApiKey` | Any strong string — must match X-Api-Key header sent by Container App Job |

### Step 6 — EventService integration (5 lines)

In your existing `EventService.CloseEventAsync`, after the event closes:
```csharp
try
{
    await _pewoJobDataService.CreateRunOnEventCloseAsync(
        idCustomer, idEvent, storeNo, storeName, eventDate,
        null, eventGuid, idStore, "CLOSED", eventScheduledDate, cancellationToken);
}
catch (Exception ex)
{
    _logger.LogError(ex, "[PEWO] event-close prime failed Event={Id}", idEvent);
    // Never block event close if PEWO fails
}
```

---

## Tests

Copy all 3 test files from `05_Tests/` into your existing test project:
```
Tests/System/Controllers/PewoControllerTest.cs
Tests/System/Services/PewoWorkerServiceTest.cs
Tests/System/Services/PewoStepServiceTest.cs
```

---

## Postman Collections

Import both collections from `06_Postman/`:

**PEWO_DbServices_Postman_Collection.json** — 12 DB Services endpoints, full Day 1 + Day 2 + MEO flow with auto-captured IDs.

**PEWO_WebRestAPI_Postman_Collection.json** — End-to-end Web REST API flow:
- `01_Event_Close` — simulates UI event close
- `02_Worker_Run` — simulates Container App Job + security tests
- `03_Individual_Steps_Day1` — TOTALS_CHECK + EMAIL
- `04_Individual_Steps_Day2` — READ_BLOB_ZIP + SFTP + ARCHIVE + EMAIL_SUMMARY
- `05_Manual_Retry` — reset failed run
- `06_MEO_Stubs` — stub calls for reference

Set collection variables before running:
```
baseUrl    = https://localhost:7141  (DB Services)  or  https://localhost:7200 (Web REST API)
apiKey     = YOUR_LOCAL_TEST_KEY_123
idCustomer = 47
idEvent    = your real dev event id
eventGuid  = your real dev event guid
```

---

## Local Debugging Flow

```
1. VS Instance 1: DB Services API → F5 → port 7141
2. VS Instance 2: Web REST API    → F5 → port 7200
3. SSMS: query window open

4. Upload test blobs (if no real event):
   az storage blob upload --account-name stgfcdev4ue --container-name output-files
     --name "{eventGuid}/TAR_ITM_GM_0421_20250101_120000.txt" --file TAR_ITM_GM_0421_20250101_120000.txt
   (repeat for PRPC file)

5. Postman: POST /api/Pewo/event-close (once)

6. PowerShell loop (simulates Container App Job):
   while ($true) {
     Invoke-RestMethod -Method POST -Uri 'https://localhost:7200/api/Pewo/worker/run'
       -Headers @{'X-Api-Key'='YOUR_LOCAL_TEST_KEY_123'} -SkipCertificateCheck
     Start-Sleep -Seconds 30
   }

7. Watch breakpoints fire in PewoWorkerService → PewoStepService (TOTALS_CHECK → EMAIL)

8. SSMS: confirm GM_TOTALS_CHECK COMPLETED
   Advance GM_PRC_DELIVERY: UPDATE dbo.Pewo_Schedule SET Next_Run_At=DATEADD(MINUTE,-1,GETUTCDATE())
   WHERE ... WorkflowType_Code='GM_PRC_DELIVERY'

9. PowerShell loop picks up Day 2 automatically on next tick
   Watch: READ_BLOB_ZIP → SFTP → ARCHIVE

10. Ctrl+C to stop PowerShell loop
```

---

## Architecture Summary

```
UI closes event
    └──► EventService.CloseEventAsync() + 5-line PEWO hook
              └──► POST /api/Pewo/event-close
                        └──► usp_Pewo_CreateRunOnEventClose
                                  └──► Pewo_WorkflowRun (PENDING, GM_TOTALS_CHECK)
                                  └──► Pewo_WorkflowRunEvent (Event_Guid captured)
                                  └──► Pewo_Schedule.Next_Run_At = NOW

Container App Job (every 10 min)
    └──► POST /api/Pewo/worker/run  [X-Api-Key]
              └──► usp_Pewo_GetDueJobs
                        Day 1: GM_TOTALS_CHECK due → SCHEDULE
                              └──► TOTALS_CHECK (ValidateNgen via ITotalsValidationService)
                              └──► EMAIL

              └──► usp_Pewo_GetDueJobs (next morning 8AM)
                        Day 2: GM_PRC_DELIVERY schedule due
                              Fan-out: creates NEW_CHILD runs per completed GM_TOTALS_CHECK
                              └──► READ_BLOB_ZIP (output-files/{eventGuid}/ → flexcount-save)
                              └──► SFTP (flexcount-save → /www.data/target-ssh/nexgen)
                              └──► ARCHIVE (output-files originals → flexcount-save)
```

---

## Open Items (Not Blocking NGen Delivery)

| # | Item |
|---|---|
| 01 | TOTALS_CHECK — replace in-process ValidateNgen call with HTTP call when dedicated API ready |
| 02 | MEO TRANSFORM — EBCDIC conversion (EBCDICFileHelper.cs exists), blocked on MEO blob location |
| 03 | MEO file location — icnts.dat/cpinv.dat/CTL file blob path not confirmed |
| 04 | MEO email totals — per-store Records/Qty/Ext data |
| 05 | MEO three SFTP destinations — target-ssh, target3-ssh, excell |
| 06 | SFTP dev credentials — confirm LegacyHostName etc. in dev Key Vault or add local bypass |

---

## No Additional DB Schema Changes

Tables and schema already deployed. SPs are CREATE OR ALTER — rerunnable.
Seed files are MERGE-based — rerunnable. Zero data loss on re-run.

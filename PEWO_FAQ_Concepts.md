# PEWO — Post-Event Workflow Orchestrator
## Frequently Asked Questions & Concepts Guide

---

## What Is PEWO?

PEWO (Post-Event Workflow Orchestrator) automates the NGen/PRPC file delivery process for TARGET after inventory events close. It replaces manual SRE Unix scripts with a fully automated, resumable, auditable, and data-driven workflow system built inside the FlexCount Web REST API.

The entire system is driven by seed data — workflows, steps, schedules, recipients, container names, SFTP paths are all configured in the database. No code changes are needed to add a step, change a recipient, or onboard a new customer.

---

## Table of Contents

1. [Schedules](#schedules)
2. [Workflows and Steps](#workflows-and-steps)
3. [Step Kinds](#step-kinds)
4. [How a Job Is Triggered](#how-a-job-is-triggered)
5. [Job Sources — SCHEDULE, PENDING_EVENT, NEW_CHILD, RETRY, SAFETY_NET](#job-sources)
6. [Fan-Out](#fan-out)
7. [Artifact_Ref — Step Output Tracking](#artifact_ref)
8. [Resume-Not-Restart](#resume-not-restart)
9. [Retry and Backoff](#retry-and-backoff)
10. [Manual Retry](#manual-retry)
11. [Dead Letter](#dead-letter)
12. [Security](#security)
13. [Logging](#logging)
14. [Adding a New Customer](#adding-a-new-customer)
15. [Adding or Removing a Step](#adding-or-removing-a-step)
16. [Local Debug Order](#local-debug-order)
17. [Key Tables Reference](#key-tables-reference)

---

## Schedules

A schedule defines **when** a workflow fires. It lives in `Pewo_Schedule` and has one row per workflow type per customer.

The schedule is seeded once and never manually edited in production. The only column that changes at runtime is `Next_Run_At` — the worker advances it automatically after each run completes.

There are two types of schedule:

**Cron-based** — fires at a fixed time every day. Example: `GM_PRC_DELIVERY` fires at 8AM UTC daily. `Next_Run_At` is advanced to the next cron occurrence after each run.

**Event-driven** — fires when an inventory event closes. The `Cron_Expression` column is set to the sentinel value `ON_EVENT_CLOSE` instead of a real cron. `Next_Run_At` is normally set to 50 years in the future — meaning it never self-fires. When an event closes, `usp_Pewo_CreateRunOnEventClose` sets `Next_Run_At = NOW` so the next worker tick picks it up. After the run completes the worker resets `Next_Run_At` back to 50 years.

**Why 50 years?** It is a sentinel — a way to keep the schedule ACTIVE in the database while preventing it from being triggered by the regular cron check. The worker recognizes `ON_EVENT_CLOSE` and skips advancing the schedule on failure to protect other events that may be waiting.

**To add a new schedule** — run the seed script for that workflow. That is all. The worker picks it up automatically on the next tick.

---

## Workflows and Steps

A workflow is a named sequence of steps for a specific customer. It lives in `Pewo_CustomerWorkflowType` and `Pewo_WorkflowStepDef`.

Currently two workflows exist for TARGET (id_Customer = 47):

**GM_TOTALS_CHECK**
Fires when event closes. Validates NGen totals and sends a notification email.
```
Step 1: TOTALS_CHECK  — validate GM file headers and totals
Step 2: EMAIL         — notify ops team that totals passed
```

**GM_PRC_DELIVERY**
Fires at 8AM UTC next day. Delivers files to TARGET SFTP.
```
Step 1: READ_BLOB_ZIP — download source files, zip in memory, stage in blob
Step 2: SFTP          — deliver zips to TARGET SFTP server
Step 3: ARCHIVE       — copy original txt files to archive container
Step 4: EMAIL         — notify ops team that delivery completed
```

Steps are ordered by `Step_Order`. The worker executes them in ascending order, one at a time.

---

## Step Kinds

Step kinds are the vocabulary of what a step can do. They are seeded once in `Pewo_StepKind`. Adding a new kind requires adding a row to `Pewo_StepKind` and implementing the corresponding case in `PewoStepService.cs`.

| Step Kind | What It Does |
|---|---|
| `TOTALS_CHECK` | Calls `ITotalsValidationService.ValidateNgen` — validates GM file headers and totals against expected values |
| `READ_BLOB_ZIP` | Lists source txt files from `output-files/{eventGuid}/`, zips each individually in memory, uploads zips to staging blob container |
| `SFTP` | Downloads zips from staging, writes to temp file, delivers to TARGET SFTP via SSH key auth, deletes temp file |
| `ARCHIVE` | Copies original txt files from source container to archive container under `{eventGuid}/` subfolder |
| `EMAIL` | Sends per-event notification email via SendGrid using recipients from step Config JSON |
| `EMAIL_SUMMARY` | Waits until all batch delivery runs are terminal then sends one consolidated HTML table email |
| `GET_EVENTS` | MEO stub — discovers blob files by pattern for MEO workflow (not yet implemented) |
| `TRANSFORM` | MEO stub — EBCDIC/ASCII conversion (not yet implemented) |

**Each step's behavior is configured by the `Config` JSON column in `Pewo_WorkflowStepDef`.** For example, the SFTP step reads `remotePath` and `stagingContainer` from Config. The EMAIL step reads `recipients` and `subject`. This means behavior can be changed by updating seed data without touching code.

---

## How a Job Is Triggered

There are two trigger paths:

**Path 1 — Event Close (GM_TOTALS_CHECK)**
```
UI closes event
    → EventService.CloseInventory
        → Fix 10: EventGuid null guard
        → Fix 17: CancellationToken guard
        → PewoJobDataService.CreateRunOnEventCloseAsync
            → usp_Pewo_CreateRunOnEventClose
                → INSERT Pewo_WorkflowRun (PENDING)
                → INSERT Pewo_WorkflowRunEvent (EventGuid, StoreNo)
                → UPDATE Pewo_Schedule SET Next_Run_At = NOW
```

**Path 2 — Container App Job (every 10 minutes)**
```
Container App Job fires
    → POST /api/Pewo/worker/run  (X-Api-Key header)
        → PewoApiKeyFilter validates key against Key Vault
        → 202 Accepted returned immediately
        → Task.Run fires (fire and forget — not tied to HTTP lifetime)
            → PewoWorkerService.RunAsync
                → _isRunning guard (prevents duplicate execution)
                → GetDueJobsAsync → usp_Pewo_GetDueJobs
                → foreach job → execute steps
```

The Container App Job runs every 10 minutes with `parallelism=1` — only one instance ever runs at a time. The Web REST API is a single pod — no horizontal scaling. Together these guarantee sequential predictable execution with no concurrency conflicts.

---

## Job Sources

`usp_Pewo_GetDueJobs` returns jobs tagged with a `Job_Source` value. The worker uses this to decide whether to create a new run or resume an existing one.

**SCHEDULE**
Standard cron-based job. `Next_Run_At <= NOW` and no existing PENDING run for this schedule. Worker creates a new `WorkflowRun` via `CreateWorkflowRunAsync`.

Example: `GM_PRC_DELIVERY` schedule becoming due at 8AM with no active run.

**PENDING_EVENT**
An `ON_EVENT_CLOSE` run that exists as PENDING but whose shared schedule `Next_Run_At` was pushed to 50 years by a sibling run completing first. This handles the case where multiple stores close events simultaneously — the first store's run completes and advances the schedule to 50 years, which would otherwise orphan all other stores' runs permanently.

Guard: `NOT EXISTS ON Pewo_WorkflowStepRun` — only picks up runs where no step has started yet. Once the worker starts executing steps this guard naturally excludes the run from being picked up again.

Worker reuses the existing `id_WorkflowRun` — does not create a new one.

**NEW_CHILD**
A fan-out child run created in the current tick by `usp_Pewo_GetDueJobs` from a completed parent run. Created atomically in a transaction and returned in the same SP call so the worker processes them immediately without waiting for the next tick.

Worker reuses the existing `id_WorkflowRun` — the SP already created it.

**RETRY**
A failed run whose `Retry_At <= NOW` and `Retry_Count < Max_Retries`. Worker reuses the existing `id_WorkflowRun` and resumes from the first non-COMPLETED step.

**SAFETY_NET**
Catches any completed parent run that has no corresponding child delivery run ever created — regardless of age or lookback window. Prevents events being permanently missed if the 8AM schedule is delayed beyond the 24-hour lookback window. Returns `Job_Source = SAFETY_NET` so the worker treats it as a new SCHEDULE-style job and creates a fresh run.

---

## Fan-Out

Fan-out is the mechanism that automatically creates one `GM_PRC_DELIVERY` run per completed `GM_TOTALS_CHECK` run.

It is controlled by `Fan_Out_Source_WorkflowType_Code` on `Pewo_CustomerWorkflowType`:

```
GM_TOTALS_CHECK.Fan_Out_Source_WorkflowType_Code = NULL    ← parent, no fan-out
GM_PRC_DELIVERY.Fan_Out_Source_WorkflowType_Code = 'GM_TOTALS_CHECK'  ← child
```

When `usp_Pewo_GetDueJobs` fires at 8AM it runs a fan-out INSERT block that:

1. Finds all COMPLETED `GM_TOTALS_CHECK` runs within `Fan_Out_Lookback_Hours = 24`
2. For each one that has no existing `GM_PRC_DELIVERY` child run — creates a new `Pewo_WorkflowRun` (PENDING, Batch_Key = today)
3. Copies `Pewo_WorkflowRunEvent` from parent to child (EventGuid, StoreNo, EventDate)
4. Wraps both INSERTs in a transaction — if the second INSERT fails, the first rolls back (no orphaned runs)
5. Returns the new child runs as `NEW_CHILD` source in the same SP call

The `NOT EXISTS Batch_Key = @Today` guard prevents the fan-out from running again on every 10-minute tick after today's child runs are already created.

Each store's delivery run is completely independent — one store failing does not affect others. Each has its own retry count, step history, and artifact refs.

---

## Artifact_Ref

`Pewo_WorkflowStepRun.Artifact_Ref` is a text column that records what a step produced or tracks its idempotency state. It is written by the worker after each step executes and read back by subsequent steps via `GetRunResumeAsync`.

| Step | Artifact_Ref Value | Purpose |
|---|---|---|
| `TOTALS_CHECK` | `{"totalQty":5000,"totalExt":25000}` | Audit of validated totals |
| `READ_BLOB_ZIP` | `staged:file1.zip,file2.zip` | Tells SFTP which zips to deliver |
| `SFTP` | `sftp:delivered:/path:file1.zip,file2.zip` | Idempotency — skip if already delivered |
| `SFTP` (partial) | `sftp:partial:file1.zip` | Mid-delivery progress — retry skips file1 |
| `ARCHIVE` | `archived:2:{eventGuid}` | Confirms count of archived files |
| `EMAIL` | `notified:2026-07-13T08:05:00Z` | Idempotency — never send twice |

**Step-to-step data passing** — when the SFTP step loads it reads `step.Artifact_Ref` from the prior READ_BLOB_ZIP step to know which zips to deliver. If SFTP has no prior artifact_ref of its own, the worker injects the last completed step's artifact_ref automatically via `lastCompletedArtifactRef` tracking.

**SFTP partial delivery** — after each zip is successfully delivered, progress is immediately saved to `Artifact_Ref` as `sftp:partial:{delivered so far}`. On retry, already-delivered zips are skipped. This prevents duplicate SFTP delivery even if the worker crashes mid-loop.

---

## Resume-Not-Restart

Every step writes its result to `Pewo_WorkflowStepRun` immediately after execution. On retry the worker loads all steps via `GetRunResumeAsync`, sees which are already COMPLETED, and skips them.

```
Step 1 READ_BLOB_ZIP  COMPLETED  ← skip on retry
Step 2 SFTP           FAILED     ← resume here
Step 3 ARCHIVE        PENDING    ← not yet run
Step 4 EMAIL          PENDING    ← not yet run
```

This means a retry only re-executes what actually needs to be re-executed. A 4-step workflow where step 3 fails never re-runs steps 1 and 2 — saving time, avoiding duplicate blob writes, and preventing double-delivery.

---

## Retry and Backoff

When a step fails the worker:

1. Sets `Pewo_WorkflowStepRun.Status = FAILED` with `Failure_Details`
2. Sets `Pewo_WorkflowRun.Status = FAILED`
3. Increments `Retry_Count`
4. Sets `Retry_At = NOW + 2^retryCount minutes`
   - Retry 1 → 2 minutes
   - Retry 2 → 4 minutes
   - Retry 3 → 8 minutes
5. On the next worker tick where `Retry_At <= NOW` and `Retry_Count < Max_Retries` — the RETRY source returns the run and the worker resumes from the failed step

`Max_Retries` is configured per workflow type in `Pewo_CustomerWorkflowType` (currently 3 for all workflows).

For `ON_EVENT_CLOSE` workflows — `AdvanceScheduleAsync` is **not called** when a run fails. The shared schedule must not be advanced on failure because other events may be waiting with their own PENDING_EVENT runs.

---

## Manual Retry

When a run exhausts all automatic retries and stays permanently FAILED — SRE investigates the root cause (bad credentials, missing file, network issue), fixes it, then calls:

```
POST /api/Pewo/runs/{id}/retry
Headers: X-Api-Key: <pewo-api-key>
```

This calls `usp_Pewo_ResetRunForRetry` which:

- Sets `Pewo_WorkflowRun.Status = PENDING`
- Clears `Retry_At`, `Reason`, `Finished_At`
- Resets `Retry_Count = 0` — allows 3 fresh automated retries
- Resets only FAILED steps to PENDING — COMPLETED steps are untouched
- Writes an audit entry to `Pewo_WorkflowRunLog`

On the next worker tick the PENDING_EVENT or RETRY source picks up the run and resumes from the first PENDING step. No data loss, no re-execution of completed work.

---

## Dead Letter

When `Retry_Count >= Max_Retries` and a run fails permanently the worker sends a dead letter alert email:

- Reads the `EMAIL` step's `Config` JSON for the same workflow to get recipients
- Overrides the subject with a failure message: `PEWO FAILED — GM_PRC_DELIVERY RunId=5 — Max retries exceeded. Manual intervention required.`
- Calls `IPewoStepService.EmailAsync` — same SendGrid path as normal emails
- Dead letter email failure is always swallowed — never crashes the worker

This means ops is always notified of a permanently failed run without any additional configuration. The same people who receive success emails also receive failure alerts.

---

## Security

**API Key Authentication**
The `POST /api/Pewo/worker/run` endpoint is protected by `PewoApiKeyFilter`. Every request must include `X-Api-Key` header. The filter resolves the expected key from Key Vault via `KeyVaultSecretManager.GetSecretValueBySecretKey(WISAppConstants.PewoApiKey)` on every request — never cached. The key is stored in Key Vault as `PewoApiKey`.

**Container App Job**
The job reads `PewoApiKey` from Key Vault at runtime using its system-assigned managed identity. The key is never hardcoded or stored in plain text. SRE must grant the job's managed identity `GET` permission on Key Vault.

**SFTP**
SSH key authentication. Private key stored in Key Vault as `SFTPServerSSHPrivateKey`. Never uses password auth. The key is resolved at point of use — not cached at startup.

**Blob Storage**
Accessed via `BlobConnectionString` from Key Vault. Resolved at point of use in `PewoStepService` via `GetBlobConnectionString()` — not cached in constructor. This prevents null cache if Key Vault is temporarily unavailable at API startup.

**Has_Post_Event_Workflow Guard**
`usp_Pewo_CreateRunOnEventClose` JOINs `dbo.Customer` and only primes workflows for customers where `Has_Post_Event_Workflow = 1`. New customers are never accidentally enrolled in PEWO just by having workflow type records seeded.

---

## Logging

Every significant event in a run is written to `Pewo_WorkflowRunLog` via `IPewoLogService.LogAsync`. Log entries include:

- `id_WorkflowRun` — which run this log belongs to
- `id_Customer` — customer context
- `Step_Kind` — which step (NULL for run-level entries)
- `Log_Level` — INFO or ERROR
- `Message` — human-readable description
- `Event_Context` — EventGuid for step-level entries
- `logged_date` — UTC timestamp

**Log levels:**
- `INFO` — job picked up, step completed, run completed
- `ERROR` — step failed with failure details, run failed, dead letter sent

**Critical design rule:** `PewoLogService.LogAsync` wraps all DB calls in `try/catch` and swallows exceptions. A logging failure is written to the application log (App Insights / VS Output) but never propagates to the worker. Steps and run status always complete correctly regardless of log failures. This prevents the logging system from crashing the orchestration system.

**Querying logs:**
```sql
-- All logs for a specific run in chronological order
SELECT Step_Kind, Log_Level, Message, logged_date
FROM   Pewo_WorkflowRunLog
WHERE  id_WorkflowRun = <runId>
ORDER  BY logged_date ASC

-- All errors across all runs
SELECT wr.id_WorkflowRun, cwt.WorkflowType_Code,
       wrl.Step_Kind, wrl.Message, wrl.logged_date
FROM   Pewo_WorkflowRunLog wrl
JOIN   Pewo_WorkflowRun wr ON wr.id_WorkflowRun = wrl.id_WorkflowRun
JOIN   Pewo_CustomerWorkflowType cwt ON cwt.id_CustomerWorkflowType = wr.id_CustomerWorkflowType
WHERE  wrl.Log_Level = 'ERROR'
ORDER  BY wrl.logged_date DESC
```

---

## Adding a New Customer

To onboard a new customer to PEWO:

1. Set `Has_Post_Event_Workflow = 1` on the customer row in `dbo.Customer`
2. Write a seed script that inserts into:
   - `Pewo_CustomerWorkflowType` — one row per workflow (GM_TOTALS_CHECK, GM_PRC_DELIVERY)
   - `Pewo_WorkflowStepDef` — one row per step per workflow with Config JSON
   - `Pewo_Schedule` — one row per workflow with correct cron or ON_EVENT_CLOSE
3. Run the seed script
4. Done — the next event close for that customer automatically primes PEWO

No code changes required. The entire system is data-driven.

---

## Adding or Removing a Step

**To add a new step:**
1. Add a row to `Pewo_StepKind` if the step kind is new
2. Add a case to `PewoStepService.cs` switch statement implementing the step logic
3. Insert a row into `Pewo_WorkflowStepDef` for the target workflow with the correct `Step_Order` and `Config` JSON
4. Deploy — the worker picks up the new step automatically on the next run

**To remove a step:**
Simply do not include that `Step_Kind` in the `Pewo_WorkflowStepDef` rows for that workflow. The worker only executes steps that have a definition row — there is no hard-coded step list in the code.

Example: To run GM_PRC_DELIVERY without EMAIL — delete the EMAIL row from `Pewo_WorkflowStepDef` for that workflow. No code change.

**To temporarily disable a step:**
If a `is_Active` column exists on `Pewo_WorkflowStepDef` — set it to 0. The resume query filters it out. If not — delete the row or change `Step_Order` to be after a step that will always fail.

---

## Local Debug Order

```
1. Seed scripts (run once):
   PEWO_Seed_StepKind.sql
   PEWO_Seed_TARGET_GM_TOTALS_CHECK.sql
   PEWO_Seed_TARGET_GM_PRC_DELIVERY.sql

2. Start both APIs in Visual Studio:
   flexcount-database-services → F5 → port 7141
   WIS_WebApp_RestAPI          → F5 → port 7200

3. Prime event close via SSMS:
   EXEC dbo.usp_Pewo_CreateRunOnEventClose
       @id_Customer=47, @id_Event=3839, @Store_No='1047',
       @Event_Guid='your-guid', @id_Store=2106,
       @Event_Status='CLOSED', ...

4. Fire Day 1 worker (Postman):
   POST https://localhost:7200/api/Pewo/worker/run
   Headers: X-Api-Key: <your-key>

5. Verify GM_TOTALS_CHECK completed in SSMS

6. Force GM_PRC_DELIVERY schedule due:
   UPDATE Pewo_Schedule SET Next_Run_At = DATEADD(MINUTE,-1,GETUTCDATE())
   WHERE WorkflowType_Code = 'GM_PRC_DELIVERY'...

7. Fire Day 2 worker (Postman):
   POST https://localhost:7200/api/Pewo/worker/run

8. Verify GM_PRC_DELIVERY completed in SSMS

9. To reset for next debug session:
   DECLARE @RunIds TABLE (id INT)
   INSERT INTO @RunIds SELECT id_WorkflowRun FROM Pewo_WorkflowRun
   WHERE id_CustomerWorkflowType IN (
       SELECT id_CustomerWorkflowType FROM Pewo_CustomerWorkflowType
       WHERE id_Customer = 47)
   DELETE FROM Pewo_WorkflowStepRun  WHERE id_WorkflowRun IN (SELECT id FROM @RunIds)
   DELETE FROM Pewo_WorkflowRunEvent WHERE id_WorkflowRun IN (SELECT id FROM @RunIds)
   DELETE FROM Pewo_WorkflowRunLog   WHERE id_WorkflowRun IN (SELECT id FROM @RunIds)
   DELETE FROM Pewo_WorkflowRun      WHERE id_WorkflowRun IN (SELECT id FROM @RunIds)
```

---

## Key Tables Reference

| Table | Purpose | Written By |
|---|---|---|
| `Pewo_CustomerWorkflowType` | Workflow config per customer — step kinds, max retries, fan-out source | Seed script |
| `Pewo_WorkflowStepDef` | Ordered steps per workflow — step kind, config JSON, max attempts | Seed script |
| `Pewo_Schedule` | When to fire — cron or ON_EVENT_CLOSE, Next_Run_At | Seed script + worker (AdvanceSchedule) |
| `Pewo_StepKind` | Valid step kind vocabulary | Seed script |
| `Pewo_WorkflowRun` | One row per execution — status, retry count, batch key | usp_Pewo_GetDueJobs + usp_Pewo_CreateRunOnEventClose |
| `Pewo_WorkflowRunEvent` | Event metadata per run — EventGuid, StoreNo, EventDate | usp_Pewo_CreateRunOnEventClose + usp_Pewo_GetDueJobs (fan-out) |
| `Pewo_WorkflowStepRun` | One row per step execution — status, artifact_ref, failure details | Worker via usp_Pewo_UpsertStepRun |
| `Pewo_WorkflowRunLog` | Audit log — INFO and ERROR entries per run | Worker via usp_Pewo_InsertLog |
| `Customer` | Has_Post_Event_Workflow flag controls PEWO enrollment | One-time setup per customer |

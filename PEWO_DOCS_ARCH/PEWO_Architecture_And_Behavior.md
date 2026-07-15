# PEWO — Post-Event Workflow Orchestrator
## Architecture, Code Flow & Behavior Documentation

---

## 1. Three-Repo Architecture Overview

```mermaid
graph TB
    subgraph DACPAC["WIS_Database (DACPAC)"]
        direction TB
        subgraph TABLES["Tables"]
            T1["Pewo_CustomerWorkflowType\nWorkflow config per customer"]
            T2["Pewo_WorkflowStepDef\nOrdered steps per workflow"]
            T3["Pewo_Schedule\nWhen to fire each workflow"]
            T4["Pewo_WorkflowRun\nOne row per execution"]
            T5["Pewo_WorkflowRunEvent\nEvent metadata per run"]
            T6["Pewo_WorkflowStepRun\nOne row per step execution"]
            T7["Pewo_WorkflowRunLog\nAudit log entries"]
            T8["Pewo_StepKind\nAllowed step types"]
            T9["Customer\nHas_Post_Event_Workflow flag"]
        end
        subgraph SPS["Stored Procedures"]
            SP1["usp_Pewo_CreateRunOnEventClose\nPrimes ON_EVENT_CLOSE workflows"]
            SP2["usp_Pewo_GetDueJobs\nFan-out + returns due jobs"]
            SP3["usp_Pewo_GetRunResume\nLoads steps for a run"]
            SP4["usp_Pewo_GetWorkflowRunEvents\nLoads event metadata"]
            SP5["usp_Pewo_UpsertStepRun\nSaves step result"]
            SP6["usp_Pewo_SetRunTerminalStatus\nMarks run COMPLETED/FAILED"]
            SP7["usp_Pewo_AdvanceSchedule\nUpdates Next_Run_At"]
            SP8["usp_Pewo_ResetRunForRetry\nManual retry reset"]
            SP9["usp_Pewo_InsertLog\nWrites audit log"]
            SP10["usp_Pewo_GetBatchRunStatus\nBatch delivery status"]
        end
    end

    subgraph DBSVC["flexcount-database-services :7141"]
        direction TB
        subgraph CTRL_DB["API Layer"]
            DC["PewoDataController\n12 HTTP endpoints"]
        end
        subgraph APP_DB["Application Layer"]
            MED["MediatR Dispatcher"]
            H1["GetDueJobsHandler"]
            H2["GetRunResumeHandler"]
            H3["GetWorkflowRunEventsHandler"]
            H4["UpsertStepRunHandler"]
            H5["SetRunTerminalStatusHandler"]
            H6["AdvanceScheduleHandler"]
            H7["InsertLogHandler"]
            H8["ResetRunForRetryHandler\n+ 4 more handlers"]
        end
        subgraph REPO["Data Layer"]
            DAP["Dapper + IGenericRepository\nCustomQueryAsync / ExecuteAsync"]
        end
        subgraph WRAP["Wrapper"]
            WC["PewoDataServiceClient\nIPewoDataServiceClient\nHTTP calls to :7141"]
        end
    end

    subgraph WEBAPI["WIS_WebApp_RestAPI :7200"]
        direction TB
        subgraph CTRLW["API Layer"]
            PC["PewoController\nPOST worker/run\nPOST event-close\nPOST runs/retry"]
            FILTER["PewoApiKeyFilter\nX-Api-Key validation\nKey Vault lookup"]
        end
        subgraph DOMAIN["Domain Layer"]
            WS["PewoWorkerService\nJob loop orchestrator\nResume-not-restart\nRetry + backoff\nFire and forget"]
            SS["PewoStepService\nTOTALS_CHECK\nREAD_BLOB_ZIP\nSFTP\nARCHIVE\nEMAIL"]
            JDS["PewoJobDataService\nAdapter — wraps wrapper client"]
            LS["PewoLogService\nWraps InsertLog — swallows exceptions"]
        end
        subgraph EVENTINT["Event Integration"]
            ES["EventService.CloseInventory\nExisting method — PEWO hook added\nFix10 EventGuid guard\nFix17 CancellationToken guard"]
        end
    end

    subgraph INFRA["Azure Infrastructure"]
        CAJ[["Container App Job\ncron */10 * * * *\nparallelism=1\ncurlimages/curl"]]
        KV[("Key Vault\nPewoApiKey\nSFTPServerSSHPrivateKey\nSendGridEmailKey\nBlobConnectionString")]
        BLOB1["Blob: output-files\n/{eventGuid}/\nGM + PRPC source txt files"]
        BLOB2["Blob: flexcount-save\nZips staged\nOriginals archived"]
        SFTP["TARGET SFTP\n/www.data/target-ssh/nexgen"]
        SG["SendGrid\nEmail notifications"]
    end

    subgraph DB["SQL Server"]
        SQLDB[("sqldb-flexcount-dev4-ue\nAll PEWO tables")]
    end

    CAJ -->|"POST /api/Pewo/worker/run\nX-Api-Key header"| FILTER
    FILTER --> PC
    PC --> WS
    ES -->|"PEWO hook on event close"| JDS
    WS --> SS
    WS --> JDS
    WS --> LS
    JDS --> WC
    LS --> WC
    WC -->|"HTTP REST"| DC
    DC --> MED
    MED --> H1 & H2 & H3 & H4 & H5 & H6 & H7 & H8
    H1 & H2 & H3 & H4 & H5 & H6 & H7 & H8 --> DAP
    DAP <-->|"Dapper SP calls"| SQLDB
    SQLDB --> T1 & T2 & T3 & T4 & T5 & T6 & T7
    SP1 & SP2 & SP3 & SP4 & SP5 & SP6 & SP7 & SP8 & SP9 & SP10 <--> SQLDB
    KV -.->|"secrets at runtime"| CAJ
    KV -.->|"secrets at runtime"| SS
    SS <-->|"BlobContainerClient"| BLOB1
    SS <-->|"BlobContainerClient"| BLOB2
    SS -->|"IFtpHelper SSH key"| SFTP
    SS -->|"SendGridClient"| SG

    classDef dacpac fill:#2980B9,stroke:#1F618D,color:#fff
    classDef dbsvc fill:#8E44AD,stroke:#6C3483,color:#fff
    classDef webapi fill:#27AE60,stroke:#1E8449,color:#fff
    classDef infra fill:#E67E22,stroke:#CA6F1E,color:#fff
    classDef db fill:#2C3E50,stroke:#1A252F,color:#fff

    class T1,T2,T3,T4,T5,T6,T7,T8,T9,SP1,SP2,SP3,SP4,SP5,SP6,SP7,SP8,SP9,SP10 dacpac
    class DC,MED,H1,H2,H3,H4,H5,H6,H7,H8,DAP,WC dbsvc
    class PC,FILTER,WS,SS,JDS,LS,ES webapi
    class CAJ,KV,BLOB1,BLOB2,SFTP,SG infra
    class SQLDB db
```

---

## 2. Data Model — Entity Relationships

```mermaid
erDiagram
    Customer {
        int id_Customer PK
        nvarchar Name
        bit Has_Post_Event_Workflow
        bit is_Deleted
    }

    Pewo_CustomerWorkflowType {
        int id_CustomerWorkflowType PK
        int id_Customer FK
        varchar WorkflowType_Code
        nvarchar WorkflowType_Name
        varchar Fan_Out_Source_WorkflowType_Code
        smallint Max_Retries
        int Fan_Out_Lookback_Hours
        bit is_Active
    }

    Pewo_WorkflowStepDef {
        int id_WorkflowStepDef PK
        int id_CustomerWorkflowType FK
        varchar Step_Kind
        nvarchar Step_Name
        smallint Step_Order
        smallint Max_Attempts
        int Backoff_Seconds
        nvarchar Config
    }

    Pewo_Schedule {
        int id_Schedule PK
        int id_CustomerWorkflowType FK
        nvarchar Schedule_Name
        varchar Cron_Expression
        varchar Timezone
        datetime Next_Run_At
        varchar Status
        bit is_Enabled
        int Last_Run_Id
    }

    Pewo_WorkflowRun {
        int id_WorkflowRun PK
        int id_Schedule FK
        int id_CustomerWorkflowType FK
        varchar Status
        smallint Max_Retries
        smallint Retry_Count
        datetime Retry_At
        nvarchar Batch_Key
        datetime Started_At
        datetime Finished_At
        nvarchar Reason
    }

    Pewo_WorkflowRunEvent {
        int id_WorkflowRunEvent PK
        int id_WorkflowRun FK
        int id_Event
        int id_Customer
        int id_Store
        nvarchar Store_No
        nvarchar Store_Name
        uniqueidentifier Event_Guid
        nvarchar Event_Status
        datetime Event_Date
        datetime Event_Scheduled_Date
        nvarchar Metadata_Json
    }

    Pewo_WorkflowStepRun {
        int id_WorkflowStepRun PK
        int id_WorkflowRun FK
        int id_WorkflowStepDef FK
        varchar Step_Kind
        varchar Status
        smallint Attempts
        nvarchar Artifact_Ref
        nvarchar Failure_Details
        datetime Start_Time
        datetime End_Time
    }

    Pewo_WorkflowRunLog {
        int id_WorkflowRunLog PK
        int id_WorkflowRun FK
        int id_Customer
        varchar Step_Kind
        varchar Log_Level
        nvarchar Message
        nvarchar Event_Context
        datetime logged_date
    }

    Customer ||--o{ Pewo_CustomerWorkflowType : "id_Customer"
    Pewo_CustomerWorkflowType ||--o{ Pewo_WorkflowStepDef : "id_CustomerWorkflowType"
    Pewo_CustomerWorkflowType ||--o{ Pewo_Schedule : "id_CustomerWorkflowType"
    Pewo_Schedule ||--o{ Pewo_WorkflowRun : "id_Schedule"
    Pewo_WorkflowRun ||--o{ Pewo_WorkflowRunEvent : "id_WorkflowRun"
    Pewo_WorkflowRun ||--o{ Pewo_WorkflowStepRun : "id_WorkflowRun"
    Pewo_WorkflowRun ||--o{ Pewo_WorkflowRunLog : "id_WorkflowRun"
    Pewo_WorkflowStepDef ||--o{ Pewo_WorkflowStepRun : "id_WorkflowStepDef"
```

---

## 3. Day 1 — Event Close Sequence

```mermaid
sequenceDiagram
    actor UI as UI / Browser
    participant ES as EventService.cs\nWIS_WebApp_RestAPI
    participant JDS as PewoJobDataService\nWIS_WebApp_RestAPI
    participant WC as PewoDataServiceClient\nWrapper
    participant DC as PewoDataController\n:7141
    participant SP as usp_Pewo_CreateRunOnEventClose\nSQL Server

    rect rgb(39, 174, 96)
        Note over UI,SP: DAY 1 — Inventory Event Closes

        UI->>ES: HTTP PUT /api/Event/close (eventId)
        ES->>ES: GetEventCondition, GetFileMetadata
        ES->>ES: SendEmailReports, SendEmailOutputs
        ES->>ES: _iEventServiceClient.Update\n(Status = Closed, ScheduledCloseTime = now)

        Note over ES: Fix 10: EventGuid null guard
        Note over ES: Fix 17: CancellationToken guard

        ES->>JDS: CreateRunOnEventCloseAsync\n(idCustomer, idEvent, storeNo,\nstoreName, eventDate, eventGuid,\nidStore, eventStatus)

        JDS->>WC: HTTP POST /api/PewoData/event-close
        WC->>DC: CreateRunOnEventClose request
        DC->>SP: EXEC usp_Pewo_CreateRunOnEventClose

        Note over SP: JOIN Customer WHERE Has_Post_Event_Workflow=1\nONLY primes PEWO-enabled customers

        SP->>SP: FOR EACH ON_EVENT_CLOSE workflow type\nfor this customer:
        SP->>SP: UPDLOCK + HOLDLOCK — concurrency safe
        SP->>SP: NOT EXISTS check\n(id_Event OR Event_Guid — any status)
        SP->>SP: INSERT Pewo_WorkflowRun\n(Status=PENDING, Max_Retries=3)
        SP->>SP: INSERT Pewo_WorkflowRunEvent\n(EventGuid, StoreNo, idEvent)
        SP->>SP: UPDATE Pewo_Schedule\nSET Next_Run_At = GETUTCDATE()

        SP-->>DC: id_WorkflowRun created
        DC-->>WC: PewoEventCloseResponse
        WC-->>JDS: Response
        JDS-->>ES: Runs created
        ES-->>UI: Event close response (PEWO never blocks)
    end
```

---

## 4. Day 1 — Worker Processes Totals Check

```mermaid
sequenceDiagram
    participant CAJ as Container App Job\nAzure
    participant PC as PewoController\n:7200
    participant WS as PewoWorkerService
    participant SS as PewoStepService
    participant JDS as PewoJobDataService
    participant DB as SQL Server via\nDB Services :7141

    rect rgb(52, 152, 219)
        Note over CAJ,DB: DAY 1 — Within 10 Minutes of Event Close

        CAJ->>PC: POST /api/Pewo/worker/run\nX-Api-Key: ***
        PC->>PC: PewoApiKeyFilter validates key\nagainst Key Vault
        PC-->>CAJ: 202 Accepted immediately
        PC->>WS: Task.Run — fire and forget\n(CancellationToken.None)

        Note over WS: _isRunning guard prevents\nduplicate execution

        WS->>JDS: GetDueJobsAsync
        JDS->>DB: usp_Pewo_GetDueJobs\nFan-out INSERT runs\nSELECT SCHEDULE source
        DB-->>JDS: DueJobDto\nWorkflowType=GM_TOTALS_CHECK\nJob_Source=SCHEDULE
        JDS-->>WS: List of due jobs

        Note over WS: Job_Source=SCHEDULE → CreateWorkflowRunAsync
        WS->>JDS: CreateWorkflowRunAsync
        JDS->>DB: usp_Pewo_CreateWorkflowRun
        DB-->>JDS: id_WorkflowRun = 4
        JDS-->>WS: runId = 4

        WS->>JDS: GetRunResumeAsync (runId=4)
        JDS->>DB: usp_Pewo_GetRunResume
        DB-->>JDS: Steps ordered by Step_Order\nStep1: TOTALS_CHECK PENDING\nStep2: EMAIL PENDING
        JDS-->>WS: Steps list

        WS->>JDS: GetWorkflowRunEventsAsync
        JDS->>DB: usp_Pewo_GetWorkflowRunEvents
        DB-->>JDS: EventGuid, StoreNo, EventDate
        JDS-->>WS: Event metadata

        rect rgb(39, 174, 96)
            Note over WS,SS: Step 1 — TOTALS_CHECK
            WS->>SS: TotalsCheck(request)\nArtifact_Ref=null
            SS->>SS: ITotalsValidationService\n.ValidateNgen(EventGuid)
            SS->>SS: Reads output-files/{eventGuid}/\nValidates GM file headers
            SS-->>WS: Success\nArtifact_Ref={"totalQty":5000}

            WS->>JDS: UpsertStepRunAsync\nStatus=COMPLETED\nArtifact_Ref={"totalQty":5000}
            JDS->>DB: usp_Pewo_UpsertStepRun
            WS->>JDS: LogAsync (Step TOTALS_CHECK COMPLETED)
            JDS->>DB: usp_Pewo_InsertLog
        end

        rect rgb(142, 68, 173)
            Note over WS,SS: Step 2 — EMAIL
            WS->>SS: EmailAsync(request)\nConfig={"recipients":"ops@wisintl.com"}
            SS->>SS: Build notification HTML body\nRunId, StoreNo, EventGuid, UTC time
            SS->>SS: SendGridClient.SendEmailAsync
            SS-->>WS: Success\nArtifact_Ref=notified:2026-07-13T...

            WS->>JDS: UpsertStepRunAsync\nStatus=COMPLETED
            JDS->>DB: usp_Pewo_UpsertStepRun
        end

        Note over WS: All steps COMPLETED
        WS->>JDS: SetRunTerminalStatusAsync\nStatus=COMPLETED
        JDS->>DB: usp_Pewo_SetRunTerminalStatus\nFinished_At=GETUTCDATE()

        Note over WS: Fix 9: ON_EVENT_CLOSE cron\nAdvanceSchedule only on success
        WS->>JDS: AdvanceScheduleAsync\nNext_Run_At = NOW + 50 years
        JDS->>DB: usp_Pewo_AdvanceSchedule
    end
```

---

## 5. Day 2 — Fan-Out and NGen Delivery

```mermaid
sequenceDiagram
    participant CAJ as Container App Job\n8AM UTC
    participant WS as PewoWorkerService
    participant SS as PewoStepService
    participant JDS as PewoJobDataService
    participant DB as SQL Server
    participant BLOB as Azure Blob Storage
    participant SFTP as TARGET SFTP

    rect rgb(142, 68, 173)
        Note over CAJ,SFTP: DAY 2 — 8AM UTC (or forced via Next_Run_At update in dev)

        CAJ->>WS: POST /worker/run → RunAsync fires

        WS->>JDS: GetDueJobsAsync
        JDS->>DB: usp_Pewo_GetDueJobs

        Note over DB: FAN-OUT BLOCK (atomic transaction):
        DB->>DB: Find COMPLETED GM_TOTALS_CHECK\nwithin Fan_Out_Lookback_Hours=24
        DB->>DB: NOT EXISTS Batch_Key=today guard\nprevents repeated fan-out
        DB->>DB: INSERT Pewo_WorkflowRun\n(GM_PRC_DELIVERY PENDING Batch_Key=today)
        DB->>DB: INSERT Pewo_WorkflowRunEvent\n(copied from parent event)

        DB-->>JDS: DueJobDto\nWorkflowType=GM_PRC_DELIVERY\nJob_Source=NEW_CHILD\nid_WorkflowRun=5
        JDS-->>WS: Job list

        Note over WS: Job_Source=NEW_CHILD → reuse id_WorkflowRun=5

        WS->>JDS: GetRunResumeAsync (runId=5)
        JDS->>DB: usp_Pewo_GetRunResume
        DB-->>JDS: Step1: READ_BLOB_ZIP\nStep2: SFTP\nStep3: ARCHIVE\nStep4: EMAIL

        WS->>JDS: GetWorkflowRunEventsAsync
        DB-->>JDS: EventGuid, StoreNo

        rect rgb(52, 152, 219)
            Note over WS,BLOB: Step 1 — READ_BLOB_ZIP
            WS->>SS: ReadBlobZipAsync\n(Artifact_Ref=null)
            SS->>BLOB: List output-files/{eventGuid}/\nFind TAR_ITM_GM_*.txt\nFind TAR_ITM_PRPC_*.txt
            BLOB-->>SS: File list
            SS->>BLOB: DownloadFileBlob (GM txt)
            SS->>SS: ZipArchive in memory\nCompressionLevel.Optimal
            SS->>BLOB: Upload TAR_ITM_GM_0421.zip\nto flexcount-save/
            SS->>BLOB: DownloadFileBlob (PRPC txt)
            SS->>SS: ZipArchive in memory
            SS->>BLOB: Upload TAR_ITM_PRPC_0421.zip\nto flexcount-save/
            SS-->>WS: Success\nArtifact_Ref=staged:TAR_ITM_GM.zip,TAR_ITM_PRPC.zip

            WS->>JDS: UpsertStepRunAsync COMPLETED\nArtifact_Ref=staged:...
            JDS->>DB: usp_Pewo_UpsertStepRun
        end

        rect rgb(231, 76, 60)
            Note over WS,SFTP: Step 2 — SFTP
            WS->>SS: SftpAsync\n(Artifact_Ref=staged:TAR_ITM_GM.zip,TAR_ITM_PRPC.zip)
            SS->>SS: CleanupStalePewoTempFiles()
            SS->>SS: Parse zip names from staged: prefix
            loop For each zip file
                SS->>BLOB: DownloadFileBlob from flexcount-save/
                SS->>SS: Write to temp file\npewo_{runId}_{zipName}_{timestamp}
                SS->>SFTP: IFtpHelper.UploadFile\nSSH key auth\n/www.data/target-ssh/nexgen
                SS->>JDS: UpsertStepRunAsync PENDING\nsftp:partial:{delivered so far}
                SS->>SS: Delete temp file (finally block)
            end
            SS-->>WS: Success\nArtifact_Ref=sftp:delivered:/www.data/...:both files

            WS->>JDS: UpsertStepRunAsync COMPLETED
            JDS->>DB: usp_Pewo_UpsertStepRun
        end

        rect rgb(39, 174, 96)
            Note over WS,BLOB: Step 3 — ARCHIVE
            WS->>SS: ArchiveAsync\n(Artifact_Ref=sftp:delivered:...)
            SS->>BLOB: List output-files/{eventGuid}/
            SS->>BLOB: DownloadFileBlob TAR_ITM_GM.txt
            SS->>BLOB: Upload to flexcount-save/{eventGuid}/TAR_ITM_GM.txt
            SS->>BLOB: DownloadFileBlob TAR_ITM_PRPC.txt
            SS->>BLOB: Upload to flexcount-save/{eventGuid}/TAR_ITM_PRPC.txt
            SS-->>WS: Success\nArtifact_Ref=archived:2:{eventGuid}

            WS->>JDS: UpsertStepRunAsync COMPLETED
            JDS->>DB: usp_Pewo_UpsertStepRun
        end

        rect rgb(142, 68, 173)
            Note over WS,SS: Step 4 — EMAIL
            WS->>SS: EmailAsync\n(Config={"recipients":"ops@wisintl.com"})
            SS->>SS: Build delivery confirmation HTML\nStoreNo, EventGuid, DeliveredAt UTC
            SS->>SS: SendGridClient.SendEmailAsync
            SS-->>WS: Success\nArtifact_Ref=notified:2026-07-14T08:05:00Z

            WS->>JDS: UpsertStepRunAsync COMPLETED
            JDS->>DB: usp_Pewo_UpsertStepRun
        end

        Note over WS: All 4 steps COMPLETED
        WS->>JDS: SetRunTerminalStatusAsync COMPLETED
        JDS->>DB: usp_Pewo_SetRunTerminalStatus\nFinished_At=GETUTCDATE()

        WS->>JDS: AdvanceScheduleAsync\nNext_Run_At = tomorrow 8AM
        JDS->>DB: usp_Pewo_AdvanceSchedule
    end
```

---

## 6. Retry Flow

```mermaid
flowchart TD
    START([Worker Tick]) --> GDJ[GetDueJobsAsync\nusp_Pewo_GetDueJobs]

    GDJ --> SOURCES{Job Source?}

    SOURCES -->|SCHEDULE| S1[Create new WorkflowRun\nusp_Pewo_CreateWorkflowRun]
    SOURCES -->|SAFETY_NET| S1
    SOURCES -->|RETRY| S2[Reuse existing\nid_WorkflowRun]
    SOURCES -->|NEW_CHILD| S2
    SOURCES -->|PENDING_EVENT| S2

    S1 & S2 --> RESUME[GetRunResumeAsync\nLoad steps ordered by Step_Order]
    RESUME --> EVENTS[GetWorkflowRunEventsAsync\nLoad EventGuid StoreNo]

    EVENTS --> LOOP{Next step\nin order}

    LOOP -->|Status=COMPLETED| SKIP[Skip — resume not restart\nlastCompletedArtifactRef tracked]
    SKIP --> LOOP

    LOOP -->|Status=PENDING or null| EXEC[Execute Step\nInject prior Artifact_Ref\nif step own is null]

    EXEC --> RESULT{Success?}

    RESULT -->|YES| UPSERT_OK[UpsertStepRunAsync\nStatus=COMPLETED\nArtifact_Ref saved]
    RESULT -->|NO| UPSERT_FAIL[UpsertStepRunAsync\nStatus=FAILED\nFailure_Details saved]

    UPSERT_OK --> LOOP

    UPSERT_FAIL --> TERMINAL_FAIL[SetRunTerminalStatusAsync\nStatus=FAILED\nRetry_At = now + 2^n min]

    TERMINAL_FAIL --> MAX{Retry_Count\n>= Max_Retries?}
    MAX -->|NO| ADVANCE_CRON{ON_EVENT_CLOSE?}
    ADVANCE_CRON -->|YES| SKIP_ADVANCE[Skip AdvanceSchedule\nFix 9 — shared schedule\nnot advanced on failure]
    ADVANCE_CRON -->|NO| ADVANCE[AdvanceScheduleAsync\nNext_Run_At = next cron]

    MAX -->|YES| DEAD[Dead Letter Email\nIPewoStepService.EmailAsync\nSame recipients from EMAIL step config]
    DEAD --> ADVANCE

    LOOP -->|All COMPLETED| TERMINAL_OK[SetRunTerminalStatusAsync\nStatus=COMPLETED\nFinished_At=GETUTCDATE]
    TERMINAL_OK --> ADVANCE_OK[AdvanceScheduleAsync\nON_EVENT_CLOSE → 50 years\nCron → next occurrence]

    SKIP_ADVANCE --> DONE([Done])
    ADVANCE --> DONE
    ADVANCE_OK --> DONE

    classDef success fill:#27AE60,stroke:#1E8449,color:#fff
    classDef failure fill:#C0392B,stroke:#922B21,color:#fff
    classDef decision fill:#E67E22,stroke:#CA6F1E,color:#fff
    classDef skip fill:#7F8C8D,stroke:#566573,color:#fff

    class UPSERT_OK,TERMINAL_OK,ADVANCE_OK success
    class UPSERT_FAIL,TERMINAL_FAIL,DEAD failure
    class SOURCES,RESULT,MAX,ADVANCE_CRON,LOOP decision
    class SKIP,SKIP_ADVANCE skip
```

---

## 7. Behavior Documentation

### What PEWO Does

PEWO (Post-Event Workflow Orchestrator) automates the NGen/PRPC file delivery process for the TARGET customer. It replaces manual SRE Unix scripts with a fully automated, resumable, auditable workflow system.

### Two Workflows

**GM_TOTALS_CHECK** (Day 1 — fires when event closes)
- Triggered: When inventory event closes via `EventService.CloseInventory`
- Steps: TOTALS_CHECK → EMAIL
- Purpose: Validate GM file headers and totals. Notify ops if passed.

**GM_PRC_DELIVERY** (Day 2 — fires at 8AM UTC)
- Triggered: Fan-out from completed GM_TOTALS_CHECK runs
- Steps: READ_BLOB_ZIP → SFTP → ARCHIVE → EMAIL
- Purpose: Zip source files, deliver to TARGET SFTP, archive originals, notify ops.

### Key Design Principles

**Resume-Not-Restart**
Every step writes its result to `Pewo_WorkflowStepRun` immediately after execution. On retry the worker loads all steps, sees which are already COMPLETED, and skips them. If SFTP of file 2 fails after file 1 succeeded — only file 2 is retried.

**Data-Driven Configuration**
Workflows, steps, schedules, recipients, container names, SFTP paths — all in seed data. No code changes needed to add a step, change a recipient, or onboard a new customer.

**Has_Post_Event_Workflow Guard**
`usp_Pewo_CreateRunOnEventClose` JOINs `Customer` table and only primes workflows for customers where `Has_Post_Event_Workflow = 1`. New customers are never accidentally enrolled.

**ON_EVENT_CLOSE Sentinel**
`GM_TOTALS_CHECK` schedule uses `Cron_Expression = 'ON_EVENT_CLOSE'` — not a real cron. `Next_Run_At` is set to NOW by the SP on event close, and reset to 50 years when the run completes. Worker knows not to advance the schedule on failure for event-driven workflows.

**Fan-Out**
`GM_PRC_DELIVERY.Fan_Out_Source_WorkflowType_Code = 'GM_TOTALS_CHECK'`. At 8AM `usp_Pewo_GetDueJobs` finds all COMPLETED GM_TOTALS_CHECK runs within 24 hours, creates one GM_PRC_DELIVERY child run per event atomically in a transaction. Each store is fully independent — one store failing does not block others.

**Simultaneous Event Closes**
Multiple stores closing at the same time each create their own `WorkflowRun` and `WorkflowRunEvent`. The PENDING_EVENT source in `usp_Pewo_GetDueJobs` catches any run orphaned because a sibling completed first and pushed the shared schedule to 50 years. Guard: `NOT EXISTS ON Pewo_WorkflowStepRun` prevents in-progress runs being picked up again.

**Fire and Forget**
`POST /api/Pewo/worker/run` returns 202 immediately. Worker runs in a background `Task.Run` with `CancellationToken.None` — not tied to HTTP request lifetime. `_isRunning` static guard prevents duplicate execution if CAJ fires again before previous tick completes.

**Logging Never Crashes the Worker**
`PewoLogService.LogAsync` wraps all DB calls in try/catch. A logging failure is written to the application log but never propagates to the worker. Steps and run status always complete correctly regardless of log failures.

**SFTP Partial Delivery**
After each zip is successfully delivered, progress is saved to `Pewo_WorkflowStepRun.Artifact_Ref` as `sftp:partial:{delivered files}`. On retry, already-delivered zips are skipped. No duplicate SFTP delivery.

**Dead Letter**
When a run exhausts all retries permanently, `PewoWorkerService` reads the EMAIL step's Config JSON, overrides the subject with a failure message, and calls `IPewoStepService.EmailAsync`. Same recipients as the normal completion email. No new configuration needed.

### Schedule States

| Workflow | Normal State | After Event Close | After Run Completes |
|---|---|---|---|
| GM_TOTALS_CHECK | Next_Run_At = 50 years | Next_Run_At = NOW | Next_Run_At = 50 years |
| GM_PRC_DELIVERY | Next_Run_At = tomorrow 8AM | Unchanged | Next_Run_At = next day 8AM |

### Artifact_Ref by Step

| Step | Artifact_Ref Value | Purpose |
|---|---|---|
| TOTALS_CHECK | `{"totalQty":5000,"totalExt":25000}` | Audit of validated totals |
| READ_BLOB_ZIP | `staged:file1.zip,file2.zip` | Tells SFTP which zips to deliver |
| SFTP | `sftp:delivered:/path:file1.zip,file2.zip` | Idempotency — skip if already delivered |
| ARCHIVE | `archived:2:{eventGuid}` | Confirms count of archived files |
| EMAIL | `notified:2026-07-13T08:05:00Z` | Idempotency — never send twice |

### Three Repos — What Each Owns

| Repo | Owns | Deployed Via |
|---|---|---|
| WIS_Database | Table schemas, SPs, indexes, constraints | DACPAC — declarative diff deployment |
| flexcount-database-services | HTTP API wrapping SP calls via Dapper | Container App |
| WIS_WebApp_RestAPI | Orchestration, step logic, blob, SFTP, email | Container App |

### Local Debug Order

```
1. Run seed scripts (once): StepKind → GM_TOTALS_CHECK → GM_PRC_DELIVERY
2. SSMS: EXEC usp_Pewo_CreateRunOnEventClose (simulate event close)
3. Postman: POST /api/Pewo/worker/run (Day 1 — totals check + email)
4. SSMS: UPDATE Pewo_Schedule SET Next_Run_At = DATEADD(MINUTE,-1,GETUTCDATE())
         WHERE WorkflowType_Code = 'GM_PRC_DELIVERY'
5. Postman: POST /api/Pewo/worker/run (Day 2 — delivery)
6. Verify: SELECT * FROM Pewo_WorkflowRun / Pewo_WorkflowStepRun / Pewo_WorkflowRunLog
7. Cleanup: Delete runs cascade, reset schedules, repeat from step 2
```

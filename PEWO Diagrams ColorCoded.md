---
title: PEWO System Diagrams — Color Coded
---

## Diagram 1 — High Level Components (Color Coded)

```mermaid
graph TB
    subgraph UI["🖥️ UI / Application Layer"]
        A([Inventory Event Closes])
    end

    subgraph AZ["☁️ Azure Infrastructure"]
        CAJ[["⏰ Container App Job\ncron every 10 min\nparallelism = 1"]]
        KV[("🔐 Key Vault\nPewoApiKey\nSFTP SSH Key\nSendGrid Key\nBlob Connection")]
    end

    subgraph WEB["🌐 WIS_WebApp_RestAPI\nlocalhost:7200"]
        EC["POST /event-close"]
        WR["POST /worker/run"]
        FILTER["🛡️ PewoApiKeyFilter"]
        WS["PewoWorkerService"]
        SS["PewoStepService"]
    end

    subgraph DBS["🗄️ flexcount-database-services\nlocalhost:7141"]
        CTRL["PewoDataController"]
        SVC["PewoDataService"]
        HAND["MediatR Handlers × 12"]
        DB[("SQL Server\nPEWO Tables")]
    end

    subgraph BLOB["📦 Azure Blob Storage"]
        OUT["output-files\n/{eventGuid}/"]
        SAVE["flexcount-save\nzips + originals"]
    end

    subgraph EXT["📡 External Systems"]
        SFTP["TARGET SFTP\n/www.data/target-ssh/nexgen"]
        EMAIL["SendGrid\nEmail Recipients"]
    end

    A -->|"5-line hook"| EC
    CAJ -->|"X-Api-Key header"| FILTER
    FILTER --> WR
    KV -.->|"secrets"| CAJ
    KV -.->|"secrets"| SS
    EC --> WS
    WR --> WS
    WS --> SS
    WS <-->|"HTTP REST\nwrapper client"| CTRL
    CTRL --> SVC
    SVC --> HAND
    HAND <--> DB
    SS -->|"DownloadFileBlob\nUploadFileAsync"| OUT
    SS -->|"upload zips"| SAVE
    SS -->|"UploadFile SSH key"| SFTP
    SS -->|"SendGridClient"| EMAIL

    classDef ui fill:#4A90D9,stroke:#2C5F8A,color:#fff,rx:8
    classDef azure fill:#0078D4,stroke:#005A9E,color:#fff
    classDef webapi fill:#27AE60,stroke:#1E8449,color:#fff
    classDef dbsvc fill:#8E44AD,stroke:#6C3483,color:#fff
    classDef blob fill:#E67E22,stroke:#CA6F1E,color:#fff
    classDef ext fill:#C0392B,stroke:#922B21,color:#fff
    classDef db fill:#2C3E50,stroke:#1A252F,color:#fff

    class A ui
    class CAJ,KV azure
    class EC,WR,FILTER,WS,SS webapi
    class CTRL,SVC,HAND dbsvc
    class DB db
    class OUT,SAVE blob
    class SFTP,EMAIL ext
```

---

## Diagram 2 — Schedule Flow (Color Coded)

```mermaid
sequenceDiagram
    actor UI as 🖥️ UI / EventService
    participant API as 🌐 Web REST API
    participant SP as 🗄️ usp_Pewo_CreateRunOnEventClose
    participant SCH as 📅 Pewo_Schedule
    participant CAJ as ⏰ Container App Job
    participant GDJ as 🗄️ usp_Pewo_GetDueJobs

    rect rgb(39, 174, 96)
        Note over UI,SCH: DAY 1 — Event Closes
        UI->>API: POST /api/Pewo/event-close
        API->>SP: Execute SP
        SP->>SCH: INSERT WorkflowRun PENDING
        SP->>SCH: INSERT WorkflowRunEvent Event_Guid Store_No
        SP->>SCH: UPDATE Schedule Next_Run_At = NOW
        SP-->>API: id_WorkflowRun
        API-->>UI: PewoEventCloseResponse
    end

    rect rgb(52, 152, 219)
        Note over CAJ,GDJ: Within 10 min — CAJ fires Day 1
        CAJ->>API: POST /worker/run X-Api-Key
        API->>GDJ: usp_Pewo_GetDueJobs
        GDJ-->>API: GM_TOTALS_CHECK SCHEDULE source
        Note over API: TOTALS_CHECK → EMAIL
        API->>SCH: Status=COMPLETED
        API->>SCH: Next_Run_At = 50 years ON_EVENT_CLOSE sentinel
    end

    rect rgb(142, 68, 173)
        Note over CAJ,GDJ: Next Morning 8AM — CAJ fires Day 2
        CAJ->>API: POST /worker/run X-Api-Key
        API->>GDJ: usp_Pewo_GetDueJobs
        Note over GDJ: Fan-out: finds COMPLETED GM_TOTALS_CHECK within 24h
        GDJ->>SCH: INSERT WorkflowRun GM_PRC_DELIVERY NEW_CHILD
        GDJ->>SCH: INSERT WorkflowRunEvent copied from parent
        GDJ-->>API: GM_PRC_DELIVERY NEW_CHILD source
        Note over API: READ_BLOB_ZIP → SFTP → ARCHIVE
        API->>SCH: Status=COMPLETED
        API->>SCH: Next_Run_At = tomorrow 8AM
    end
```

---

## Diagram 3 — Step Execution Flow (Color Coded)

```mermaid
flowchart TD
    START([🚀 Worker picks up due job]):::start --> LOAD[📋 Load steps\nusp_Pewo_GetRunResume\nOrdered ASC by Step_Order]:::db

    LOAD --> LOOP{Next step?}:::decision

    LOOP -->|COMPLETED| SKIP[⏭️ Skip\nresume-not-restart]:::skip
    SKIP --> LOOP

    LOOP -->|PENDING| DISPATCH{Step_Kind?}:::decision

    DISPATCH -->|TOTALS_CHECK| TC[🔍 ValidateNgen\noutput-files/eventGuid\nITotalsValidationService]:::day1
    DISPATCH -->|EMAIL Day1| EM[📧 SendGrid\nper-event notification\nfrom Config JSON]:::day1
    DISPATCH -->|READ_BLOB_ZIP| RBZ[📦 BlobContainerClient\nlist output-files/eventGuid\nzip each file individually\nupload to flexcount-save]:::day2
    DISPATCH -->|SFTP| SFTP[📡 IFtpHelper.UploadFile\nSSH key auth\n/www.data/target-ssh/nexgen]:::day2
    DISPATCH -->|ARCHIVE| ARCH[🗄️ Copy originals\noutput-files → flexcount-save\nNO deletion]:::day2
    DISPATCH -->|EMAIL_SUMMARY| EMS[📊 GetBatchRunStatus\nwait gate via retry\nHTML table email]:::summary
    DISPATCH -->|GET_EVENTS| GE[🔎 MEO stub\nblob discovery]:::stub
    DISPATCH -->|TRANSFORM| TR[⚙️ MEO stub\nEBCDIC/ASCII\npending]:::stub

    TC & EM & RBZ & SFTP & ARCH & EMS & GE & TR --> RESULT{Success?}:::decision

    RESULT -->|YES| OK[✅ UpsertStepRun COMPLETED\nArtifact_Ref saved\nInsertLog INFO]:::success
    RESULT -->|NO| FAIL[❌ UpsertStepRun FAILED\nFailure_Details saved\nInsertLog ERROR]:::failure

    OK --> LOOP
    FAIL --> TERMINAL_FAIL[🔴 SetRunTerminalStatus FAILED\nRetry_At = now + 2^n min\nAdvanceSchedule]:::failure

    LOOP -->|All done| TERMINAL_OK[🟢 SetRunTerminalStatus COMPLETED\nAdvanceSchedule Next_Run_At\nInsertLog run complete]:::success

    TERMINAL_OK & TERMINAL_FAIL --> DONE([🏁 Done]):::start

    classDef start fill:#2C3E50,stroke:#1A252F,color:#fff,rx:20
    classDef db fill:#8E44AD,stroke:#6C3483,color:#fff
    classDef decision fill:#E67E22,stroke:#CA6F1E,color:#fff
    classDef skip fill:#7F8C8D,stroke:#566573,color:#fff
    classDef day1 fill:#27AE60,stroke:#1E8449,color:#fff
    classDef day2 fill:#2980B9,stroke:#1F618D,color:#fff
    classDef summary fill:#8E44AD,stroke:#6C3483,color:#fff
    classDef stub fill:#95A5A6,stroke:#717D7E,color:#fff
    classDef success fill:#27AE60,stroke:#1E8449,color:#fff
    classDef failure fill:#C0392B,stroke:#922B21,color:#fff
```

---

## Diagram 4 — Email Communication Flow (Color Coded)

```mermaid
sequenceDiagram
    participant WS as 🌐 PewoWorkerService
    participant SS as ⚙️ PewoStepService
    participant DB as 🗄️ GetBatchRunStatus
    participant SG as 📧 SendGrid
    participant OPS as 📬 ops-team@company.com

    rect rgb(39, 174, 96)
        Note over WS,OPS: DAY 1 — Per-Event Notification
        WS->>SS: EmailAsync step_Kind=EMAIL
        SS->>SS: Build body RunId Store_No Event_Guid Completed UTC
        SS->>SG: SendEmailAsync recipients from Config JSON
        SG-->>OPS: TOTALS CHECK PASSED Store T1234
        SS-->>WS: artifact_ref = notified:2026-01-01T08:00:05Z
    end

    rect rgb(142, 68, 173)
        Note over WS,OPS: DAY 2 — Batch Summary Wait Gate
        WS->>SS: EmailSummaryAsync step_Kind=EMAIL_SUMMARY
        SS->>DB: GetBatchRunStatusAsync\nbatchKey=today\nbatchWorkflowTypeCode=GM_PRC_DELIVERY

        alt 🟡 Batch still active PENDING or RUNNING
            DB-->>SS: rows with Status=PENDING
            SS-->>WS: return FAIL — not yet terminal
            Note over WS: Retry backoff fires\n2^n minutes wait\nworker retries automatically
        else 🟢 All runs COMPLETED or FAILED
            DB-->>SS: all rows terminal
            SS->>SS: Build HTML table\nStore Event Date Status Detail
            SS->>SG: SendEmailAsync consolidated summary
            SG-->>OPS: GM + PRPC Delivery Summary\nHTML table all stores
            SS-->>WS: artifact_ref = summary-sent:2026-01-01:3events
        end
    end

    rect rgb(44, 62, 80)
        Note over SS: Idempotency Guards
        Note over SS: notified: prefix — skip if already sent
        Note over SS: summary-sent: prefix — skip if already sent
    end
```

---

## Diagram 5 — Consolidated Architecture (Color Coded)

```mermaid
graph LR
    subgraph TRIGGER["⚡ Triggers"]
        direction TB
        UI(["🖥️ UI\nEvent Closes"])
        CAJ[["⏰ Container App Job\ncron */10 * * * *\nparallelism=1\ncurlimages/curl"]]
    end

    subgraph KV["🔐 Key Vault"]
        K1["PewoApiKey"]
        K2["SFTP SSH Key"]
        K3["SendGrid Key"]
        K4["Blob Connection"]
    end

    subgraph WEBAPI["🌐 WIS_WebApp_RestAPI  :7200"]
        direction TB
        subgraph CTRL_WEB["API Layer"]
            EC2["POST /event-close"]
            WR2["POST /worker/run\n🛡️ X-Api-Key protected"]
        end
        subgraph SVC_WEB["Domain Layer"]
            WS2["PewoWorkerService\nJob loop + retry\nCronos next run calc"]
            SS2["PewoStepService\nTOTALS_CHECK\nREAD_BLOB_ZIP\nSFTP\nARCHIVE\nEMAIL\nEMAIL_SUMMARY"]
            JS["PewoJobDataService\nAdapter"]
            LS["PewoLogService\nSwallows exceptions"]
        end
    end

    subgraph DBSVC["🗄️ flexcount-database-services  :7141"]
        direction TB
        subgraph CTRL_DB["API Layer"]
            DC["PewoDataController\n12 endpoints"]
        end
        subgraph APP_DB["Application Layer"]
            DS["PewoDataService\nMediatR dispatcher"]
            H1["GetDueJobsHandler"]
            H2["GetRunResumeHandler"]
            H3["UpsertStepRunHandler"]
            H4["SetRunTerminalStatusHandler"]
            H5["AdvanceScheduleHandler\n+ 7 more handlers"]
        end
        subgraph WRAP["Wrapper"]
            WC["PewoDataServiceClient\nHTTP calls to :7141"]
        end
    end

    subgraph DB["💾 SQL Server"]
        direction TB
        T1["Pewo_WorkflowRun"]
        T2["Pewo_WorkflowRunEvent"]
        T3["Pewo_WorkflowStepRun"]
        T4["Pewo_Schedule"]
        T5["Pewo_WorkflowRunLog"]
        T6["Pewo_CustomerWorkflowType\nPewo_WorkflowStepDef\nPewo_StepKind"]
    end

    subgraph EXT["📡 External"]
        BLOB1["output-files\n/{eventGuid}/\nGM + PRPC txt files"]
        BLOB2["flexcount-save\nzips + archived originals"]
        SFTP2["TARGET SFTP\n/www.data/target-ssh/nexgen"]
        SG2["SendGrid\nEmail"]
    end

    UI -->|"event-close hook"| EC2
    CAJ -->|"POST + X-Api-Key"| WR2
    KV -.->|"secrets at runtime"| CAJ
    KV -.->|"secrets at runtime"| SS2

    EC2 --> WS2
    WR2 --> WS2
    WS2 --> SS2
    WS2 --> JS
    WS2 --> LS
    JS --> WC
    LS --> WC
    WC -->|"HTTP REST"| DC
    DC --> DS
    DS --> H1 & H2 & H3 & H4 & H5
    H1 & H2 & H3 & H4 & H5 <-->|"Dapper + SP calls"| T1 & T2 & T3 & T4 & T5

    SS2 <-->|"BlobContainerClient\nDownloadFileBlob\nUploadFileAsync"| BLOB1
    SS2 <-->|"upload zips\narchive originals"| BLOB2
    SS2 -->|"IFtpHelper.UploadFile\nSSH key"| SFTP2
    SS2 -->|"SendGridClient"| SG2

    classDef trigger fill:#E67E22,stroke:#CA6F1E,color:#fff
    classDef kv fill:#2C3E50,stroke:#1A252F,color:#fff
    classDef webapi fill:#27AE60,stroke:#1E8449,color:#fff
    classDef dbsvc fill:#8E44AD,stroke:#6C3483,color:#fff
    classDef db fill:#2980B9,stroke:#1F618D,color:#fff
    classDef ext fill:#C0392B,stroke:#922B21,color:#fff

    class UI,CAJ trigger
    class K1,K2,K3,K4 kv
    class EC2,WR2,WS2,SS2,JS,LS webapi
    class DC,DS,H1,H2,H3,H4,H5,WC dbsvc
    class T1,T2,T3,T4,T5,T6 db
    class BLOB1,BLOB2,SFTP2,SG2 ext
```

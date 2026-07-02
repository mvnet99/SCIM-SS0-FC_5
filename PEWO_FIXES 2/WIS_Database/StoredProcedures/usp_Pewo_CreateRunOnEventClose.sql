-- =============================================
-- usp_Pewo_CreateRunOnEventClose
-- WIS_Database/dbo/StoredProcedures/usp_Pewo_CreateRunOnEventClose.sql
--
-- Changes from previous version:
--   Has_Post_Event_Workflow — JOIN to dbo.Customer ensures only customers
--   with Has_Post_Event_Workflow = 1 get workflow runs created.
--   Prevents accidental priming of new customers not yet onboarded to PEWO.
--   ON_EVENT_CLOSE sentinel — confirmed throughout.
--   UPDLOCK + HOLDLOCK — concurrency-safe duplicate check.
--   Dual dup check — id_Event AND Event_Guid (any status, not just PENDING).
-- CREATE OR ALTER — safe to rerun.
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[usp_Pewo_CreateRunOnEventClose]
    @id_Customer          INT,
    @id_Event             INT,
    @Store_No             NVARCHAR(20),
    @Store_Name           NVARCHAR(200),
    @Event_Date           DATETIME,
    @WorkflowType_Code    VARCHAR(50)      = NULL,
    @Event_Guid           UNIQUEIDENTIFIER = NULL,
    @id_Store             INT              = NULL,
    @Event_Status         NVARCHAR(25)     = NULL,
    @Event_Scheduled_Date DATETIME         = NULL
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #CreatedRuns (
        id_WorkflowRun          INT,
        id_CustomerWorkflowType INT,
        WorkflowType_Code       VARCHAR(50)
    );

    DECLARE @id_CustomerWorkflowType INT;
    DECLARE @Max_Retries             SMALLINT;
    DECLARE @id_Schedule             INT;
    DECLARE @Matched_Code            VARCHAR(50);
    DECLARE @id_WorkflowRun          INT;

    -- Cursor over all active ON_EVENT_CLOSE workflow types for this customer.
    -- Has_Post_Event_Workflow = 1 guard ensures only PEWO-enabled customers are primed.
    -- New customers without this flag set are silently skipped.
    DECLARE wf_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT wt.id_CustomerWorkflowType,
               wt.Max_Retries,
               s.id_Schedule,
               wt.WorkflowType_Code
        FROM   dbo.Pewo_CustomerWorkflowType wt
        JOIN   dbo.Pewo_Schedule             s  ON s.id_CustomerWorkflowType = wt.id_CustomerWorkflowType
        JOIN   dbo.Customer                  c  ON c.id_Customer = wt.id_Customer
                                                AND c.Has_Post_Event_Workflow = 1
                                                AND c.is_Deleted = 0
        WHERE  wt.id_Customer        = @id_Customer
        AND    wt.is_Active          = 1
        AND    s.is_Enabled          = 1
        AND    s.Status              = 'ACTIVE'
        AND    s.Cron_Expression     = 'ON_EVENT_CLOSE'
        AND    (@WorkflowType_Code IS NULL OR wt.WorkflowType_Code = @WorkflowType_Code);

    OPEN wf_cursor;
    FETCH NEXT FROM wf_cursor INTO @id_CustomerWorkflowType, @Max_Retries, @id_Schedule, @Matched_Code;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        BEGIN TRANSACTION;

        -- Concurrency-safe duplicate check:
        --   UPDLOCK  — blocks other sessions reading these rows until commit
        --   HOLDLOCK — holds the lock for the duration of the transaction
        -- Checks id_Event always AND Event_Guid when provided (any run status)
        -- NULL Event_Guid handled safely via id_Event fallback
        IF NOT EXISTS (
            SELECT 1
            FROM   dbo.Pewo_WorkflowRun      ex_wr WITH (UPDLOCK, HOLDLOCK)
            JOIN   dbo.Pewo_WorkflowRunEvent ex_wre ON ex_wre.id_WorkflowRun = ex_wr.id_WorkflowRun
            WHERE  ex_wr.id_CustomerWorkflowType = @id_CustomerWorkflowType
            AND    (
                ex_wre.id_Event = @id_Event
                OR (@Event_Guid IS NOT NULL AND ex_wre.Event_Guid = @Event_Guid)
            )
        )
        BEGIN
            INSERT INTO dbo.Pewo_WorkflowRun
                (id_Schedule, id_CustomerWorkflowType, Status, Max_Retries,
                 Retry_Count, created_date, last_updated_date)
            VALUES
                (@id_Schedule, @id_CustomerWorkflowType, 'PENDING', @Max_Retries,
                 0, GETUTCDATE(), GETUTCDATE());

            SET @id_WorkflowRun = SCOPE_IDENTITY();

            INSERT INTO dbo.Pewo_WorkflowRunEvent
                (id_WorkflowRun, id_Event, id_Customer, id_Store, Store_No, Store_Name,
                 Event_Guid, Event_Status, Event_Scheduled_Date, Event_Date, created_date)
            VALUES
                (@id_WorkflowRun, @id_Event, @id_Customer, @id_Store, @Store_No, @Store_Name,
                 @Event_Guid, @Event_Status, @Event_Scheduled_Date, @Event_Date, GETUTCDATE());

            UPDATE dbo.Pewo_Schedule
            SET    Next_Run_At       = GETUTCDATE(),
                   last_updated_date = GETUTCDATE()
            WHERE  id_Schedule = @id_Schedule;

            INSERT INTO #CreatedRuns VALUES (@id_WorkflowRun, @id_CustomerWorkflowType, @Matched_Code);
        END

        COMMIT TRANSACTION;

        FETCH NEXT FROM wf_cursor INTO @id_CustomerWorkflowType, @Max_Retries, @id_Schedule, @Matched_Code;
    END;

    CLOSE wf_cursor;
    DEALLOCATE wf_cursor;

    SELECT id_WorkflowRun, id_CustomerWorkflowType, WorkflowType_Code
    FROM   #CreatedRuns;

    DROP TABLE #CreatedRuns;
END;
GO

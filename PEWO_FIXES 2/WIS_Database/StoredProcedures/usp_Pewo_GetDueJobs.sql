-- =============================================
-- usp_Pewo_GetDueJobs
-- WIS_Database/dbo/StoredProcedures/usp_Pewo_GetDueJobs.sql
--
-- Fixes applied:
--   Fix 1  — PENDING_EVENT source handles simultaneous event closes where sibling run
--             pushed Next_Run_At to 50 years. Guards via Pewo_WorkflowStepRun NOT EXISTS.
--   Fix 11 — Batch_Key = @Today replaces fragile 10-second time window for NEW_CHILD detection.
--   Fix 13 — SAFETY_NET UNION ALL catches any COMPLETED parent run with no child run ever,
--             regardless of age or lookback window — no event ever permanently missed.
--   Fix 19 — Fan-out INSERTs wrapped in BEGIN TRANSACTION / COMMIT — atomic, no orphaned runs.
--   Fix 20 — NOT EXISTS Batch_Key = @Today guard prevents repeated fan-out execution each tick.
-- CREATE OR ALTER — safe to rerun.
-- =============================================
CREATE OR ALTER PROCEDURE dbo.usp_Pewo_GetDueJobs
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Now    DATETIME     = GETUTCDATE();
    DECLARE @Today  NVARCHAR(10) = CONVERT(VARCHAR(10), @Now, 120);

    -- ── Fan-out: create NEW_CHILD WorkflowRun rows (Fix 19 — atomic transaction) ──
    BEGIN TRANSACTION;
    BEGIN TRY

        -- Fix 20: NOT EXISTS on Batch_Key prevents repeated fan-out after today's runs created
        INSERT INTO dbo.Pewo_WorkflowRun
            (id_Schedule, id_CustomerWorkflowType, Status, Retry_Count, Max_Retries,
             Batch_Key, created_date, last_updated_date)
        SELECT
            child_s.id_Schedule,
            child_cwt.id_CustomerWorkflowType,
            'PENDING', 0, child_cwt.Max_Retries,
            @Today, @Now, @Now
        FROM      dbo.Pewo_CustomerWorkflowType child_cwt
        JOIN      dbo.Pewo_Schedule child_s
                  ON child_s.id_CustomerWorkflowType = child_cwt.id_CustomerWorkflowType
                  AND child_s.Status = 'ACTIVE' AND child_s.is_Enabled = 1
        JOIN      dbo.Pewo_CustomerWorkflowType parent_cwt
                  ON parent_cwt.WorkflowType_Code = child_cwt.Fan_Out_Source_WorkflowType_Code
                  AND parent_cwt.id_Customer       = child_cwt.id_Customer
        JOIN      dbo.Pewo_WorkflowRun parent_wr
                  ON parent_wr.id_CustomerWorkflowType = parent_cwt.id_CustomerWorkflowType
                  AND parent_wr.Status = 'COMPLETED'
                  AND parent_wr.Finished_At >= DATEADD(HOUR, -child_cwt.Fan_Out_Lookback_Hours, @Now)
        JOIN      dbo.Pewo_WorkflowRunEvent parent_wre
                  ON parent_wre.id_WorkflowRun = parent_wr.id_WorkflowRun
        WHERE     child_cwt.Fan_Out_Source_WorkflowType_Code IS NOT NULL
        AND       child_cwt.is_Active = 1
        AND       child_s.Next_Run_At <= @Now
        AND NOT EXISTS (
            SELECT 1 FROM dbo.Pewo_WorkflowRun
            WHERE  id_CustomerWorkflowType = child_cwt.id_CustomerWorkflowType
            AND    Batch_Key = @Today
            AND    Status NOT IN ('FAILED')
        )
        AND NOT EXISTS (
            SELECT 1
            FROM   dbo.Pewo_WorkflowRun      ex_wr
            JOIN   dbo.Pewo_WorkflowRunEvent ex_wre ON ex_wre.id_WorkflowRun = ex_wr.id_WorkflowRun
            WHERE  ex_wr.id_CustomerWorkflowType = child_cwt.id_CustomerWorkflowType
            AND    ex_wre.Event_Guid = parent_wre.Event_Guid
        );

        -- Fix 11: Batch_Key = @Today for reliable NEW_CHILD detection (not time window)
        INSERT INTO dbo.Pewo_WorkflowRunEvent
            (id_WorkflowRun, id_Event, id_Customer, id_Store, Store_No, Store_Name,
             Event_Guid, Event_Status, Event_Scheduled_Date, Event_Date, Metadata_Json, created_date)
        SELECT
            child_wr.id_WorkflowRun,
            parent_wre.id_Event,   parent_wre.id_Customer, parent_wre.id_Store,
            parent_wre.Store_No,   parent_wre.Store_Name,  parent_wre.Event_Guid,
            parent_wre.Event_Status, parent_wre.Event_Scheduled_Date, parent_wre.Event_Date,
            parent_wre.Metadata_Json, @Now
        FROM      dbo.Pewo_WorkflowRun child_wr
        JOIN      dbo.Pewo_CustomerWorkflowType child_cwt
                  ON child_cwt.id_CustomerWorkflowType = child_wr.id_CustomerWorkflowType
                  AND child_cwt.Fan_Out_Source_WorkflowType_Code IS NOT NULL
        JOIN      dbo.Pewo_CustomerWorkflowType parent_cwt
                  ON parent_cwt.WorkflowType_Code = child_cwt.Fan_Out_Source_WorkflowType_Code
                  AND parent_cwt.id_Customer       = child_cwt.id_Customer
        JOIN      dbo.Pewo_WorkflowRun parent_wr
                  ON parent_wr.id_CustomerWorkflowType = parent_cwt.id_CustomerWorkflowType
                  AND parent_wr.Status = 'COMPLETED'
                  AND parent_wr.Finished_At >= DATEADD(HOUR, -child_cwt.Fan_Out_Lookback_Hours, @Now)
        JOIN      dbo.Pewo_WorkflowRunEvent parent_wre
                  ON parent_wre.id_WorkflowRun = parent_wr.id_WorkflowRun
        WHERE     child_wr.Status = 'PENDING'
        AND       child_wr.Batch_Key = @Today
        AND       child_cwt.Fan_Out_Source_WorkflowType_Code IS NOT NULL
        AND NOT EXISTS (
            SELECT 1 FROM dbo.Pewo_WorkflowRunEvent
            WHERE  id_WorkflowRun = child_wr.id_WorkflowRun
        );

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        DECLARE @ErrMsg NVARCHAR(500) = ERROR_MESSAGE();
        RAISERROR('[PEWO] usp_Pewo_GetDueJobs fan-out failed: %s', 10, 1, @ErrMsg) WITH NOWAIT;
    END CATCH;

    -- ── Return all due jobs ───────────────────────────────────────────────────

    -- SCHEDULE: Next_Run_At due, non-fan-out workflow, no existing pending run
    SELECT s.id_Schedule, s.id_CustomerWorkflowType, s.Schedule_Name, s.Cron_Expression,
           s.Timezone, s.Next_Run_At, s.Status, s.Last_Run_Id,
           cwt.WorkflowType_Code, cwt.WorkflowType_Name, cwt.Max_Retries,
           NULL AS id_WorkflowRun, 'SCHEDULE' AS Job_Source
    FROM   dbo.Pewo_Schedule s
    JOIN   dbo.Pewo_CustomerWorkflowType cwt ON cwt.id_CustomerWorkflowType = s.id_CustomerWorkflowType
    WHERE  s.Next_Run_At <= @Now AND s.Status = 'ACTIVE' AND s.is_Enabled = 1 AND cwt.is_Active = 1
    AND    cwt.Fan_Out_Source_WorkflowType_Code IS NULL
    AND NOT EXISTS (
        SELECT 1 FROM dbo.Pewo_WorkflowRun
        WHERE  id_Schedule = s.id_Schedule AND Status IN ('PENDING','RUNNING')
    )

    UNION ALL

    -- RETRY: failed run with Retry_At now due and retries remaining
    SELECT s.id_Schedule, wr.id_CustomerWorkflowType, s.Schedule_Name, s.Cron_Expression,
           s.Timezone, s.Next_Run_At, s.Status, s.Last_Run_Id,
           cwt.WorkflowType_Code, cwt.WorkflowType_Name, cwt.Max_Retries,
           wr.id_WorkflowRun, 'RETRY' AS Job_Source
    FROM   dbo.Pewo_WorkflowRun wr
    JOIN   dbo.Pewo_Schedule s ON s.id_Schedule = wr.id_Schedule
    JOIN   dbo.Pewo_CustomerWorkflowType cwt ON cwt.id_CustomerWorkflowType = wr.id_CustomerWorkflowType
    WHERE  wr.Status = 'FAILED' AND wr.Retry_At <= @Now
    AND    wr.Retry_Count < cwt.Max_Retries AND cwt.is_Active = 1

    UNION ALL

    -- NEW_CHILD: fan-out child runs created above — Fix 11 uses Batch_Key = @Today
    SELECT s.id_Schedule, wr.id_CustomerWorkflowType, s.Schedule_Name, s.Cron_Expression,
           s.Timezone, s.Next_Run_At, s.Status, s.Last_Run_Id,
           cwt.WorkflowType_Code, cwt.WorkflowType_Name, cwt.Max_Retries,
           wr.id_WorkflowRun, 'NEW_CHILD' AS Job_Source
    FROM   dbo.Pewo_WorkflowRun wr
    JOIN   dbo.Pewo_CustomerWorkflowType cwt ON cwt.id_CustomerWorkflowType = wr.id_CustomerWorkflowType
    JOIN   dbo.Pewo_Schedule s ON s.id_CustomerWorkflowType = wr.id_CustomerWorkflowType
    WHERE  wr.Status = 'PENDING'
    AND    wr.Batch_Key = @Today
    AND    cwt.Fan_Out_Source_WorkflowType_Code IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM dbo.Pewo_WorkflowStepRun
        WHERE  id_WorkflowRun = wr.id_WorkflowRun
    )

    UNION ALL

    -- PENDING_EVENT: Fix 1 — ON_EVENT_CLOSE runs orphaned because sibling run
    -- completed first and pushed shared schedule Next_Run_At to 50 years.
    -- Only picks up runs where no step execution has started yet.
    SELECT s.id_Schedule, wr.id_CustomerWorkflowType, s.Schedule_Name, s.Cron_Expression,
           s.Timezone, s.Next_Run_At, s.Status, s.Last_Run_Id,
           cwt.WorkflowType_Code, cwt.WorkflowType_Name, cwt.Max_Retries,
           wr.id_WorkflowRun, 'PENDING_EVENT' AS Job_Source
    FROM   dbo.Pewo_WorkflowRun wr
    JOIN   dbo.Pewo_CustomerWorkflowType cwt ON cwt.id_CustomerWorkflowType = wr.id_CustomerWorkflowType
    JOIN   dbo.Pewo_Schedule s ON s.id_CustomerWorkflowType = wr.id_CustomerWorkflowType
    WHERE  wr.Status = 'PENDING'
    AND    s.Cron_Expression = 'ON_EVENT_CLOSE'
    AND    cwt.Fan_Out_Source_WorkflowType_Code IS NULL
    AND    cwt.is_Active = 1
    AND NOT EXISTS (
        SELECT 1 FROM dbo.Pewo_WorkflowStepRun
        WHERE  id_WorkflowRun = wr.id_WorkflowRun
    )

    UNION ALL

    -- SAFETY_NET: Fix 13 — catches any COMPLETED parent run that has no child delivery run
    -- ever created, regardless of age. Prevents permanent event loss from delayed schedules.
    -- Returns as SCHEDULE source so worker creates a new run via CreateWorkflowRunAsync.
    SELECT s.id_Schedule, child_cwt.id_CustomerWorkflowType, s.Schedule_Name, s.Cron_Expression,
           s.Timezone, s.Next_Run_At, s.Status, s.Last_Run_Id,
           child_cwt.WorkflowType_Code, child_cwt.WorkflowType_Name, child_cwt.Max_Retries,
           NULL AS id_WorkflowRun, 'SAFETY_NET' AS Job_Source
    FROM   dbo.Pewo_CustomerWorkflowType child_cwt
    JOIN   dbo.Pewo_Schedule s ON s.id_CustomerWorkflowType = child_cwt.id_CustomerWorkflowType
    JOIN   dbo.Pewo_CustomerWorkflowType parent_cwt
           ON parent_cwt.WorkflowType_Code = child_cwt.Fan_Out_Source_WorkflowType_Code
           AND parent_cwt.id_Customer       = child_cwt.id_Customer
    JOIN   dbo.Pewo_WorkflowRun parent_wr
           ON parent_wr.id_CustomerWorkflowType = parent_cwt.id_CustomerWorkflowType
           AND parent_wr.Status = 'COMPLETED'
    JOIN   dbo.Pewo_WorkflowRunEvent parent_wre
           ON parent_wre.id_WorkflowRun = parent_wr.id_WorkflowRun
    WHERE  child_cwt.Fan_Out_Source_WorkflowType_Code IS NOT NULL
    AND    child_cwt.is_Active = 1
    AND    s.Status = 'ACTIVE' AND s.is_Enabled = 1
    AND NOT EXISTS (
        SELECT 1
        FROM   dbo.Pewo_WorkflowRun      ex_wr
        JOIN   dbo.Pewo_WorkflowRunEvent ex_wre ON ex_wre.id_WorkflowRun = ex_wr.id_WorkflowRun
        WHERE  ex_wr.id_CustomerWorkflowType = child_cwt.id_CustomerWorkflowType
        AND    ex_wre.Event_Guid = parent_wre.Event_Guid
    );
END
GO

-- =============================================
-- usp_Pewo_GetDueJobs
-- Returns all jobs due to run this tick.
-- Source types:
--   SCHEDULE  — schedule Next_Run_At due, no active run, no fan-out source
--   RETRY     — failed run whose Retry_At is now due
--   NEW_CHILD — fan-out: child runs created from completed parent runs (e.g. GM_PRC_DELIVERY from GM_TOTALS_CHECK)
-- CREATE OR ALTER — safe to rerun.
-- =============================================
CREATE OR ALTER PROCEDURE dbo.usp_Pewo_GetDueJobs
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Now DATETIME = GETUTCDATE();

    -- Step 1: Fan-out INSERT — create NEW_CHILD WorkflowRun rows
    -- For each fan-out workflow type, find completed parent runs within lookback window
    -- that don't yet have a child run for this workflow type + event guid combination.
    INSERT INTO dbo.Pewo_WorkflowRun
        (id_Schedule, id_CustomerWorkflowType, Status, Retry_Count, Max_Retries,
         Batch_Key, created_date, last_updated_date)
    SELECT
        child_s.id_Schedule,
        child_cwt.id_CustomerWorkflowType,
        'PENDING',
        0,
        child_cwt.Max_Retries,
        CONVERT(VARCHAR(10), @Now, 120),
        @Now,
        @Now
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
        SELECT 1
        FROM   dbo.Pewo_WorkflowRun   ex_wr
        JOIN   dbo.Pewo_WorkflowRunEvent ex_wre ON ex_wre.id_WorkflowRun = ex_wr.id_WorkflowRun
        WHERE  ex_wr.id_CustomerWorkflowType = child_cwt.id_CustomerWorkflowType
        AND    ex_wre.Event_Guid = parent_wre.Event_Guid
    );

    -- Step 2: Copy WorkflowRunEvent from parent to newly created child runs
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
    AND       child_wr.created_date >= DATEADD(SECOND, -10, @Now)
    AND NOT EXISTS (SELECT 1 FROM dbo.Pewo_WorkflowRunEvent WHERE id_WorkflowRun = child_wr.id_WorkflowRun);

    -- Step 3: Return all due jobs
    SELECT s.id_Schedule, s.id_CustomerWorkflowType, s.Schedule_Name, s.Cron_Expression,
           s.Timezone, s.Next_Run_At, s.Status, s.Last_Run_Id,
           cwt.WorkflowType_Code, cwt.WorkflowType_Name, cwt.Max_Retries,
           NULL AS id_WorkflowRun, 'SCHEDULE' AS Job_Source
    FROM   dbo.Pewo_Schedule s
    JOIN   dbo.Pewo_CustomerWorkflowType cwt ON cwt.id_CustomerWorkflowType = s.id_CustomerWorkflowType
    WHERE  s.Next_Run_At <= @Now AND s.Status = 'ACTIVE' AND s.is_Enabled = 1 AND cwt.is_Active = 1
    AND    cwt.Fan_Out_Source_WorkflowType_Code IS NULL
    AND NOT EXISTS (SELECT 1 FROM dbo.Pewo_WorkflowRun
                    WHERE id_Schedule = s.id_Schedule AND Status IN ('PENDING','RUNNING'))

    UNION ALL

    SELECT s.id_Schedule, wr.id_CustomerWorkflowType, s.Schedule_Name, s.Cron_Expression,
           s.Timezone, s.Next_Run_At, s.Status, s.Last_Run_Id,
           cwt.WorkflowType_Code, cwt.WorkflowType_Name, cwt.Max_Retries,
           wr.id_WorkflowRun, 'RETRY' AS Job_Source
    FROM   dbo.Pewo_WorkflowRun wr
    JOIN   dbo.Pewo_Schedule s ON s.id_Schedule = wr.id_Schedule
    JOIN   dbo.Pewo_CustomerWorkflowType cwt ON cwt.id_CustomerWorkflowType = wr.id_CustomerWorkflowType
    WHERE  wr.Status = 'FAILED' AND wr.Retry_At <= @Now AND wr.Retry_Count < cwt.Max_Retries AND cwt.is_Active = 1

    UNION ALL

    SELECT s.id_Schedule, wr.id_CustomerWorkflowType, s.Schedule_Name, s.Cron_Expression,
           s.Timezone, s.Next_Run_At, s.Status, s.Last_Run_Id,
           cwt.WorkflowType_Code, cwt.WorkflowType_Name, cwt.Max_Retries,
           wr.id_WorkflowRun, 'NEW_CHILD' AS Job_Source
    FROM   dbo.Pewo_WorkflowRun wr
    JOIN   dbo.Pewo_CustomerWorkflowType cwt ON cwt.id_CustomerWorkflowType = wr.id_CustomerWorkflowType
    JOIN   dbo.Pewo_Schedule s ON s.id_CustomerWorkflowType = wr.id_CustomerWorkflowType
    WHERE  wr.Status = 'PENDING' AND wr.created_date >= DATEADD(SECOND, -10, @Now)
    AND    cwt.Fan_Out_Source_WorkflowType_Code IS NOT NULL;
END

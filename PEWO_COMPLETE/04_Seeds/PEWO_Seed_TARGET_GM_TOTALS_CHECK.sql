-- =============================================
-- DACPAC Post-Deployment Script
-- Workflow: GM_TOTALS_CHECK
-- Rerunnable: YES — safe to execute multiple times.
-- No prerequisites.
-- =============================================

DECLARE @id_Customer             INT;
DECLARE @id_CustomerWorkflowType INT;
DECLARE @id_Schedule             INT;

-- ── Resolve id_Customer dynamically per environment ──────────────────────────
SET @id_Customer = (
    SELECT id_Customer
    FROM   dbo.Customer
    WHERE  Name       = 'TARGET FLEXCOUNT'
      AND  is_Deleted = 0
);

IF @id_Customer IS NULL
BEGIN
    RAISERROR('TARGET FLEXCOUNT customer not found in dbo.Customer. Verify Name and is_Deleted=0 before running seed.', 16, 1);
    RETURN;
END

-- ── 1. Pewo_CustomerWorkflowType ─────────────────────────────────────────────
IF EXISTS (
    SELECT 1 FROM dbo.Pewo_CustomerWorkflowType
    WHERE  id_Customer = @id_Customer AND WorkflowType_Code = 'GM_TOTALS_CHECK'
)
BEGIN
    SELECT @id_CustomerWorkflowType = id_CustomerWorkflowType
    FROM   dbo.Pewo_CustomerWorkflowType
    WHERE  id_Customer = @id_Customer AND WorkflowType_Code = 'GM_TOTALS_CHECK';
END
ELSE
BEGIN
    INSERT INTO dbo.Pewo_CustomerWorkflowType
        (id_Customer, WorkflowType_Code, WorkflowType_Name, Description,
         Max_Retries, is_Active, Fan_Out_Source_WorkflowType_Code,
         Fan_Out_Lookback_Hours, created_date, last_updated_date)
    VALUES
        (@id_Customer, 'GM_TOTALS_CHECK', 'TARGET GM + PRPC Totals Check',
         'Day 1 — validates GM and PRPC files immediately after event close, emails per-event result',
         3, 1, NULL, NULL, GETUTCDATE(), GETUTCDATE());
    SET @id_CustomerWorkflowType = SCOPE_IDENTITY();
END

-- ── 2. Pewo_WorkflowStepDef — MERGE ──────────────────────────────────────────
MERGE dbo.Pewo_WorkflowStepDef AS target
USING (
    VALUES
        (@id_CustomerWorkflowType, 1, 'TOTALS_CHECK', 'Validate GM + PRPC Totals',
         '{"sourceContainer":"fc-hold-target","filePatternGm":"TAR_ITM_GM_","filePatternPrpc":"TAR_ITM_PRPC_"}',
         3, 60, 1),
        (@id_CustomerWorkflowType, 2, 'EMAIL', 'Send Totals Check Result',
         '{"recipients":["ops-team@company.com"],"subject":"TARGET Totals Check Result"}',
         3, 30, 1)
) AS source (id_CustomerWorkflowType, Step_Order, Step_Kind, Step_Name, Config, Max_Attempts, Backoff_Seconds, is_Active)
ON  target.id_CustomerWorkflowType = source.id_CustomerWorkflowType
AND target.Step_Order = source.Step_Order
WHEN MATCHED THEN UPDATE SET
    Step_Kind = source.Step_Kind, Step_Name = source.Step_Name, Config = source.Config,
    Max_Attempts = source.Max_Attempts, Backoff_Seconds = source.Backoff_Seconds,
    is_Active = source.is_Active, last_updated_date = GETUTCDATE()
WHEN NOT MATCHED BY TARGET THEN INSERT
    (id_CustomerWorkflowType, Step_Order, Step_Kind, Step_Name, Config, Max_Attempts, Backoff_Seconds, is_Active, created_date, last_updated_date)
VALUES
    (source.id_CustomerWorkflowType, source.Step_Order, source.Step_Kind, source.Step_Name,
     source.Config, source.Max_Attempts, source.Backoff_Seconds, source.is_Active, GETUTCDATE(), GETUTCDATE());

-- ── 3. Pewo_Schedule ─────────────────────────────────────────────────────────
IF EXISTS (SELECT 1 FROM dbo.Pewo_Schedule WHERE id_CustomerWorkflowType = @id_CustomerWorkflowType)
BEGIN
    SELECT @id_Schedule = id_Schedule FROM dbo.Pewo_Schedule WHERE id_CustomerWorkflowType = @id_CustomerWorkflowType;
    UPDATE dbo.Pewo_Schedule
    SET    Schedule_Name = 'TARGET GM_TOTALS_CHECK (event-close primed)',
           Cron_Expression = 'EVENT_PRIMED', Timezone = 'UTC', is_Enabled = 1, last_updated_date = GETUTCDATE()
    WHERE  id_Schedule = @id_Schedule;
    -- Next_Run_At intentionally NOT updated — only usp_Pewo_CreateRunOnEventClose sets it
END
ELSE
BEGIN
    INSERT INTO dbo.Pewo_Schedule
        (id_CustomerWorkflowType, Schedule_Name, Cron_Expression, Timezone, Next_Run_At, Status, is_Enabled, created_date, last_updated_date)
    VALUES
        (@id_CustomerWorkflowType, 'TARGET GM_TOTALS_CHECK (event-close primed)',
         'EVENT_PRIMED', 'UTC',
         DATEADD(YEAR, 50, GETUTCDATE()),
         'ACTIVE', 1, GETUTCDATE(), GETUTCDATE());
    SET @id_Schedule = SCOPE_IDENTITY();
END

UPDATE dbo.Customer SET Has_Post_Event_Workflow = 1, last_updated_date = GETUTCDATE() WHERE id_Customer = @id_Customer;

SELECT 'Pewo_CustomerWorkflowType' AS [Table], @id_CustomerWorkflowType AS [id], 'GM_TOTALS_CHECK' AS [Code];
SELECT 'Pewo_Schedule' AS [Table], @id_Schedule AS [id_Schedule], Cron_Expression, Next_Run_At, Status FROM dbo.Pewo_Schedule WHERE id_Schedule = @id_Schedule;
SELECT Step_Order, Step_Kind, Step_Name, Max_Attempts, Backoff_Seconds, is_Active FROM dbo.Pewo_WorkflowStepDef WHERE id_CustomerWorkflowType = @id_CustomerWorkflowType ORDER BY Step_Order;
GO

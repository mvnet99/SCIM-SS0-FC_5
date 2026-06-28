-- =============================================
-- DACPAC Post-Deployment Script
-- Workflow: GM_PRC_DELIVERY_SUMMARY
-- Rerunnable: YES — safe to execute multiple times.
-- Prerequisite: GM_PRC_DELIVERY seed must have run first.
-- =============================================

DECLARE @id_Customer             INT;
DECLARE @id_CustomerWorkflowType INT;
DECLARE @id_Schedule             INT;

SET @id_Customer = (
    SELECT id_Customer FROM dbo.Customer
    WHERE  Name = 'TARGET FLEXCOUNT' AND is_Deleted = 0
);

IF @id_Customer IS NULL
BEGIN
    RAISERROR('TARGET FLEXCOUNT customer not found in dbo.Customer. Verify Name and is_Deleted=0 before running seed.', 16, 1);
    RETURN;
END

IF NOT EXISTS (
    SELECT 1 FROM dbo.Pewo_CustomerWorkflowType
    WHERE  id_Customer = @id_Customer AND WorkflowType_Code = 'GM_PRC_DELIVERY'
)
BEGIN
    RAISERROR('GM_PRC_DELIVERY workflow not found for this customer. Run PEWO_Seed_TARGET_GM_PRC_DELIVERY.sql first.', 16, 1);
    RETURN;
END

-- ── 1. Pewo_CustomerWorkflowType ─────────────────────────────────────────────
IF EXISTS (
    SELECT 1 FROM dbo.Pewo_CustomerWorkflowType
    WHERE  id_Customer = @id_Customer AND WorkflowType_Code = 'GM_PRC_DELIVERY_SUMMARY'
)
BEGIN
    SELECT @id_CustomerWorkflowType = id_CustomerWorkflowType
    FROM   dbo.Pewo_CustomerWorkflowType
    WHERE  id_Customer = @id_Customer AND WorkflowType_Code = 'GM_PRC_DELIVERY_SUMMARY';
END
ELSE
BEGIN
    INSERT INTO dbo.Pewo_CustomerWorkflowType
        (id_Customer, WorkflowType_Code, WorkflowType_Name, Description,
         Max_Retries, is_Active, Fan_Out_Source_WorkflowType_Code,
         Fan_Out_Lookback_Hours, created_date, last_updated_date)
    VALUES
        (@id_Customer, 'GM_PRC_DELIVERY_SUMMARY', 'TARGET GM + PRPC Delivery Acknowledgment',
         'Sends one consolidated per-event ack email once the day''s GM_PRC_DELIVERY batch is fully terminal',
         6, 1, NULL, NULL, GETUTCDATE(), GETUTCDATE());
    SET @id_CustomerWorkflowType = SCOPE_IDENTITY();
END

-- ── 2. Pewo_WorkflowStepDef — MERGE ──────────────────────────────────────────
-- Max_Attempts=6 + Backoff_Seconds=1800 gives several hours for batch to settle.
-- EMAIL_SUMMARY returns failure (not exception) while batch still active —
-- retry/backoff acts as a wait gate until GM_PRC_DELIVERY batch is terminal.
MERGE dbo.Pewo_WorkflowStepDef AS target
USING (
    VALUES
        (@id_CustomerWorkflowType, 1, 'EMAIL_SUMMARY', 'Send Consolidated Delivery Acknowledgment',
         '{"batchWorkflowTypeCode":"GM_PRC_DELIVERY","recipients":["ops-team@company.com"],"subject":"TARGET GM + PRPC Delivery Summary"}',
         6, 1800, 1)
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
    SET    Schedule_Name = 'TARGET GM_PRC_DELIVERY_SUMMARY Daily 9AM', Cron_Expression = '0 9 * * *',
           Timezone = 'UTC', is_Enabled = 1, last_updated_date = GETUTCDATE()
    WHERE  id_Schedule = @id_Schedule;
    -- Next_Run_At intentionally NOT updated
END
ELSE
BEGIN
    INSERT INTO dbo.Pewo_Schedule
        (id_CustomerWorkflowType, Schedule_Name, Cron_Expression, Timezone, Next_Run_At, Status, is_Enabled, created_date, last_updated_date)
    VALUES
        (@id_CustomerWorkflowType, 'TARGET GM_PRC_DELIVERY_SUMMARY Daily 9AM', '0 9 * * *', 'UTC',
         DATEADD(HOUR, 9, CAST(CAST(GETUTCDATE() AS DATE) AS DATETIME)),
         'ACTIVE', 1, GETUTCDATE(), GETUTCDATE());
    SET @id_Schedule = SCOPE_IDENTITY();
END

SELECT 'Pewo_CustomerWorkflowType' AS [Table], @id_CustomerWorkflowType AS [id], 'GM_PRC_DELIVERY_SUMMARY' AS [Code];
SELECT 'Pewo_Schedule' AS [Table], @id_Schedule AS [id_Schedule], Cron_Expression, Next_Run_At, Status FROM dbo.Pewo_Schedule WHERE id_Schedule = @id_Schedule;
SELECT Step_Order, Step_Kind, Step_Name, Max_Attempts, Backoff_Seconds FROM dbo.Pewo_WorkflowStepDef WHERE id_CustomerWorkflowType = @id_CustomerWorkflowType ORDER BY Step_Order;
GO

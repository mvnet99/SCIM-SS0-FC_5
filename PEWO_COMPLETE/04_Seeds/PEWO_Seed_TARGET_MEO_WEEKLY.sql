-- =============================================
-- DACPAC Post-Deployment Script
-- Workflow: MEO_WEEKLY
-- Rerunnable: YES — safe to execute multiple times.
-- No prerequisites — MEO is independent of GM workflows.
-- =============================================

DECLARE @id_Customer             INT;
DECLARE @id_CustomerWorkflowType INT;
DECLARE @id_Schedule             INT;
DECLARE @SftpConfig              NVARCHAR(500);

SET @id_Customer = (
    SELECT id_Customer FROM dbo.Customer
    WHERE  Name = 'TARGET FLEXCOUNT' AND is_Deleted = 0
);

IF @id_Customer IS NULL
BEGIN
    RAISERROR('TARGET FLEXCOUNT customer not found in dbo.Customer. Verify Name and is_Deleted=0 before running seed.', 16, 1);
    RETURN;
END

SET @SftpConfig = N'{"remotePath":"/www.data/target-ssh/nexgen","stagingContainer":"flexcount-save","idCustomer":' + CAST(@id_Customer AS NVARCHAR) + N'}';

-- ── 1. Pewo_CustomerWorkflowType ─────────────────────────────────────────────
IF EXISTS (
    SELECT 1 FROM dbo.Pewo_CustomerWorkflowType
    WHERE  id_Customer = @id_Customer AND WorkflowType_Code = 'MEO_WEEKLY'
)
BEGIN
    SELECT @id_CustomerWorkflowType = id_CustomerWorkflowType
    FROM   dbo.Pewo_CustomerWorkflowType
    WHERE  id_Customer = @id_Customer AND WorkflowType_Code = 'MEO_WEEKLY';
END
ELSE
BEGIN
    INSERT INTO dbo.Pewo_CustomerWorkflowType
        (id_Customer, WorkflowType_Code, WorkflowType_Name, Description,
         Max_Retries, is_Active, Fan_Out_Source_WorkflowType_Code,
         Fan_Out_Lookback_Hours, created_date, last_updated_date)
    VALUES
        (@id_Customer, 'MEO_WEEKLY', 'TARGET MEO Weekly Delivery',
         'Weekly MEO file transformation and delivery to TARGET every Wednesday at 8AM UTC',
         3, 1, NULL, NULL, GETUTCDATE(), GETUTCDATE());
    SET @id_CustomerWorkflowType = SCOPE_IDENTITY();
END

-- ── 2. Pewo_WorkflowStepDef — MERGE ──────────────────────────────────────────
MERGE dbo.Pewo_WorkflowStepDef AS target
USING (
    VALUES
        (@id_CustomerWorkflowType, 1, 'GET_EVENTS', 'Discover Closed Events Since Last Run',
         '{"sourceContainer":"fc-hold-target","filePattern":"MEO_","eventSource":"query","sinceLastRun":true}',
         3, 60, 1),
        (@id_CustomerWorkflowType, 2, 'TRANSFORM', 'Transform MEO Files',
         '{"transformationType":"MEO","sourceContainer":"fc-hold-target","outputContainer":"flexcount-save","filePattern":"MEO_","operations":["operation_1","operation_2","operation_3"]}',
         3, 60, 1),
        (@id_CustomerWorkflowType, 3, 'READ_BLOB_ZIP', 'Zip Transformed MEO Files',
         '{"sourceContainer":"flexcount-save","filePattern":"MEO_TRANSFORMED_","stagingContainer":"flexcount-save"}',
         3, 60, 1),
        (@id_CustomerWorkflowType, 4, 'SFTP', 'Deliver MEO Zip to TARGET SFTP',
         @SftpConfig,
         3, 120, 1),
        (@id_CustomerWorkflowType, 5, 'EMAIL', 'Send MEO Weekly Delivery Notification',
         '{"recipients":["ops-team@company.com"],"subject":"TARGET MEO Weekly Files Delivered"}',
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
    SET    Schedule_Name = 'TARGET MEO Weekly Wednesday 8AM', Cron_Expression = '0 8 * * 3',
           Timezone = 'UTC', is_Enabled = 1, last_updated_date = GETUTCDATE()
    WHERE  id_Schedule = @id_Schedule;
END
ELSE
BEGIN
    INSERT INTO dbo.Pewo_Schedule
        (id_CustomerWorkflowType, Schedule_Name, Cron_Expression, Timezone, Next_Run_At, Status, is_Enabled, created_date, last_updated_date)
    VALUES
        (@id_CustomerWorkflowType, 'TARGET MEO Weekly Wednesday 8AM', '0 8 * * 3', 'UTC',
         DATEADD(DAY, ((4 - DATEPART(WEEKDAY, GETUTCDATE()) + 7) % 7),
             CAST(CAST(GETUTCDATE() AS DATE) AS DATETIME)) + '08:00:00',
         'ACTIVE', 1, GETUTCDATE(), GETUTCDATE());
    SET @id_Schedule = SCOPE_IDENTITY();
END

SELECT 'Pewo_CustomerWorkflowType' AS [Table], @id_CustomerWorkflowType AS [id], 'MEO_WEEKLY' AS [Code];
SELECT 'Pewo_Schedule' AS [Table], @id_Schedule AS [id_Schedule], Cron_Expression, Next_Run_At, Status FROM dbo.Pewo_Schedule WHERE id_Schedule = @id_Schedule;
SELECT Step_Order, Step_Kind, Step_Name, Max_Attempts, Backoff_Seconds, is_Active FROM dbo.Pewo_WorkflowStepDef WHERE id_CustomerWorkflowType = @id_CustomerWorkflowType ORDER BY Step_Order;
GO

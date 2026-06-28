CREATE OR ALTER PROCEDURE dbo.usp_Pewo_CreateRunOnEventClose
    @id_Customer          INT,
    @id_Event             INT,
    @Store_No             NVARCHAR(50),
    @Store_Name           NVARCHAR(200) = NULL,
    @Event_Date           DATETIME,
    @WorkflowType_Code    NVARCHAR(100) = NULL,
    @Event_Guid           UNIQUEIDENTIFIER = NULL,
    @id_Store             INT = NULL,
    @Event_Status         NVARCHAR(50) = NULL,
    @Event_Scheduled_Date DATETIME = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Now DATETIME = GETUTCDATE();

    -- Create one WorkflowRun per active workflow type for this customer
    -- that is event-primed (Cron_Expression = 'EVENT_PRIMED')
    -- and does not yet have a run for this event guid
    INSERT INTO dbo.Pewo_WorkflowRun
        (id_Schedule, id_CustomerWorkflowType, Status, Retry_Count, Max_Retries, created_date, last_updated_date)
    SELECT
        s.id_Schedule,
        cwt.id_CustomerWorkflowType,
        'PENDING',
        0,
        cwt.Max_Retries,
        @Now,
        @Now
    FROM      dbo.Pewo_CustomerWorkflowType cwt
    JOIN      dbo.Pewo_Schedule s ON s.id_CustomerWorkflowType = cwt.id_CustomerWorkflowType
    WHERE     cwt.id_Customer  = @id_Customer
    AND       cwt.is_Active    = 1
    AND       s.Cron_Expression = 'EVENT_PRIMED'
    AND       s.is_Enabled      = 1
    AND       (@WorkflowType_Code IS NULL OR cwt.WorkflowType_Code = @WorkflowType_Code)
    AND NOT EXISTS (
        SELECT 1
        FROM   dbo.Pewo_WorkflowRun ex_wr
        JOIN   dbo.Pewo_WorkflowRunEvent ex_wre ON ex_wre.id_WorkflowRun = ex_wr.id_WorkflowRun
        WHERE  ex_wr.id_CustomerWorkflowType = cwt.id_CustomerWorkflowType
        AND    ex_wre.Event_Guid = @Event_Guid
    );

    -- Create WorkflowRunEvent for each new run
    INSERT INTO dbo.Pewo_WorkflowRunEvent
        (id_WorkflowRun, id_Event, id_Customer, id_Store, Store_No, Store_Name,
         Event_Guid, Event_Status, Event_Scheduled_Date, Event_Date, created_date)
    SELECT
        wr.id_WorkflowRun,
        @id_Event, @id_Customer, @id_Store, @Store_No, @Store_Name,
        @Event_Guid, @Event_Status, @Event_Scheduled_Date, @Event_Date, @Now
    FROM   dbo.Pewo_WorkflowRun wr
    WHERE  wr.created_date >= DATEADD(SECOND, -5, @Now)
    AND    wr.Status = 'PENDING'
    AND NOT EXISTS (SELECT 1 FROM dbo.Pewo_WorkflowRunEvent WHERE id_WorkflowRun = wr.id_WorkflowRun);

    -- Advance schedule Next_Run_At to now so next worker tick picks it up
    UPDATE s
    SET    s.Next_Run_At = @Now, s.last_updated_date = @Now
    FROM   dbo.Pewo_Schedule s
    JOIN   dbo.Pewo_WorkflowRun wr ON wr.id_Schedule = s.id_Schedule
    WHERE  wr.created_date >= DATEADD(SECOND, -5, @Now)
    AND    wr.Status = 'PENDING';

    -- Return created runs
    SELECT wr.id_WorkflowRun, wr.id_CustomerWorkflowType, cwt.WorkflowType_Code
    FROM   dbo.Pewo_WorkflowRun wr
    JOIN   dbo.Pewo_CustomerWorkflowType cwt ON cwt.id_CustomerWorkflowType = wr.id_CustomerWorkflowType
    WHERE  wr.created_date >= DATEADD(SECOND, -5, @Now)
    AND    wr.Status = 'PENDING';
END

CREATE OR ALTER PROCEDURE dbo.usp_Pewo_GetWorkflowRunEvents
    @id_WorkflowRun INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT id_WorkflowRunEvent, id_WorkflowRun, id_Event, id_Customer, id_Store,
           Store_No, Store_Name, Event_Guid, Event_Status,
           Event_Scheduled_Date, Event_Date, Metadata_Json, created_date
    FROM   dbo.Pewo_WorkflowRunEvent
    WHERE  id_WorkflowRun = @id_WorkflowRun;
END

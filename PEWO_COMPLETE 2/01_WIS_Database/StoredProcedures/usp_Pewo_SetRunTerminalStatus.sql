CREATE OR ALTER PROCEDURE dbo.usp_Pewo_SetRunTerminalStatus
    @id_WorkflowRun INT,
    @Status         NVARCHAR(20),
    @Reason         NVARCHAR(2000) = NULL,
    @Retry_At       DATETIME = NULL,
    @Retry_Count    SMALLINT = 0
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Pewo_WorkflowRun
    SET    Status            = @Status,
           Reason            = @Reason,
           Retry_At          = @Retry_At,
           Retry_Count       = @Retry_Count,
           Finished_At       = CASE WHEN @Status IN ('COMPLETED','FAILED','CANCELLED') THEN GETUTCDATE() ELSE Finished_At END,
           last_updated_date = GETUTCDATE()
    WHERE  id_WorkflowRun = @id_WorkflowRun;
END

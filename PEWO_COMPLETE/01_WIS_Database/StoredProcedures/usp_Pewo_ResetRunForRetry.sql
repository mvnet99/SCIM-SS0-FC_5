CREATE OR ALTER PROCEDURE dbo.usp_Pewo_ResetRunForRetry
    @id_WorkflowRun INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Pewo_WorkflowRun
    SET    Status            = 'PENDING',
           Reason            = NULL,
           Retry_At          = NULL,
           last_updated_date = GETUTCDATE()
    WHERE  id_WorkflowRun = @id_WorkflowRun;

    -- Reset only FAILED steps — leave COMPLETED steps untouched (resume-not-restart)
    UPDATE dbo.Pewo_WorkflowStepRun
    SET    Status            = 'PENDING',
           Failure_Details   = NULL,
           last_updated_date = GETUTCDATE()
    WHERE  id_WorkflowRun = @id_WorkflowRun
    AND    Status = 'FAILED';
END

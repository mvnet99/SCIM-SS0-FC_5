-- =============================================
-- usp_Pewo_ResetRunForRetry
-- WIS_Database/dbo/StoredProcedures/usp_Pewo_ResetRunForRetry.sql
--
-- Changes:
--   Retry_Count = 0 — resets so RETRY source filter (Retry_Count < Max_Retries) passes again.
--                     Without this, a run exhausting automated retries can never be
--                     picked up by RETRY source even after manual reset.
--   Finished_At = NULL — run has not finished, clear stale timestamp.
--   Fix 14 audit log — INSERT to Pewo_WorkflowRunLog records manual retry for ops visibility.
--   Resume-not-restart — only FAILED steps reset, COMPLETED steps untouched.
-- CREATE OR ALTER — safe to rerun.
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[usp_Pewo_ResetRunForRetry]
    @id_WorkflowRun INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Reset run to PENDING with clean retry state
    UPDATE dbo.Pewo_WorkflowRun
    SET    Status            = 'PENDING',
           Reason            = NULL,
           Retry_At          = NULL,
           Retry_Count       = 0,            -- reset so RETRY source filter passes again
           Finished_At       = NULL,         -- not finished — clear stale timestamp
           last_updated_date = GETUTCDATE()
    WHERE  id_WorkflowRun = @id_WorkflowRun;

    -- Reset only FAILED steps — COMPLETED steps untouched (resume-not-restart)
    UPDATE dbo.Pewo_WorkflowStepRun
    SET    Status            = 'PENDING',
           Failure_Details   = NULL,
           last_updated_date = GETUTCDATE()
    WHERE  id_WorkflowRun = @id_WorkflowRun
    AND    Status = 'FAILED';

    -- Fix 14: Audit log — record manual retry for ops visibility
    -- Allows SRE to see history of manual interventions in Pewo_WorkflowRunLog
    INSERT INTO dbo.Pewo_WorkflowRunLog
        (id_WorkflowRun, id_Customer, Customer_Name, Step_Kind,
         Log_Level, Message, Event_Context, logged_date, created_by)
    SELECT
        @id_WorkflowRun,
        wre.id_Customer,
        NULL,
        NULL,
        'INFO',
        'Manual retry initiated via POST /api/Pewo/runs/' + CAST(@id_WorkflowRun AS NVARCHAR) + '/retry — Retry_Count reset to 0',
        NULL,
        GETUTCDATE(),
        1
    FROM   dbo.Pewo_WorkflowRun wr
    LEFT JOIN dbo.Pewo_WorkflowRunEvent wre ON wre.id_WorkflowRun = wr.id_WorkflowRun
    WHERE  wr.id_WorkflowRun = @id_WorkflowRun;
END;
GO

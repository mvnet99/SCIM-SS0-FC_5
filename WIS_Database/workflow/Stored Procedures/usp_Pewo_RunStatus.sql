-- =============================================================================
-- usp_Pewo_SetRunTerminalStatus
-- Set WorkflowRun to COMPLETED or FAILED. Sets retry_at on failure.
-- Called by POST /api/pewo/runs/{runId}
-- =============================================================================
CREATE PROCEDURE [workflow].[usp_Pewo_SetRunTerminalStatus]
    @runId       UNIQUEIDENTIFIER,
    @status      VARCHAR(20),   -- 'COMPLETED' | 'FAILED'
    @reason      NVARCHAR(MAX),
    @retryAt     DATETIME2,     -- NULL when status=COMPLETED
    @retryCount  INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE workflow.WorkflowRun
    SET    status      = @status,
           reason      = @reason,
           retry_at    = @retryAt,
           retry_count = @retryCount,
           finished_at = SYSUTCDATETIME()
    WHERE  run_id = @runId;
END;
GO

-- =============================================================================
-- usp_Pewo_ResetRunForRetry
-- Manual retry: reset FAILED StepRun rows to PENDING. Leave SUCCESS untouched.
-- Also reset WorkflowRun status to PENDING.
-- Called by POST /api/pewo/runs/{runId}/retry
-- =============================================================================
CREATE PROCEDURE [workflow].[usp_Pewo_ResetRunForRetry]
    @runId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    -- Reset failed steps to PENDING
    UPDATE workflow.StepRun
    SET    status     = 'PENDING',
           reason     = NULL,
           updated_at = SYSUTCDATETIME()
    WHERE  run_id = @runId
    AND    status = 'FAILED';

    -- Reset the run itself
    UPDATE workflow.WorkflowRun
    SET    status      = 'PENDING',
           reason      = NULL,
           retry_at    = NULL,
           finished_at = NULL
    WHERE  run_id = @runId;
END;
GO

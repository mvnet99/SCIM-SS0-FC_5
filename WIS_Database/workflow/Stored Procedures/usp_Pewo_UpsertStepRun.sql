-- =============================================================================
-- usp_Pewo_UpsertStepRun
-- MERGE upsert: write step result once after step completes.
-- Called by POST /api/pewo/runs/{runId}/steps/{stepId}
-- =============================================================================
CREATE PROCEDURE [workflow].[usp_Pewo_UpsertStepRun]
    @runId       UNIQUEIDENTIFIER,
    @stepDefId   INT,
    @status      VARCHAR(20),
    @attempts    INT,
    @reason      NVARCHAR(MAX),
    @artifactRef NVARCHAR(500),
    @startedAt   DATETIME2,
    @finishedAt  DATETIME2
AS
BEGIN
    SET NOCOUNT ON;

    MERGE workflow.StepRun AS target
    USING (SELECT @runId AS run_id, @stepDefId AS step_def_id) AS source
        ON target.run_id = source.run_id AND target.step_def_id = source.step_def_id
    WHEN MATCHED THEN
        UPDATE SET
            status       = @status,
            attempts     = @attempts,
            reason       = @reason,
            artifact_ref = @artifactRef,
            started_at   = COALESCE(target.started_at, @startedAt),
            finished_at  = @finishedAt,
            updated_at   = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (run_id, step_def_id, status, attempts, reason, artifact_ref, started_at, finished_at)
        VALUES (@runId, @stepDefId, @status, @attempts, @reason, @artifactRef, @startedAt, @finishedAt);
END;
GO

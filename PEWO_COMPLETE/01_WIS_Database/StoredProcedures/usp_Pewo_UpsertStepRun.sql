CREATE OR ALTER PROCEDURE dbo.usp_Pewo_UpsertStepRun
    @id_WorkflowRun     INT,
    @id_WorkflowStepDef INT,
    @Step_Kind          NVARCHAR(50),
    @Status             NVARCHAR(20),
    @Attempts           SMALLINT,
    @Artifact_Ref       NVARCHAR(500) = NULL,
    @Failure_Details    NVARCHAR(2000) = NULL,
    @Start_Time         DATETIME = NULL,
    @End_Time           DATETIME = NULL
AS
BEGIN
    SET NOCOUNT ON;
    MERGE dbo.Pewo_WorkflowStepRun AS target
    USING (SELECT @id_WorkflowRun AS id_WorkflowRun, @id_WorkflowStepDef AS id_WorkflowStepDef) AS source
    ON    target.id_WorkflowRun     = source.id_WorkflowRun
    AND   target.id_WorkflowStepDef = source.id_WorkflowStepDef
    WHEN MATCHED THEN UPDATE SET
        Status            = @Status,
        Attempts          = @Attempts,
        Artifact_Ref      = @Artifact_Ref,
        Failure_Details   = @Failure_Details,
        Start_Time        = @Start_Time,
        End_Time          = @End_Time,
        last_updated_date = GETUTCDATE()
    WHEN NOT MATCHED THEN INSERT
        (id_WorkflowRun, id_WorkflowStepDef, Step_Kind, Status, Attempts,
         Artifact_Ref, Failure_Details, Start_Time, End_Time, created_date, last_updated_date)
    VALUES
        (@id_WorkflowRun, @id_WorkflowStepDef, @Step_Kind, @Status, @Attempts,
         @Artifact_Ref, @Failure_Details, @Start_Time, @End_Time, GETUTCDATE(), GETUTCDATE());
END

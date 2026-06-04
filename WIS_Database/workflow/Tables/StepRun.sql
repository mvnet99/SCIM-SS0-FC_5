CREATE TABLE [workflow].[StepRun] (
    [step_run_id]  UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [run_id]       UNIQUEIDENTIFIER NOT NULL,
    [step_def_id]  INT              NOT NULL,
    [status]       VARCHAR(20)      NOT NULL DEFAULT 'PENDING',
    [attempts]     INT              NOT NULL DEFAULT 0,
    [reason]       NVARCHAR(MAX)    NULL,
    [artifact_ref] NVARCHAR(500)    NULL,
    [started_at]   DATETIME2        NULL,
    [finished_at]  DATETIME2        NULL,
    [created_at]   DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
    [updated_at]   DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [PK_StepRun]             PRIMARY KEY ([step_run_id]),
    CONSTRAINT [FK_StepRun_WorkflowRun] FOREIGN KEY ([run_id])
                                        REFERENCES [workflow].[WorkflowRun]([run_id]),
    CONSTRAINT [FK_StepRun_StepDef]     FOREIGN KEY ([step_def_id])
                                        REFERENCES [workflow].[WorkflowStepDef]([step_def_id]),
    CONSTRAINT [UQ_StepRun_RunStep]     UNIQUE ([run_id], [step_def_id]),
    CONSTRAINT [CK_StepRun_Status]      CHECK ([status] IN (
                                            'PENDING',
                                            'SUCCESS',
                                            'FAILED'
                                        ))
);
GO

CREATE INDEX [IX_StepRun_Resume]
    ON [workflow].[StepRun] ([run_id], [step_def_id])
    INCLUDE ([status], [attempts], [artifact_ref], [reason]);
GO

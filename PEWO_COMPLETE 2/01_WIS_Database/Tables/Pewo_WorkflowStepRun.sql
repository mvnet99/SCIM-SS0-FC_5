-- =============================================
-- PEWO: WorkflowStepRun
-- One row per step per WorkflowRun.
-- Resume logic: status = COMPLETED -> skip, FAILED or NULL -> execute.
-- Artifact_Ref stores blob path or inline scalar JSON for context handoff between steps and across retries.
-- 
-- Step_Kind validated via FK to Pewo_StepKind, same pattern as Pewo_WorkflowStepDef
-- usp_Pewo_UpsertStepRun auto-registers any new Step_Kind into Pewo_StepKind before the upsert.
-- =============================================
CREATE TABLE [dbo].[Pewo_WorkflowStepRun] (
    [id_WorkflowStepRun]   INT            IDENTITY (1, 1) NOT NULL,
    [id_WorkflowRun]       INT            NOT NULL,
    [id_WorkflowStepDef]   INT            NOT NULL,
    [Step_Kind]            VARCHAR (50)   NOT NULL,
    [Status]               VARCHAR (20)   CONSTRAINT [df_Pewo_WorkflowStepRun_Status] DEFAULT ('PENDING') NOT NULL,
    [Attempts]             SMALLINT       CONSTRAINT [df_Pewo_WorkflowStepRun_Attempts] DEFAULT ((0)) NOT NULL,
    [Artifact_Ref]         NVARCHAR (500) NULL,
    [Failure_Details]      NVARCHAR (MAX) NULL,
    [Start_Time]           DATETIME       NULL,
    [End_Time]             DATETIME       NULL,
    [created_date]         DATETIME       NULL,
    [last_updated_date]    DATETIME       NULL,
    [created_by]           INT            NULL,
    [updated_by]           INT            NULL,
    CONSTRAINT [PK_Pewo_WorkflowStepRun] PRIMARY KEY CLUSTERED ([id_WorkflowStepRun] ASC),
    CONSTRAINT [UQ_Pewo_WorkflowStepRun_Run_Step] UNIQUE ([id_WorkflowRun], [id_WorkflowStepDef]),
    CONSTRAINT [CK_Pewo_WorkflowStepRun_Status] CHECK ([Status] IN ('PENDING', 'RUNNING', 'COMPLETED', 'FAILED')),
    CONSTRAINT [FK_Pewo_WorkflowStepRun_WorkflowRun] FOREIGN KEY ([id_WorkflowRun])
        REFERENCES [dbo].[Pewo_WorkflowRun] ([id_WorkflowRun]),
    CONSTRAINT [FK_Pewo_WorkflowStepRun_WorkflowStepDef] FOREIGN KEY ([id_WorkflowStepDef])
        REFERENCES [dbo].[Pewo_WorkflowStepDef] ([id_WorkflowStepDef]),
    CONSTRAINT [FK_Pewo_WorkflowStepRun_StepKind] FOREIGN KEY ([Step_Kind])
        REFERENCES [dbo].[Pewo_StepKind] ([Step_Kind]),
    CONSTRAINT [FK_Pewo_WorkflowStepRun_Created_By] FOREIGN KEY ([created_by])
        REFERENCES [dbo].[Corporate_User] ([id_User]),
    CONSTRAINT [FK_Pewo_WorkflowStepRun_Updated_By] FOREIGN KEY ([updated_by])
        REFERENCES [dbo].[Corporate_User] ([id_User])
);
GO

CREATE INDEX [IX_Pewo_WorkflowStepRun_Resume]
    ON [dbo].[Pewo_WorkflowStepRun] ([id_WorkflowRun], [id_WorkflowStepDef])
    INCLUDE ([Status], [Attempts], [Artifact_Ref], [Failure_Details]);
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Auto incremented primary key column', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowStepRun', @level2type = N'COLUMN', @level2name = N'id_WorkflowStepRun';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Blob path or inline scalar JSON written by completed step for downstream context handoff on retry. Max 500 chars nvarchar(500)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowStepRun', @level2type = N'COLUMN', @level2name = N'Artifact_Ref';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Step status varchar(20): PENDING, RUNNING, COMPLETED, FAILED', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowStepRun', @level2type = N'COLUMN', @level2name = N'Status';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Details of failure if step failed nvarchar(max)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowStepRun', @level2type = N'COLUMN', @level2name = N'Failure_Details';
GO
-- =============================================
-- PEWO: WorkflowRun
-- One row per schedule fire. Schedule-driven, not event-driven.
-- Event/store context lives in WorkflowRunEvent (metadata only).
-- Retry fields drive auto-retry on next cron wake.
-- Batch_Key groups runs created together by a single fan-out pass (date string yyyy-MM-dd, e.g. '2026-06-15'). NULL for non-fan-out workflow types. Lets the EMAIL_SUMMARY step find "today's batch".
-- =============================================
CREATE TABLE [dbo].[Pewo_WorkflowRun] (
    [id_WorkflowRun]           INT           IDENTITY (1, 1) NOT NULL,
    [id_Schedule]              INT           NOT NULL,
    [id_CustomerWorkflowType]  INT           NOT NULL,
    [Status]                   VARCHAR (20)  CONSTRAINT [df_Pewo_WorkflowRun_Status] DEFAULT ('PENDING') NOT NULL,
    [Reason]                   NVARCHAR (MAX) NULL,
    [Retry_At]                 DATETIME      NULL,
    [Retry_Count]              SMALLINT      CONSTRAINT [df_Pewo_WorkflowRun_Retry_Count] DEFAULT ((0)) NOT NULL,
    [Max_Retries]              SMALLINT      CONSTRAINT [df_Pewo_WorkflowRun_Max_Retries] DEFAULT ((3)) NOT NULL,
    [Batch_Key]                NVARCHAR (50) NULL,
    [Started_At]               DATETIME      NULL,
    [Finished_At]              DATETIME      NULL,
    [created_date]             DATETIME      NULL,
    [last_updated_date]        DATETIME      NULL,
    [created_by]               INT           NULL,
    [updated_by]               INT           NULL,
    CONSTRAINT [PK_Pewo_WorkflowRun] PRIMARY KEY CLUSTERED ([id_WorkflowRun] ASC),
    CONSTRAINT [CK_Pewo_WorkflowRun_Status] CHECK ([Status] IN ('PENDING', 'RUNNING', 'COMPLETED', 'FAILED')),
    CONSTRAINT [FK_Pewo_WorkflowRun_Schedule] FOREIGN KEY ([id_Schedule])
        REFERENCES [dbo].[Pewo_Schedule] ([id_Schedule]),
    CONSTRAINT [FK_Pewo_WorkflowRun_CustomerWorkflowType] FOREIGN KEY ([id_CustomerWorkflowType])
        REFERENCES [dbo].[Pewo_CustomerWorkflowType] ([id_CustomerWorkflowType]),
    CONSTRAINT [FK_Pewo_WorkflowRun_Created_By] FOREIGN KEY ([created_by])
        REFERENCES [dbo].[Corporate_User] ([id_User]),
    CONSTRAINT [FK_Pewo_WorkflowRun_Updated_By] FOREIGN KEY ([updated_by])
        REFERENCES [dbo].[Corporate_User] ([id_User])
);
GO

CREATE INDEX [IX_Pewo_WorkflowRun_Retry]
    ON [dbo].[Pewo_WorkflowRun] ([Status], [Retry_At])
    WHERE [Status] = 'FAILED';
GO

CREATE INDEX [IX_Pewo_WorkflowRun_BatchKey]
    ON [dbo].[Pewo_WorkflowRun] ([id_CustomerWorkflowType], [Batch_Key])
    WHERE [Batch_Key] IS NOT NULL;
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Auto incremented primary key column', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRun', @level2type = N'COLUMN', @level2name = N'id_WorkflowRun';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Run status varchar(20): PENDING, RUNNING, COMPLETED, FAILED', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRun', @level2type = N'COLUMN', @level2name = N'Status';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'UTC datetime when this failed run should be retried. NULL when completed successfully.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRun', @level2type = N'COLUMN', @level2name = N'Retry_At';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Number of retry attempts made so far, smallint default 0', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRun', @level2type = N'COLUMN', @level2name = N'Retry_Count';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Groups runs created together by a single fan-out pass (date string yyyy-MM-dd). NULL for non-fan-out workflow types.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRun', @level2type = N'COLUMN', @level2name = N'Batch_Key';
GO
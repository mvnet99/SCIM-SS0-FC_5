-- =============================================
-- PEWO: WorkflowRunLog
-- Append-only linear log. One row per significant moment.
-- Readable as a timeline per run: schedule wake, each step start/complete/fail, retry, completion.
-- event_context stores event/store pairs as JSON array for both single and multi-event runs:
--   Single:  [{"eventId":1234,"storeNo":"0421"}]
--   Multi:   [{"eventId":1234,"storeNo":"0421"},{"eventId":1235,"storeNo":"0422"}]
-- Never updated. Only inserted.
-- =============================================
CREATE TABLE [dbo].[Pewo_WorkflowRunLog] (
    [id_WorkflowRunLog]    INT            IDENTITY (1, 1) NOT NULL,
    [id_WorkflowRun]       INT            NOT NULL,
    [id_Customer]          INT            NULL,
    [Customer_Name]        NVARCHAR (255) NULL,
    [Step_Kind]            VARCHAR (50)   NULL,
    [Log_Level]            VARCHAR (10)   CONSTRAINT [df_Pewo_WorkflowRunLog_Log_Level] DEFAULT ('INFO') NOT NULL,
    [Message]              NVARCHAR (MAX) NOT NULL,
    [Event_Context]        NVARCHAR (MAX) NULL,
    [logged_date]          DATETIME       NOT NULL CONSTRAINT [df_Pewo_WorkflowRunLog_logged_date] DEFAULT (GETUTCDATE()),
    [created_by]           INT            NULL,
    CONSTRAINT [PK_Pewo_WorkflowRunLog] PRIMARY KEY CLUSTERED ([id_WorkflowRunLog] ASC),
    CONSTRAINT [CK_Pewo_WorkflowRunLog_Log_Level] CHECK ([Log_Level] IN ('INFO', 'WARN', 'ERROR')),
    CONSTRAINT [FK_Pewo_WorkflowRunLog_WorkflowRun] FOREIGN KEY ([id_WorkflowRun])
        REFERENCES [dbo].[Pewo_WorkflowRun] ([id_WorkflowRun]),
    CONSTRAINT [FK_Pewo_WorkflowRunLog_Created_By] FOREIGN KEY ([created_by])
        REFERENCES [dbo].[Corporate_User] ([id_User])
);
GO

CREATE INDEX [IX_Pewo_WorkflowRunLog_RunId]
    ON [dbo].[Pewo_WorkflowRunLog] ([id_WorkflowRun], [logged_date])
    INCLUDE ([Step_Kind], [Log_Level], [Message]);
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Auto incremented primary key column', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRunLog', @level2type = N'COLUMN', @level2name = N'id_WorkflowRunLog';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'JSON array of event and store pairs.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRunLog', @level2type = N'COLUMN', @level2name = N'Event_Context';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Log level varchar(10): INFO, WARN, ERROR', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRunLog', @level2type = N'COLUMN', @level2name = N'Log_Level';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Log message describing the workflow moment nvarchar(max)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRunLog', @level2type = N'COLUMN', @level2name = N'Message';
GO
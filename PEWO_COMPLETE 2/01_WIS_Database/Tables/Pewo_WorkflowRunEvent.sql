-- =============================================
-- PEWO: WorkflowRunEvent
-- Metadata only. One or many rows per WorkflowRun.
-- For GM_TOTALS_CHECK / GM_PRC_DELIVERY: 1 row (single event/store).
-- For MEO: N rows (one per store/event in the weekly/daily batch).
-- Does NOT influence run count or step execution.
-- id_Event is stored as plain INT — no FK — because MEO runs may not always map to a single Event row
--
-- Event_Guid, id_Store, Event_Status, Event_Scheduled_Date are captured once, at event-close time, from the real dbo.Event row 
-- These are display/audit metadata only — never used to re-decide anything later.
--
-- Event_Date holds the event's CLOSED date (dbo.Event.Scheduled_CloseTime), in sync with the original dbo.Event
-- =============================================
CREATE TABLE [dbo].[Pewo_WorkflowRunEvent] (
    [id_WorkflowRunEvent]     INT               IDENTITY (1, 1) NOT NULL,
    [id_WorkflowRun]          INT               NOT NULL,
    [id_Event]                INT               NULL,
    [id_Customer]             INT               NULL,
    [id_Store]                INT               NULL,
    [Store_No]                NVARCHAR (20)     NULL,
    [Store_Name]              NVARCHAR (200)    NULL,
    [Event_Guid]              UNIQUEIDENTIFIER  NULL,
    [Event_Status]            NVARCHAR (25)     NULL,
    [Event_Scheduled_Date]    DATETIME          NULL,
    [Event_Date]              DATETIME          NULL,
    [Metadata_Json]           NVARCHAR (MAX)    NULL,
    [created_date]            DATETIME          NULL,
    [created_by]              INT               NULL,
    CONSTRAINT [PK_Pewo_WorkflowRunEvent] PRIMARY KEY CLUSTERED ([id_WorkflowRunEvent] ASC),
    CONSTRAINT [FK_Pewo_WorkflowRunEvent_WorkflowRun] FOREIGN KEY ([id_WorkflowRun])
        REFERENCES [dbo].[Pewo_WorkflowRun] ([id_WorkflowRun]),
    CONSTRAINT [FK_Pewo_WorkflowRunEvent_Created_By] FOREIGN KEY ([created_by])
        REFERENCES [dbo].[Corporate_User] ([id_User])
);
GO

CREATE INDEX [IX_Pewo_WorkflowRunEvent_RunId]
    ON [dbo].[Pewo_WorkflowRunEvent] ([id_WorkflowRun])
    INCLUDE ([id_Event], [id_Customer], [Store_No]);
GO

CREATE UNIQUE INDEX [UQ_Pewo_WorkflowRunEvent_Run_Event]
    ON [dbo].[Pewo_WorkflowRunEvent] ([id_WorkflowRun], [id_Event])
    WHERE [id_Event] IS NOT NULL;
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Auto incremented primary key column', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRunEvent', @level2type = N'COLUMN', @level2name = N'id_WorkflowRunEvent';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Foreign key to WorkflowRun table', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRunEvent', @level2type = N'COLUMN', @level2name = N'id_WorkflowRun';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Event ID integer — no FK constraint, nullable.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRunEvent', @level2type = N'COLUMN', @level2name = N'id_Event';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Store ID, captured from dbo.Event.id_Store at event-close time.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRunEvent', @level2type = N'COLUMN', @level2name = N'id_Store';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Event guid, captured from dbo.Event.Event_Guid at event-close time. Used by TOTALS_CHECK.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRunEvent', @level2type = N'COLUMN', @level2name = N'Event_Guid';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Snapshot of dbo.Event.Status at close time. Display/audit only — never used to re-decide anything later.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRunEvent', @level2type = N'COLUMN', @level2name = N'Event_Status';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'The events originally scheduled date, from dbo.Event.Scheduled_DateTime.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRunEvent', @level2type = N'COLUMN', @level2name = N'Event_Scheduled_Date';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'The events CLOSED date, from dbo.Event.Scheduled_CloseTime. Used as the fan-out lookback window anchor for GM_PRC_DELIVERY.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRunEvent', @level2type = N'COLUMN', @level2name = N'Event_Date';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Additional free-form metadata as JSON nvarchar(max), for future use. Event_Guid and the other event detail fields have their own typed columns now and no longer use this.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_WorkflowRunEvent', @level2type = N'COLUMN', @level2name = N'Metadata_Json';
GO
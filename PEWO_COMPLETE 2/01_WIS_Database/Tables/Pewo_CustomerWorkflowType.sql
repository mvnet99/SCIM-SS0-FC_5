-- =============================================
-- PEWO: CustomerWorkflowType
-- Links a customer to a workflow type definition.
-- One customer can have multiple workflow types (TARGET GM_PRC, MEO, etc.)
-- Fan_Out_Source_WorkflowType_Code: if set, this workflow type is fan-out driven
-- NULL for normal (non-fan-out) workflow types.
-- =============================================
CREATE TABLE [dbo].[Pewo_CustomerWorkflowType] (
    [id_CustomerWorkflowType]           INT            IDENTITY (1, 1) NOT NULL,
    [id_Customer]                       INT            NOT NULL,
    [WorkflowType_Code]                 VARCHAR (50)   NOT NULL,
    [WorkflowType_Name]                 NVARCHAR (100) NOT NULL,
    [Description]                       NVARCHAR (255) NULL,
    [Max_Retries]                       SMALLINT       CONSTRAINT [df_Pewo_CustomerWorkflowType_Max_Retries] DEFAULT ((3)) NOT NULL,
    [is_Active]                         BIT            CONSTRAINT [df_Pewo_CustomerWorkflowType_is_Active] DEFAULT ((1)) NOT NULL,
    [Fan_Out_Source_WorkflowType_Code]  VARCHAR (50)   NULL,
    [Fan_Out_Lookback_Hours]            INT            NULL,
    [created_date]                      DATETIME       NULL,
    [last_updated_date]                 DATETIME       NULL,
    [created_by]                        INT            NULL,
    [updated_by]                        INT            NULL,
    CONSTRAINT [PK_Pewo_CustomerWorkflowType] PRIMARY KEY CLUSTERED ([id_CustomerWorkflowType] ASC),
    CONSTRAINT [UQ_Pewo_CustomerWorkflowType_Code] UNIQUE ([id_Customer], [WorkflowType_Code]),
    CONSTRAINT [FK_Pewo_CustomerWorkflowType_Customer] FOREIGN KEY ([id_Customer])
        REFERENCES [dbo].[Customer] ([id_Customer]),
    CONSTRAINT [FK_Pewo_CustomerWorkflowType_Created_By] FOREIGN KEY ([created_by])
        REFERENCES [dbo].[Corporate_User] ([id_User]),
    CONSTRAINT [FK_Pewo_CustomerWorkflowType_Updated_By] FOREIGN KEY ([updated_by])
        REFERENCES [dbo].[Corporate_User] ([id_User])
);
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Auto incremented primary key column', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_CustomerWorkflowType', @level2type = N'COLUMN', @level2name = N'id_CustomerWorkflowType';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Customer ID integer foreign key from Customer table', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_CustomerWorkflowType', @level2type = N'COLUMN', @level2name = N'id_Customer';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Short code identifier for the workflow type e.g. GM_TOTALS_CHECK, GM_PRC_DELIVERY, MEO_DAILY varchar(50)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_CustomerWorkflowType', @level2type = N'COLUMN', @level2name = N'WorkflowType_Code';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Display name of the workflow type nvarchar(100)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_CustomerWorkflowType', @level2type = N'COLUMN', @level2name = N'WorkflowType_Name';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Maximum number of auto retry attempts for runs of this workflow type, smallint default 3', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_CustomerWorkflowType', @level2type = N'COLUMN', @level2name = N'Max_Retries';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Indicates whether this workflow type is active, bit default 1', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_CustomerWorkflowType', @level2type = N'COLUMN', @level2name = N'is_Active';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'If set, marks this workflow type as fan-out-driven from the named source WorkflowType_Code. NULL for normal (non-fan-out) workflow types.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_CustomerWorkflowType', @level2type = N'COLUMN', @level2name = N'Fan_Out_Source_WorkflowType_Code';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Only meaningful when Fan_Out_Source_WorkflowType_Code is set. How many hours back to look for eligible events at fan-out time (e.g. 24). Configuration, not code — adjust without a deployment.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Pewo_CustomerWorkflowType', @level2type = N'COLUMN', @level2name = N'Fan_Out_Lookback_Hours';
GO
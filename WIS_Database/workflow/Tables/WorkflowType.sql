CREATE TABLE [workflow].[WorkflowType] (
    [workflow_type_id] INT             NOT NULL IDENTITY(1,1),
    [customer_id]      INT             NOT NULL,
    [workflow_code]    VARCHAR(50)     NOT NULL,
    [workflow_name]    NVARCHAR(200)   NOT NULL,
    [description]      NVARCHAR(500)   NULL,
    [max_retries]      INT             NOT NULL DEFAULT 3,
    [is_active]        BIT             NOT NULL DEFAULT 1,
    [created_at]       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    [updated_at]       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [PK_WorkflowType]          PRIMARY KEY ([workflow_type_id]),
    CONSTRAINT [FK_WorkflowType_Customer] FOREIGN KEY ([customer_id])
                                          REFERENCES [dbo].[Customer]([customer_id]),
    CONSTRAINT [UQ_WorkflowType_Code]     UNIQUE ([customer_id], [workflow_code])
);
GO

-- =============================================
-- PEWO: Add Has_Post_Event_Workflow to Customer
-- =============================================
CREATE TABLE [dbo].[Customer] (
    [id_Customer]                      INT                                                IDENTITY (1, 1) NOT NULL,
    [Name]                             NVARCHAR (255)                                     NOT NULL,
    [is_Deleted]                       BIT                                                NULL,
    [created_date]                     DATETIME                                           NULL,
    [last_updated_date]                DATETIME                                           NULL,
    [Logo]                             NVARCHAR (MAX)                                     NULL,
    [Regional_Group1_Label]            NVARCHAR (100)                                     NULL,
    [Phone_Number]                     NVARCHAR (60) MASKED WITH (FUNCTION = 'default()') NULL,
    [Email]                            NVARCHAR (60) MASKED WITH (FUNCTION = 'email()')   NULL,
    [Industry]                         NVARCHAR (60)                                      NULL,
    [Regional_Group2_Label]            NVARCHAR (100)                                     NULL,
    [Regional_Group3_Label]            NVARCHAR (100)                                     NULL,
    [Regional_Group4_Label]            NVARCHAR (100)                                     NULL,
    [Status]                           NVARCHAR (60)                                      NULL,
    [id_Address]                       INT                                                NULL,
    [id_Address_Billing]               INT                                                NULL,
    [Source_System]                    NVARCHAR (60)                                      NULL,
    [Source_Parent_Account_Id]         INT                                                NULL,
    [Source_Customer_Id]               INT                                                NULL,
    [created_by]                       INT                                                NULL,
    [updated_by]                       INT                                                NULL,
    [Default_WIS_Tags_Required]        SMALLINT                                           NULL,
    [WIS_Customer_Satisfaction_Survey] BIT                                                CONSTRAINT [df_Customer_WIS_Customer_Satisfaction_Survey] DEFAULT ((1)) NOT NULL,
    [Training_Days]                    TINYINT                                            CONSTRAINT [df_Customer_Training_Days] DEFAULT ((1)) NULL,
    [Event_Days]                       TINYINT                                            CONSTRAINT [df_Customer_Event_Days] DEFAULT ((1)) NULL,
    [Source_Customer_Number]           NVARCHAR(100)                                      CONSTRAINT [df_Customer_Source_Customer_Number] DEFAULT (('')) NOT NULL ,
    [Has_Post_Event_Workflow]          BIT                                                CONSTRAINT [df_Customer_Has_Post_Event_Workflow] DEFAULT ((0)) NOT NULL,
    CONSTRAINT [PK__Customer__5D5E9BAA545BE1B6] PRIMARY KEY CLUSTERED ([id_Customer] ASC),
    CONSTRAINT [Customer_name] UNIQUE NONCLUSTERED ([Name] ASC),
    CONSTRAINT [Customer_Address_Billing_FK] FOREIGN KEY ([id_Address_Billing]) REFERENCES [dbo].[Addresses] ([id_Address]),
    CONSTRAINT [Customer_Address_FK] FOREIGN KEY ([id_Address]) REFERENCES [dbo].[Addresses] ([id_Address]),
    CONSTRAINT [FK_Customer_Corporate_User_Created_By] FOREIGN KEY ([created_by]) REFERENCES [dbo].[Corporate_User] ([id_User]),
    CONSTRAINT [FK_Customer_Corporate_User_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [dbo].[Corporate_User] ([id_User])
);






GO

EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Auto incremented primary key column', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'id_Customer';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'customer name nvarchar(255)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'Name';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Yes/ No its datatype is bit', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'is_Deleted';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Customer created date and time', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'created_date';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Customer last updated date and time', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'last_updated_date';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'capturing logo varchar(500)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'Logo';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Regional_group1label to 4 Each label gives some names like city, State and country varchar(100)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'Regional_Group1_Label';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Regional_group1label to 4 Each label gives some names like city, State and country varchar(100)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'Regional_Group2_Label';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Regional_group1label to 4 Each label gives some names like city, State and country varchar(100)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'Regional_Group3_Label';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Regional_group1label to 4 Each label gives some names like city, State and country varchar(100)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'Regional_Group4_Label';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'customer phone number varchar(60)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'Phone_Number';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'customer email varchar', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'Email';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'customer industry varchar,Retail, C-Store, etc', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'Industry';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Customer Status varchar(60),ACTIVE, INACTIVE', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'Status';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Address id from address table foreign key integer', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'id_Address';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Address billing id from address table foreign key integer', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'id_Address_Billing';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Customer created by nvarchar(255)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'created_by';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Customer Updated by nvarchar(255)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'updated_by';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Indicates whether the customer has post event workflow enabled, bit default 0', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer', @level2type = N'COLUMN', @level2name = N'Has_Post_Event_Workflow';
GO
USE PaymentDemo;

IF OBJECT_ID('dbo.Payments','U') IS NULL
BEGIN
    CREATE TABLE dbo.Payments
    (
        Id UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT DF_Payments_Id DEFAULT (NEWSEQUENTIALID()),

        OrderId NVARCHAR(100) NOT NULL,
        Amount DECIMAL(18,2) NOT NULL,
        Currency NVARCHAR(3) NOT NULL,
        Status NVARCHAR(30) NOT NULL,
        ProviderReference NVARCHAR(100) NULL,

        CreatedAt DATETIME2 NOT NULL
            CONSTRAINT DF_Payments_CreatedAt DEFAULT (SYSUTCDATETIME()),

        UpdatedAt DATETIME2 NULL,

        CONSTRAINT PK_Payments PRIMARY KEY CLUSTERED (Id)
    );
END
GO
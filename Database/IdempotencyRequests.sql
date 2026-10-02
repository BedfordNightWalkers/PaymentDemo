USE PaymentDemo;

IF OBJECT_ID('dbo.IdempotencyRequests','U') IS NULL
BEGIN
    CREATE TABLE dbo.IdempotencyRequests
    (
        Id UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT DF_IdempotencyRequests_Id DEFAULT (NEWSEQUENTIALID()),
			
		IdempotencyKey NVARCHAR(100) NOT NULL,			
		RequestHash VARCHAR(64) NOT NULL,
		PaymentId UNIQUEIDENTIFIER NOT NULL,
		Status NVARCHAR(30) NOT NULL,
		ResponseStatusCode INT NOT NULL,
		ResponseBody NVARCHAR(MAX) NOT NULL,

        CreatedAt DATETIME2 NOT NULL
            CONSTRAINT DF_IdempotencyRequests_CreatedAt DEFAULT (SYSUTCDATETIME()),

        UpdatedAt DATETIME2 NULL,

		CONSTRAINT UC_IdempotencyRequests_IdempotencyKey UNIQUE (IdempotencyKey),
        CONSTRAINT PK_IdempotencyRequests PRIMARY KEY CLUSTERED (Id)
    );
END
GO



IF OBJECT_ID('dbo.IdempotencyRequests', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.IdempotencyRequests
    (
        Id UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT DF_IdempotencyRequests_Id DEFAULT (NEWSEQUENTIALID()),
        IdempotencyKey NVARCHAR(100) NOT NULL,
        RequestHash VARCHAR(64) NOT NULL,
        PaymentId UNIQUEIDENTIFIER NULL,
        Status NVARCHAR(30) NOT NULL,
        ResponseStatusCode INT NOT NULL,
        ResponseBody NVARCHAR(MAX) NOT NULL,
        CreatedAt DATETIME2 NOT NULL
            CONSTRAINT DF_IdempotencyRequests_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt DATETIME2 NULL,

        CONSTRAINT PK_IdempotencyRequests PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UC_IdempotencyRequests_IdempotencyKey UNIQUE (IdempotencyKey)
    );
END
GO
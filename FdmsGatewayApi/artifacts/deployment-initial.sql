IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818223548_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] uniqueidentifier NOT NULL,
        [Timestamp] datetime2 NOT NULL,
        [DeviceId] int NULL,
        [InvoiceNo] nvarchar(450) NULL,
        [Direction] nvarchar(max) NOT NULL,
        [Endpoint] nvarchar(max) NOT NULL,
        [RawRequestJson] nvarchar(max) NOT NULL,
        [RawResponseJson] nvarchar(max) NULL,
        [HttpStatusCode] int NOT NULL,
        [ErrorCategory] nvarchar(max) NOT NULL,
        [ErrorCode] nvarchar(max) NULL,
        [ErrorMessage] nvarchar(max) NULL,
        [DurationMs] bigint NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818223548_InitialCreate'
)
BEGIN
    CREATE TABLE [DeviceConfigs] (
        [DeviceId] int NOT NULL,
        [TaxPayerName] nvarchar(max) NOT NULL,
        [TaxPayerTIN] nvarchar(10) NOT NULL,
        [VatNumber] nvarchar(max) NULL,
        [DeviceSerialNo] nvarchar(max) NOT NULL,
        [DeviceBranchName] nvarchar(max) NOT NULL,
        [ActivationKey] nvarchar(max) NOT NULL,
        [DeviceModelName] nvarchar(max) NOT NULL,
        [DeviceModelVersionNo] nvarchar(max) NOT NULL,
        [DeviceOperatingMode] nvarchar(max) NOT NULL,
        [EncryptedPrivateKeyPem] nvarchar(max) NOT NULL,
        [CertificatePem] nvarchar(max) NOT NULL,
        [ServerCertificatePem] nvarchar(max) NULL,
        [QrUrl] nvarchar(max) NOT NULL,
        [CurrentFiscalDayNo] int NOT NULL,
        [CurrentReceiptGlobalNo] int NOT NULL,
        [CurrentReceiptCounter] int NOT NULL,
        [LastReceiptHash] nvarchar(max) NULL,
        [FiscalDayStatus] nvarchar(max) NOT NULL,
        [FiscalDayOpenedAt] datetime2 NULL,
        CONSTRAINT [PK_DeviceConfigs] PRIMARY KEY ([DeviceId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818223548_InitialCreate'
)
BEGIN
    CREATE TABLE [FiscalDocuments] (
        [Id] uniqueidentifier NOT NULL,
        [InvoiceNo] nvarchar(450) NOT NULL,
        [ReceiptType] nvarchar(max) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [DeviceId] int NOT NULL,
        [FiscalDayNo] int NOT NULL,
        [ReceiptCounter] int NOT NULL,
        [ReceiptGlobalNo] int NOT NULL,
        [DeviceHash] nvarchar(max) NOT NULL,
        [DeviceSignature] nvarchar(max) NOT NULL,
        [ReceiptID] bigint NULL,
        [ServerDate] datetime2 NULL,
        [FdmsSignature] nvarchar(max) NULL,
        [QrCodeUrl] nvarchar(max) NULL,
        [OperationID] nvarchar(max) NULL,
        [FiscalStatus] nvarchar(450) NOT NULL,
        [ErrorMessage] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [RawPayloadJson] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_FiscalDocuments] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818223548_InitialCreate'
)
BEGIN
    CREATE TABLE [OfflineQueue] (
        [Id] uniqueidentifier NOT NULL,
        [DeviceId] int NOT NULL,
        [InvoiceNo] nvarchar(max) NOT NULL,
        [ReceiptGlobalNo] int NOT NULL,
        [ReceiptType] nvarchar(max) NOT NULL,
        [SubmitPayloadJson] nvarchar(max) NOT NULL,
        [Status] nvarchar(450) NOT NULL,
        [RetryCount] int NOT NULL,
        [QueuedAt] datetime2 NOT NULL,
        [LastAttemptAt] datetime2 NULL,
        [ErrorMessage] nvarchar(max) NULL,
        CONSTRAINT [PK_OfflineQueue] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818223548_InitialCreate'
)
BEGIN
    CREATE TABLE [WebhookRegistrations] (
        [Id] uniqueidentifier NOT NULL,
        [WebhookUrl] nvarchar(max) NOT NULL,
        [Secret] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WebhookRegistrations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818223548_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_InvoiceNo] ON [AuditLogs] ([InvoiceNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818223548_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_Timestamp] ON [AuditLogs] ([Timestamp]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818223548_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_FiscalDocuments_FiscalStatus] ON [FiscalDocuments] ([FiscalStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818223548_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_FiscalDocuments_InvoiceNo] ON [FiscalDocuments] ([InvoiceNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818223548_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OfflineQueue_DeviceId_Status_ReceiptGlobalNo] ON [OfflineQueue] ([DeviceId], [Status], [ReceiptGlobalNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818223548_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260818223548_InitialCreate', N'9.0.2');
END;

COMMIT;
GO


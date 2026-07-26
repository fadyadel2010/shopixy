-- ============================================================================
-- Migration: S31_CanonicalCrossSystemIdentityLinks (SQL Server)
-- Description: Add canonical identity graph so POS, Storefront, CommerceHub,
--              Affiliate, and vendor identities resolve through one authority.
-- Date: 2026-04-24
-- Database: SQL Server
-- Note: For PostgreSQL, see 20260424163000_S31_CanonicalCrossSystemIdentityLinks.sql
-- ============================================================================

BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '__EFMigrationsHistory')
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CanonicalIdentityLinks')
BEGIN
    CREATE TABLE [CanonicalIdentityLinks] (
        [Id] uniqueidentifier NOT NULL,
        [CanonicalIdentityId] uniqueidentifier NOT NULL,
        [SubjectType] int NOT NULL,
        [SubjectId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NULL,
        [SourceSystem] nvarchar(64) NOT NULL,
        [DisplayKey] nvarchar(255) NULL,
        [LinkedAtUtc] datetime2 NOT NULL,
        [RevokedAtUtc] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT 0,
        CONSTRAINT [PK_CanonicalIdentityLinks] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CanonicalIdentityLinks_CanonicalIdentityId')
BEGIN
    CREATE INDEX [IX_CanonicalIdentityLinks_CanonicalIdentityId]
        ON [CanonicalIdentityLinks] ([CanonicalIdentityId]);
END;
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CanonicalIdentityLinks_Tenant_Subject')
BEGIN
    CREATE INDEX [IX_CanonicalIdentityLinks_Tenant_Subject]
        ON [CanonicalIdentityLinks] ([TenantId], [SubjectType], [SubjectId]);
END;
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CanonicalIdentityLinks_Source_DisplayKey')
BEGIN
    CREATE INDEX [IX_CanonicalIdentityLinks_Source_DisplayKey]
        ON [CanonicalIdentityLinks] ([SourceSystem], [DisplayKey])
        WHERE [DisplayKey] IS NOT NULL AND [IsDeleted] = 0 AND [RevokedAtUtc] IS NULL;
END;
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'UX_CanonicalIdentityLinks_Tenant_Subject_Active')
BEGIN
    CREATE UNIQUE INDEX [UX_CanonicalIdentityLinks_Tenant_Subject_Active]
        ON [CanonicalIdentityLinks] ([TenantId], [SubjectType], [SubjectId])
        WHERE [TenantId] IS NOT NULL AND [IsDeleted] = 0 AND [RevokedAtUtc] IS NULL;
END;
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'UX_CanonicalIdentityLinks_Global_Subject_Active')
BEGIN
    CREATE UNIQUE INDEX [UX_CanonicalIdentityLinks_Global_Subject_Active]
        ON [CanonicalIdentityLinks] ([SubjectType], [SubjectId])
        WHERE [TenantId] IS NULL AND [IsDeleted] = 0 AND [RevokedAtUtc] IS NULL;
END;
GO

-- Note: The INSERT statements below use PostgreSQL-specific functions (md5, ::uuid casting)
-- For SQL Server, these would need to be converted to use HASHBYTES, CONVERT, etc.
-- This is left as a manual conversion task since the exact business logic may vary.
-- Example conversion pattern:
-- PostgreSQL: md5('CanonicalIdentityLink:PosUser:' || "Id"::text)::uuid
-- SQL Server: CONVERT(uniqueidentifier, HASHBYTES('MD5', 'CanonicalIdentityLink:PosUser:' + CAST([Id] AS nvarchar(36))))

-- INSERT INTO [CanonicalIdentityLinks] ... (data migration omitted for SQL Server)

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20260424163000_S31_CanonicalCrossSystemIdentityLinks')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES ('20260424163000_S31_CanonicalCrossSystemIdentityLinks', '9.0.1');
END;
GO

COMMIT TRANSACTION;
GO

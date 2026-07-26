-- =====================================================================================
-- Migration: S26_PaymentCancellationOutbox
-- Description: Add PaymentCancellationOutboxMessages table for durable payment cancellation
-- Phase: Payment Cancellation Hardening
-- Date: 2026-04-24
-- =====================================================================================

-- Create PaymentCancellationOutboxMessages table
CREATE TABLE PaymentCancellationOutboxMessages (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    TenantId UNIQUEIDENTIFIER NOT NULL,
    OrderId UNIQUEIDENTIFIER NOT NULL,
    PaymentIntentId UNIQUEIDENTIFIER NULL,
    Reason NVARCHAR(500) NOT NULL,
    Actor NVARCHAR(100) NOT NULL,
    ProcessedAtUtc DATETIME2 NULL,
    AttemptCount INT NOT NULL DEFAULT 0,
    LastError NVARCHAR(2000) NULL,
    NextRetryAtUtc DATETIME2 NULL,
    IsFailed BIT NOT NULL DEFAULT 0,
    CorrelationId NVARCHAR(100) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    IsDeleted BIT NOT NULL DEFAULT 0
);

-- Index for efficient polling by background worker
CREATE NONCLUSTERED INDEX IX_PaymentCancellationOutbox_Processing
ON PaymentCancellationOutboxMessages (ProcessedAtUtc, IsFailed, NextRetryAtUtc)
INCLUDE (Id, TenantId, OrderId, PaymentIntentId, CreatedAt);

-- Index for tenant isolation
CREATE NONCLUSTERED INDEX IX_PaymentCancellationOutbox_TenantId
ON PaymentCancellationOutboxMessages (TenantId);

-- Index for order lookup
CREATE NONCLUSTERED INDEX IX_PaymentCancellationOutbox_OrderId
ON PaymentCancellationOutboxMessages (OrderId);

GO

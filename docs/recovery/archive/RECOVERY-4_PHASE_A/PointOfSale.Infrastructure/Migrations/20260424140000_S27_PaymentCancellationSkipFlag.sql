-- Migration: S27_PaymentCancellationSkipFlag
-- Phase: Payment Cancellation Hardening
-- Purpose: Add SkipPaymentIntentResolution flag to PaymentCancellationOutboxMessage
--
-- Context:
-- The outbox processor needs to persist whether a cancellation is "order-only"
-- (no payment intents exist) so it can correctly set SkipPaymentIntentResolution
-- when calling IPaymentLifecycleService.HandlePaymentResultAsync.
--
-- Changes:
-- 1. Add SkipPaymentIntentResolution column (defaults to false for existing rows)
--
-- Rollback:
-- DROP COLUMN PaymentCancellationOutboxMessages.SkipPaymentIntentResolution

BEGIN TRANSACTION;

-- Add SkipPaymentIntentResolution column
ALTER TABLE PaymentCancellationOutboxMessages
ADD SkipPaymentIntentResolution BIT NOT NULL DEFAULT 0;

-- Update existing rows where PaymentIntentId is NULL to set SkipPaymentIntentResolution = true
-- This handles any existing "order-only" cancellation messages
UPDATE PaymentCancellationOutboxMessages
SET SkipPaymentIntentResolution = 1
WHERE PaymentIntentId IS NULL;

COMMIT TRANSACTION;

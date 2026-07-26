-- Migration: Add Manual Wallet Fields to PaymentIntents table
-- Created: 2026-01-25
-- Description: Adds columns for manual wallet payment verification

ALTER TABLE "PaymentIntents"
ADD COLUMN "WalletType" text NULL,
ADD COLUMN "WalletPhoneNumber" text NULL,
ADD COLUMN "ProofImageUrl" text NULL,
ADD COLUMN "VerifiedAtUtc" timestamp with time zone NULL,
ADD COLUMN "VerifiedByAdmin" text NULL,
ADD COLUMN "RejectionReason" text NULL;

-- Migration: Add WhatsApp Payment Approval Feature
-- Date: 2026-02-03
-- Description: Adds UseForPaymentApproval flag to WhatsAppAccount entity
--              to enable WhatsApp notifications for manual payment approvals

-- Add UseForPaymentApproval column to whatsapp_accounts table
ALTER TABLE whatsapp_accounts 
ADD COLUMN IF NOT EXISTS use_for_payment_approval BOOLEAN NOT NULL DEFAULT FALSE;

-- Add comment for documentation
COMMENT ON COLUMN whatsapp_accounts.use_for_payment_approval IS 
'Whether this WhatsApp account is used for manual payment approval notifications';

-- Create index for efficient querying of payment approval accounts
CREATE INDEX IF NOT EXISTS ix_whatsapp_accounts_payment_approval 
ON whatsapp_accounts(tenant_id, use_for_payment_approval) 
WHERE enabled = TRUE AND use_for_payment_approval = TRUE;

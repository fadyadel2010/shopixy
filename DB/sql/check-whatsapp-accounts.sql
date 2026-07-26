-- Check WhatsApp accounts and enable payment approval
SELECT 
    id, 
    tenant_id, 
    phone_number, 
    display_name, 
    status, 
    use_for_otp_verification, 
    use_for_order_confirmation, 
    use_for_payment_approval, 
    enabled 
FROM whatsapp_accounts 
WHERE "IsDeleted" = false 
LIMIT 5;

-- Enable payment approval for all active accounts
UPDATE whatsapp_accounts 
SET use_for_payment_approval = true 
WHERE "IsDeleted" = false 
AND enabled = true;

-- Verify update
SELECT 
    id, 
    phone_number, 
    use_for_payment_approval 
FROM whatsapp_accounts 
WHERE "IsDeleted" = false;

-- WhatsApp Payment Approval Setup Verification Script
-- Run this to verify the database is properly configured

PRINT '========================================';
PRINT 'WhatsApp Payment Approval Setup Check';
PRINT '========================================';
PRINT '';

-- 1. Check if UseForPaymentApproval column exists
PRINT '1. Checking WhatsAppAccounts table schema...';
IF EXISTS (
    SELECT 1 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'WhatsAppAccounts' 
    AND COLUMN_NAME = 'UseForPaymentApproval'
)
BEGIN
    PRINT '   ✓ UseForPaymentApproval column exists';
END
ELSE
BEGIN
    PRINT '   ✗ UseForPaymentApproval column MISSING!';
    PRINT '   → Run migration: migrations/20260203_AddWhatsAppPaymentApproval.sql';
END
PRINT '';

-- 2. Check WhatsApp accounts configured for payment approval
PRINT '2. Checking WhatsApp accounts with payment approval enabled...';
SELECT 
    Id,
    TenantId,
    PhoneNumber,
    DisplayName,
    Status,
    UseForOtpVerification,
    UseForOrderConfirmation,
    UseForPaymentApproval,
    IsActive,
    CreatedAt
FROM WhatsAppAccounts
WHERE IsDeleted = 0;

DECLARE @paymentApprovalCount INT;
SELECT @paymentApprovalCount = COUNT(*) 
FROM WhatsAppAccounts 
WHERE UseForPaymentApproval = 1 AND IsDeleted = 0 AND IsActive = 1;

IF @paymentApprovalCount > 0
BEGIN
    PRINT '';
    PRINT '   ✓ Found ' + CAST(@paymentApprovalCount AS VARCHAR) + ' active account(s) with payment approval enabled';
END
ELSE
BEGIN
    PRINT '';
    PRINT '   ⚠ No active accounts with payment approval enabled!';
    PRINT '   → Enable payment approval for at least one WhatsApp account';
    PRINT '   → UPDATE WhatsAppAccounts SET UseForPaymentApproval = 1 WHERE Id = ''<account-id>''';
END
PRINT '';

-- 3. Check recent manual wallet payments
PRINT '3. Checking recent manual wallet payment intents...';
SELECT TOP 10
    pi.Id AS PaymentIntentId,
    pi.OrderId,
    o.OrderNumber,
    o.GuestPhone,
    o.GuestName,
    pi.Status,
    pi.WalletType,
    pi.ProofImageUrl,
    pi.RejectionReason,
    pi.CreatedAt,
    pi.VerifiedAtUtc
FROM PaymentIntents pi
INNER JOIN Orders o ON pi.OrderId = o.Id
WHERE pi.Provider = 'ManualWallet'
ORDER BY pi.CreatedAt DESC;

PRINT '';
PRINT '4. Checking orders with missing GuestPhone...';
SELECT COUNT(*) AS OrdersWithoutPhone
FROM Orders o
INNER JOIN PaymentIntents pi ON o.Id = pi.OrderId
WHERE pi.Provider = 'ManualWallet'
AND (o.GuestPhone IS NULL OR o.GuestPhone = '');

PRINT '';
PRINT '========================================';
PRINT 'Setup Verification Complete';
PRINT '========================================';
PRINT '';
PRINT 'Next Steps:';
PRINT '1. If UseForPaymentApproval column is missing, run the migration';
PRINT '2. Enable payment approval for at least one WhatsApp account';
PRINT '3. Ensure orders have GuestPhone populated';
PRINT '4. Restart the API to register consumers';
PRINT '5. Test payment rejection with an order that has a valid phone number';

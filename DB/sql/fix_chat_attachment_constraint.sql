-- Fix ServiceRequestMessages constraint to allow empty body when there's an attachment
-- This allows users to send attachment-only messages in chat

-- Drop the old constraint that requires non-empty body
ALTER TABLE "ServiceRequestMessages" 
DROP CONSTRAINT IF EXISTS "CHK_ServiceRequestMessages_Body";

-- Add new constraint: body can be empty only if there's an attachment
ALTER TABLE "ServiceRequestMessages"
ADD CONSTRAINT "CHK_ServiceRequestMessages_Body" 
CHECK (
    (LENGTH(TRIM("Body")) > 0) OR 
    ("AttachmentUrl" IS NOT NULL AND LENGTH(TRIM("AttachmentUrl")) > 0)
);

-- Verify the constraint
SELECT conname, pg_get_constraintdef(oid) 
FROM pg_constraint 
WHERE conname = 'CHK_ServiceRequestMessages_Body';

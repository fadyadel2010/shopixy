-- =====================================================================================
-- Script: Redact PII from Existing ShippingWebhookLogs
-- Purpose: Remove PII from historical webhook logs to comply with data retention policy
-- Database: PostgreSQL
-- 
-- IMPORTANT: Review and test this script in a non-production environment first!
-- =====================================================================================

DO $$
BEGIN
    -- Check if table exists before proceeding
    IF NOT EXISTS (SELECT FROM pg_tables WHERE schemaname = 'public' AND tablename = 'ShippingWebhookLogs') THEN
        RAISE NOTICE 'Table ShippingWebhookLogs does not exist. Skipping redaction script.';
        RETURN;
    END IF;

    -- Option 1: Delete old webhook logs beyond retention policy (recommended)
    -- Keeps only last 90 days of webhook logs
    DELETE FROM "ShippingWebhookLogs"
    WHERE "CreatedAt" < NOW() - INTERVAL '90 days'
      AND "IsDeleted" = false;

    RAISE NOTICE 'Deleted webhook logs older than 90 days';

END $$;

-- Option 2: Redact PII from remaining logs (if you need to keep them for compliance)

-- Helper function to redact common PII patterns from text
CREATE OR REPLACE FUNCTION redact_pii(input_text TEXT) 
RETURNS TEXT AS $$
BEGIN
    IF input_text IS NULL OR input_text = '' THEN
        RETURN input_text;
    END IF;
    
    -- Redact email addresses
    input_text := REGEXP_REPLACE(input_text, '\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b', '[EMAIL_REDACTED]', 'g');
    
    -- Redact phone numbers (various formats: +201234567890, 01234567890, (123) 456-7890, etc.)
    input_text := REGEXP_REPLACE(input_text, '\+?[\d\s\-\(\)]{10,}', '[PHONE_REDACTED]', 'g');
    
    -- Redact potential addresses (street numbers + street names)
    input_text := REGEXP_REPLACE(input_text, '\b\d+\s+[A-Za-z\s]+(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Lane|Ln|Drive|Dr)\b', '[ADDRESS_REDACTED]', 'gi');
    
    -- Redact common PII field values in JSON (name, address, phone, email fields)
    input_text := REGEXP_REPLACE(input_text, '("(?:name|firstName|lastName|fullName|customerName|recipient)"\s*:\s*")[^"]*(")', '\1[NAME_REDACTED]\2', 'gi');
    input_text := REGEXP_REPLACE(input_text, '("(?:phone|mobile|telephone|phoneNumber)"\s*:\s*")[^"]*(")', '\1[PHONE_REDACTED]\2', 'gi');
    input_text := REGEXP_REPLACE(input_text, '("(?:email|emailAddress)"\s*:\s*")[^"]*(")', '\1[EMAIL_REDACTED]\2', 'gi');
    input_text := REGEXP_REPLACE(input_text, '("(?:address|street|city|state|zip|postal|postcode)"\s*:\s*")[^"]*(")', '\1[ADDRESS_REDACTED]\2', 'gi');
    
    RETURN input_text;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

DO $$
BEGIN
    -- Check if table exists before proceeding with updates
    IF NOT EXISTS (SELECT FROM pg_tables WHERE schemaname = 'public' AND tablename = 'ShippingWebhookLogs') THEN
        RETURN;
    END IF;

    -- Truncate and redact PayloadSummary (idempotent - only processes rows not already truncated)
    UPDATE "ShippingWebhookLogs"
    SET "PayloadSummary" = LEFT(redact_pii("PayloadSummary"), 500) || '...[truncated for retention policy]'
    WHERE LENGTH("PayloadSummary") > 500
      AND "PayloadSummary" NOT LIKE '%...[truncated for retention policy]'
      AND "IsDeleted" = false;

    RAISE NOTICE 'Truncated and redacted long PayloadSummary values';

    -- Redact PII from PayloadSummary that's under 500 chars but still contains PII
    UPDATE "ShippingWebhookLogs"
    SET "PayloadSummary" = redact_pii("PayloadSummary")
    WHERE LENGTH("PayloadSummary") > 0
      AND LENGTH("PayloadSummary") <= 500
      AND "PayloadSummary" NOT LIKE '%[REDACTED]%'
      AND "IsDeleted" = false;

    RAISE NOTICE 'Redacted PII from short PayloadSummary values';

    -- Redact SourceIpPrefix to network prefix only (idempotent - skips already redacted)
    UPDATE "ShippingWebhookLogs"
    SET "SourceIpPrefix" = 
        CASE 
            -- IPv4: Keep first 3 octets, zero out last octet
            WHEN "SourceIpPrefix" ~ '^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$' 
            THEN REGEXP_REPLACE("SourceIpPrefix", '^(\d{1,3}\.\d{1,3}\.\d{1,3}\.)\d{1,3}$', '\10/24')
            -- Otherwise redact completely
            ELSE '[IP_REDACTED]'
        END
    WHERE "SourceIpPrefix" IS NOT NULL
      AND "SourceIpPrefix" NOT LIKE '%/24'
      AND "SourceIpPrefix" NOT LIKE '%/64'
      AND "SourceIpPrefix" != '[IP_REDACTED]'
      AND "IsDeleted" = false;

    RAISE NOTICE 'Redacted SourceIpPrefix values';

    -- Truncate UserAgentSummary to 100 characters (idempotent - only processes rows not already truncated)
    UPDATE "ShippingWebhookLogs"
    SET "UserAgentSummary" = LEFT("UserAgentSummary", 100) || '...'
    WHERE LENGTH("UserAgentSummary") > 100
      AND "UserAgentSummary" NOT LIKE '%...'
      AND "IsDeleted" = false;

    RAISE NOTICE 'Truncated UserAgentSummary values';

    -- Option 3: For maximum security, clear all PII fields from old records
    -- Uncomment if you want to completely remove PII from logs older than 30 days
    /*
    UPDATE "ShippingWebhookLogs"
    SET "PayloadSummary" = '[REDACTED - retention policy]',
        "SourceIpPrefix" = NULL,
        "UserAgentSummary" = NULL
    WHERE "CreatedAt" < NOW() - INTERVAL '30 days'
      AND "IsDeleted" = false;

    RAISE NOTICE 'Cleared PII from webhook logs older than 30 days';
    */

END $$;

-- Verify results (only if table exists)
DO $$
DECLARE
    result_record RECORD;
BEGIN
    IF NOT EXISTS (SELECT FROM pg_tables WHERE schemaname = 'public' AND tablename = 'ShippingWebhookLogs') THEN
        RETURN;
    END IF;

    SELECT 
        COUNT(*) AS total_logs,
        COUNT(CASE WHEN "PayloadSummary" LIKE '%[%_REDACTED]%' OR "PayloadSummary" LIKE '%[REDACTED]%' THEN 1 END) AS redacted_payloads,
        COUNT(CASE WHEN "SourceIpPrefix" LIKE '%/24' OR "SourceIpPrefix" LIKE '%/64' OR "SourceIpPrefix" = '[IP_REDACTED]' THEN 1 END) AS redacted_ips,
        COUNT(CASE WHEN LENGTH("UserAgentSummary") <= 103 THEN 1 END) AS truncated_user_agents,
        MIN("CreatedAt") AS oldest_log,
        MAX("CreatedAt") AS newest_log
    INTO result_record
    FROM "ShippingWebhookLogs"
    WHERE "IsDeleted" = false;

    RAISE NOTICE 'Verification Results: Total Logs=%, Redacted Payloads=%, Redacted IPs=%, Truncated User Agents=%, Oldest=%, Newest=%',
        result_record.total_logs,
        result_record.redacted_payloads,
        result_record.redacted_ips,
        result_record.truncated_user_agents,
        result_record.oldest_log,
        result_record.newest_log;
END $$;

-- Clean up the helper function (optional - comment out if you want to keep it for future use)
-- DROP FUNCTION IF EXISTS redact_pii(TEXT);

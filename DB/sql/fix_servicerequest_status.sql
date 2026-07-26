-- Fix ServiceRequestStatus type mismatch with ROBUST DYNAMIC DEPENDENCY CLEANUP
-- This script automatically finds and drops all triggers, indexes, and constraints on the Status column.

DO $$ 
DECLARE 
    r RECORD;
    table_oid OID;
    att_num INT;
BEGIN 
    -- Get the OID of the table to handle case sensitivity correctly
    SELECT oid INTO table_oid FROM pg_class WHERE relname = 'ServiceRequests';
    
    IF table_oid IS NULL THEN
        RAISE NOTICE 'Table "ServiceRequests" not found. Skipping...';
        RETURN;
    END IF;

    -- Get the attribute number for 'Status' column
    SELECT attnum INTO att_num FROM pg_attribute WHERE attrelid = table_oid AND attname = 'Status';

    -- 1. Drop ALL triggers on the table
    FOR r IN (SELECT trigger_name FROM information_schema.triggers WHERE event_object_table = 'ServiceRequests') LOOP
        EXECUTE 'DROP TRIGGER IF EXISTS ' || quote_ident(r.trigger_name) || ' ON "ServiceRequests" CASCADE';
    END LOOP;

    -- 2. Drop ALL indexes that use the 'Status' column
    FOR r IN (
        SELECT i.relname as index_name
        FROM pg_index ix
        JOIN pg_class i ON i.oid = ix.indexrelid
        WHERE ix.indrelid = table_oid
        AND att_num = ANY(ix.indkey)
    ) LOOP
        EXECUTE 'DROP INDEX IF EXISTS ' || quote_ident(r.index_name) || ' CASCADE';
    END LOOP;
    
    -- 3. Drop ALL constraints (Check, FK, etc) on 'Status' column
    FOR r IN (
        SELECT conname 
        FROM pg_constraint 
        WHERE conrelid = table_oid
        AND conkey @> ARRAY[att_num]::smallint[]
    ) LOOP
        EXECUTE 'ALTER TABLE "ServiceRequests" DROP CONSTRAINT IF EXISTS ' || quote_ident(r.conname) || ' CASCADE';
    END LOOP;
END $$;

-- 4. Remove Default Value to avoid type conflict
ALTER TABLE "ServiceRequests" ALTER COLUMN "Status" DROP DEFAULT;

-- 5. Convert to TEXT using explicit cast
ALTER TABLE "ServiceRequests" ALTER COLUMN "Status" TYPE text USING "Status"::text;

-- 6. Cleanup the custom Type and any remaining dependents
DROP TYPE IF EXISTS "ServiceRequestStatus" CASCADE;

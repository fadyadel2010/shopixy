DO $$ 
DECLARE 
    r RECORD;
    table_oid OID;
    att_num_role INT2;
    att_num_type INT2;
BEGIN 
    -- Get the confirmed OID of the table
    SELECT oid INTO table_oid FROM pg_class WHERE relname = 'ServiceRequestMessages';
    
    IF table_oid IS NULL THEN
        RAISE NOTICE 'Table "ServiceRequestMessages" not found.';
        RETURN;
    END IF;

    -- Get attribute number for 'SenderRole'
    SELECT attnum INTO att_num_role FROM pg_attribute WHERE attrelid = table_oid AND attname = 'SenderRole';
    -- Get attribute number for 'MessageType'
    SELECT attnum INTO att_num_type FROM pg_attribute WHERE attrelid = table_oid AND attname = 'MessageType';

    -- Drop constraints on SenderRole
    IF att_num_role IS NOT NULL THEN
        FOR r IN (
            SELECT conname 
            FROM pg_constraint 
            WHERE conrelid = table_oid
            AND conkey @> ARRAY[att_num_role]
        ) LOOP
            EXECUTE 'ALTER TABLE "ServiceRequestMessages" DROP CONSTRAINT ' || quote_ident(r.conname) || ' CASCADE';
        END LOOP;
    END IF;

    -- Drop constraints on MessageType
    IF att_num_type IS NOT NULL THEN
        FOR r IN (
            SELECT conname 
            FROM pg_constraint 
            WHERE conrelid = table_oid
            AND conkey @> ARRAY[att_num_type]
        ) LOOP
            EXECUTE 'ALTER TABLE "ServiceRequestMessages" DROP CONSTRAINT ' || quote_ident(r.conname) || ' CASCADE';
        END LOOP;
    END IF;

    -- Drop indexes using these columns (if any)
    FOR r IN (
        SELECT i.relname as index_name
        FROM pg_index ix
        JOIN pg_class i ON i.oid = ix.indexrelid
        WHERE ix.indrelid = table_oid
        AND (
            (att_num_role IS NOT NULL AND att_num_role = ANY(ix.indkey)) OR
            (att_num_type IS NOT NULL AND att_num_type = ANY(ix.indkey))
        )
    ) LOOP
        EXECUTE 'DROP INDEX IF EXISTS ' || quote_ident(r.index_name) || ' CASCADE';
    END LOOP;

END $$;

-- Drop Default Values
ALTER TABLE "ServiceRequestMessages" ALTER COLUMN "SenderRole" DROP DEFAULT;
ALTER TABLE "ServiceRequestMessages" ALTER COLUMN "MessageType" DROP DEFAULT;

-- Convert to TEXT
ALTER TABLE "ServiceRequestMessages" ALTER COLUMN "SenderRole" TYPE text USING "SenderRole"::text;
ALTER TABLE "ServiceRequestMessages" ALTER COLUMN "MessageType" TYPE text USING "MessageType"::text;

-- Cleanup Custom Enum Types
DROP TYPE IF EXISTS "SenderRole" CASCADE;
DROP TYPE IF EXISTS "MessageType" CASCADE;

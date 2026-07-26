-- S51: Fix schema drift in CommerceHubPaymentCompensationOutboxMessages.AffectedStoreIdsJson
-- The model configuration specifies 'text' but the S46 migration created it as 'character varying(2000)'.
-- This migration aligns the database schema with the model configuration.

ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages" 
    ALTER COLUMN "AffectedStoreIdsJson" TYPE text;

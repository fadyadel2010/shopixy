ALTER TABLE "ProductVariants"
ADD COLUMN IF NOT EXISTS "MediaId" uuid NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_ProductVariant_ProductMedia'
    ) THEN
        ALTER TABLE "ProductVariants"
        ADD CONSTRAINT "FK_ProductVariant_ProductMedia"
            FOREIGN KEY ("MediaId")
            REFERENCES "ProductMedia" ("Id")
            ON DELETE SET NULL;
    END IF;
END $$;

ALTER TABLE "Products"
ADD COLUMN IF NOT EXISTS "ProductTemplate" character varying(100) NULL;

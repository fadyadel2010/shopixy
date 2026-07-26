BEGIN;

-- ============================================================================
-- CommerceHub Global Categories + Attribute Flags
-- Creates the shared category taxonomy used by:
--   - /api/commercehub/categories/tree
--   - /api/commercehub/categories/{id}/attributes
--   - CommerceHub publish/update validation
--   - CommerceHub Mongo projection category metadata resolution
--
-- Safe to run multiple times from PgAdmin.
-- ============================================================================

CREATE TABLE IF NOT EXISTS "CommerceHubCategories"
(
    "Id" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "NameAr" character varying(200) NULL,
    "Slug" character varying(200) NOT NULL,
    "ParentId" uuid NULL,
    "Level" integer NOT NULL DEFAULT 0,
    "IconEmoji" character varying(10) NULL,
    "IsActive" boolean NOT NULL DEFAULT true,
    "SortOrder" integer NOT NULL DEFAULT 0,
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAtUtc" timestamp with time zone NULL,
    CONSTRAINT "PK_CommerceHubCategories" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "CommerceHubCategoryAttributes"
(
    "Id" uuid NOT NULL,
    "CategoryId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "NameAr" character varying(200) NULL,
    "Type" character varying(20) NOT NULL,
    "IsRequired" boolean NOT NULL DEFAULT false,
    "IsFilterable" boolean NOT NULL DEFAULT true,
    "IsSearchable" boolean NOT NULL DEFAULT true,
    "OptionsJson" text NULL,
    "SortOrder" integer NOT NULL DEFAULT 0,
    CONSTRAINT "PK_CommerceHubCategoryAttributes" PRIMARY KEY ("Id")
);

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "Name" character varying(200) NOT NULL DEFAULT '';

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "NameAr" character varying(200) NULL;

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "Slug" character varying(200) NOT NULL DEFAULT '';

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "ParentId" uuid NULL;

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "Level" integer NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "IconEmoji" character varying(10) NULL;

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT true;

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "SortOrder" integer NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW();

ALTER TABLE IF EXISTS "CommerceHubCategories"
    ADD COLUMN IF NOT EXISTS "UpdatedAtUtc" timestamp with time zone NULL;

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "CategoryId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "Name" character varying(200) NOT NULL DEFAULT '';

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "NameAr" character varying(200) NULL;

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "Type" character varying(20) NOT NULL DEFAULT 'text';

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "IsRequired" boolean NOT NULL DEFAULT false;

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "IsFilterable" boolean NOT NULL DEFAULT true;

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "IsSearchable" boolean NOT NULL DEFAULT true;

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "OptionsJson" text NULL;

ALTER TABLE IF EXISTS "CommerceHubCategoryAttributes"
    ADD COLUMN IF NOT EXISTS "SortOrder" integer NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubCategories_CommerceHubCategories_ParentId')
    THEN
        ALTER TABLE "CommerceHubCategories"
            ADD CONSTRAINT "FK_CommerceHubCategories_CommerceHubCategories_ParentId"
            FOREIGN KEY ("ParentId")
            REFERENCES "CommerceHubCategories" ("Id")
            ON DELETE RESTRICT;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubCategoryAttributes_CommerceHubCategories_CategoryId')
    THEN
        ALTER TABLE "CommerceHubCategoryAttributes"
            ADD CONSTRAINT "FK_CommerceHubCategoryAttributes_CommerceHubCategories_CategoryId"
            FOREIGN KEY ("CategoryId")
            REFERENCES "CommerceHubCategories" ("Id")
            ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubCategories_ParentId_SortOrder"
    ON "CommerceHubCategories" ("ParentId", "SortOrder");

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CommerceHubCategories_Slug"
    ON "CommerceHubCategories" ("Slug");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubCategoryAttributes_CategoryId_SortOrder"
    ON "CommerceHubCategoryAttributes" ("CategoryId", "SortOrder");

INSERT INTO "CommerceHubCategories" ("Id", "Name", "NameAr", "Slug", "ParentId", "Level", "IconEmoji", "IsActive", "SortOrder", "CreatedAtUtc", "UpdatedAtUtc")
VALUES
    ('10000000-0000-0000-0000-000000000001', 'Electronics', 'إلكترونيات', 'electronics', NULL, 0, '📱', true, 1, TIMESTAMP WITH TIME ZONE '2026-01-01T00:00:00Z', NULL),
    ('10000000-0000-0000-0000-000000000002', 'Fashion', 'أزياء', 'fashion', NULL, 0, '👗', true, 2, TIMESTAMP WITH TIME ZONE '2026-01-01T00:00:00Z', NULL),
    ('10000000-0000-0000-0000-000000000003', 'Home & Garden', 'المنزل والحديقة', 'home-garden', NULL, 0, '🏠', true, 3, TIMESTAMP WITH TIME ZONE '2026-01-01T00:00:00Z', NULL),
    ('10000000-0000-0000-0000-000000000011', 'TVs', 'تلفزيونات', 'tvs', '10000000-0000-0000-0000-000000000001', 1, '📺', true, 1, TIMESTAMP WITH TIME ZONE '2026-01-01T00:00:00Z', NULL),
    ('10000000-0000-0000-0000-000000000012', 'Mobile Phones', 'هواتف محمولة', 'mobile-phones', '10000000-0000-0000-0000-000000000001', 1, '📱', true, 2, TIMESTAMP WITH TIME ZONE '2026-01-01T00:00:00Z', NULL),
    ('10000000-0000-0000-0000-000000000013', 'Laptops', 'لابتوب', 'laptops', '10000000-0000-0000-0000-000000000001', 1, '💻', true, 3, TIMESTAMP WITH TIME ZONE '2026-01-01T00:00:00Z', NULL),
    ('10000000-0000-0000-0000-000000000021', 'Men', 'رجالي', 'mens-fashion', '10000000-0000-0000-0000-000000000002', 1, '👔', true, 1, TIMESTAMP WITH TIME ZONE '2026-01-01T00:00:00Z', NULL),
    ('10000000-0000-0000-0000-000000000022', 'Women', 'نسائي', 'womens-fashion', '10000000-0000-0000-0000-000000000002', 1, '👗', true, 2, TIMESTAMP WITH TIME ZONE '2026-01-01T00:00:00Z', NULL),
    ('10000000-0000-0000-0000-000000000031', 'Furniture', 'أثاث', 'furniture', '10000000-0000-0000-0000-000000000003', 1, '🛋️', true, 1, TIMESTAMP WITH TIME ZONE '2026-01-01T00:00:00Z', NULL),
    ('10000000-0000-0000-0000-000000000032', 'Kitchen', 'مطبخ', 'kitchen', '10000000-0000-0000-0000-000000000003', 1, '🍳', true, 2, TIMESTAMP WITH TIME ZONE '2026-01-01T00:00:00Z', NULL)
ON CONFLICT ("Id") DO UPDATE
SET
    "Name" = EXCLUDED."Name",
    "NameAr" = EXCLUDED."NameAr",
    "Slug" = EXCLUDED."Slug",
    "ParentId" = EXCLUDED."ParentId",
    "Level" = EXCLUDED."Level",
    "IconEmoji" = EXCLUDED."IconEmoji",
    "IsActive" = EXCLUDED."IsActive",
    "SortOrder" = EXCLUDED."SortOrder",
    "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc";

INSERT INTO "CommerceHubCategoryAttributes" ("Id", "CategoryId", "Name", "NameAr", "Type", "IsRequired", "IsFilterable", "IsSearchable", "OptionsJson", "SortOrder")
VALUES
    ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000011', 'Brand', 'الماركة', 'select', true, true, true, '["Samsung","LG","Sony","TCL","Hisense","Toshiba","Panasonic"]', 1),
    ('20000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000011', 'Screen Size (inches)', 'حجم الشاشة (بوصة)', 'number', true, true, false, NULL, 2),
    ('20000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000011', 'Resolution', 'الدقة', 'select', false, true, true, '["HD","Full HD","4K UHD","8K"]', 3),
    ('20000000-0000-0000-0000-000000000011', '10000000-0000-0000-0000-000000000012', 'Brand', 'الماركة', 'select', true, true, true, '["Apple","Samsung","Xiaomi","Huawei","Oppo","Vivo","Realme","OnePlus"]', 1),
    ('20000000-0000-0000-0000-000000000012', '10000000-0000-0000-0000-000000000012', 'Storage', 'السعة التخزينية', 'select', true, true, true, '["32GB","64GB","128GB","256GB","512GB","1TB"]', 2),
    ('20000000-0000-0000-0000-000000000013', '10000000-0000-0000-0000-000000000012', 'RAM', 'الذاكرة العشوائية', 'select', false, true, false, '["2GB","3GB","4GB","6GB","8GB","12GB","16GB"]', 3),
    ('20000000-0000-0000-0000-000000000021', '10000000-0000-0000-0000-000000000013', 'Brand', 'الماركة', 'select', true, true, true, '["Apple","Dell","HP","Lenovo","ASUS","Acer","MSI"]', 1),
    ('20000000-0000-0000-0000-000000000022', '10000000-0000-0000-0000-000000000013', 'Processor', 'المعالج', 'select', false, true, true, '["Intel Core i3","Intel Core i5","Intel Core i7","Intel Core i9","AMD Ryzen 5","AMD Ryzen 7","Apple M1","Apple M2","Apple M3"]', 2),
    ('20000000-0000-0000-0000-000000000023', '10000000-0000-0000-0000-000000000013', 'RAM', 'الذاكرة العشوائية', 'select', false, true, false, '["4GB","8GB","16GB","32GB","64GB"]', 3),
    ('20000000-0000-0000-0000-000000000031', '10000000-0000-0000-0000-000000000021', 'Size', 'المقاس', 'select', true, true, false, '["XS","S","M","L","XL","XXL","3XL"]', 1),
    ('20000000-0000-0000-0000-000000000032', '10000000-0000-0000-0000-000000000021', 'Color', 'اللون', 'text', false, true, true, NULL, 2)
ON CONFLICT ("Id") DO UPDATE
SET
    "CategoryId" = EXCLUDED."CategoryId",
    "Name" = EXCLUDED."Name",
    "NameAr" = EXCLUDED."NameAr",
    "Type" = EXCLUDED."Type",
    "IsRequired" = EXCLUDED."IsRequired",
    "IsFilterable" = EXCLUDED."IsFilterable",
    "IsSearchable" = EXCLUDED."IsSearchable",
    "OptionsJson" = EXCLUDED."OptionsJson",
    "SortOrder" = EXCLUDED."SortOrder";

COMMIT;

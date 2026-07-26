-- ============================================================================
-- Migration: AddCommerceHubGlobalCategories
-- Generated from: 20260318223800_AddCommerceHubGlobalCategories.cs
-- Run this in PgAdmin4 against your database
-- ============================================================================

-- 1) Add new columns to existing tables (wrapped in DO blocks for safety)
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'CommerceHubSubOrders') THEN
        ALTER TABLE "CommerceHubSubOrders" ADD COLUMN IF NOT EXISTS "PaidAt" timestamp with time zone;
        ALTER TABLE "CommerceHubSubOrders" ADD COLUMN IF NOT EXISTS "PaymentReferenceId" varchar(100);
        ALTER TABLE "CommerceHubSubOrders" ADD COLUMN IF NOT EXISTS "PaymentStatus" integer NOT NULL DEFAULT 0;
    END IF;
END $$;

DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'CommerceHubMarketplaceOrders') THEN
        ALTER TABLE "CommerceHubMarketplaceOrders" ADD COLUMN IF NOT EXISTS "IdempotencyKey" varchar(100);
        ALTER TABLE "CommerceHubMarketplaceOrders" ADD COLUMN IF NOT EXISTS "PaidAt" timestamp with time zone;
        ALTER TABLE "CommerceHubMarketplaceOrders" ADD COLUMN IF NOT EXISTS "PaymentReferenceId" varchar(100);
    END IF;
END $$;

DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'CommerceHubListings') THEN
        ALTER TABLE "CommerceHubListings" ADD COLUMN IF NOT EXISTS "AttributesJson" text;
        ALTER TABLE "CommerceHubListings" ADD COLUMN IF NOT EXISTS "AttributeSchemaSnapshotJson" text;
    END IF;
END $$;

-- 2) Create CommerceHubCategories table
CREATE TABLE IF NOT EXISTS "CommerceHubCategories" (
    "Id" uuid NOT NULL,
    "Name" varchar(200) NOT NULL,
    "NameAr" varchar(200),
    "Slug" varchar(200) NOT NULL,
    "ParentId" uuid,
    "Level" integer NOT NULL,
    "IconEmoji" varchar(10),
    "IsActive" boolean NOT NULL DEFAULT true,
    "SortOrder" integer NOT NULL DEFAULT 0,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_CommerceHubCategories" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CommerceHubCategories_CommerceHubCategories_ParentId"
        FOREIGN KEY ("ParentId") REFERENCES "CommerceHubCategories" ("Id")
        ON DELETE RESTRICT
);

-- 3) Create CommerceHubCategoryAttributes table
CREATE TABLE IF NOT EXISTS "CommerceHubCategoryAttributes" (
    "Id" uuid NOT NULL,
    "CategoryId" uuid NOT NULL,
    "Name" varchar(200) NOT NULL,
    "NameAr" varchar(200),
    "Type" varchar(20) NOT NULL,
    "IsRequired" boolean NOT NULL,
    "IsFilterable" boolean NOT NULL DEFAULT true,
    "IsSearchable" boolean NOT NULL DEFAULT true,
    "OptionsJson" text,
    "SortOrder" integer NOT NULL DEFAULT 0,
    CONSTRAINT "PK_CommerceHubCategoryAttributes" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CommerceHubCategoryAttr_CommerceHubCat_CategoryId"
        FOREIGN KEY ("CategoryId") REFERENCES "CommerceHubCategories" ("Id")
        ON DELETE CASCADE
);

-- 3b) Add IsFilterable/IsSearchable to existing table (idempotent)
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'CommerceHubCategoryAttributes') THEN
        ALTER TABLE "CommerceHubCategoryAttributes" ADD COLUMN IF NOT EXISTS "IsFilterable" boolean NOT NULL DEFAULT true;
        ALTER TABLE "CommerceHubCategoryAttributes" ADD COLUMN IF NOT EXISTS "IsSearchable" boolean NOT NULL DEFAULT true;
    END IF;
END $$;

-- 4) Create indexes
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'CommerceHubMarketplaceOrders') THEN
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubMarketplaceOrders_IdempotencyKey"
            ON "CommerceHubMarketplaceOrders" ("IdempotencyKey")
            WHERE "IdempotencyKey" IS NOT NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubCategories_ParentId_SortOrder"
    ON "CommerceHubCategories" ("ParentId", "SortOrder");

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CommerceHubCategories_Slug"
    ON "CommerceHubCategories" ("Slug");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubCategoryAttributes_CategoryId_SortOrder"
    ON "CommerceHubCategoryAttributes" ("CategoryId", "SortOrder");

-- 5) Seed: Root categories
INSERT INTO "CommerceHubCategories" ("Id", "Name", "NameAr", "Slug", "ParentId", "Level", "IconEmoji", "IsActive", "SortOrder", "CreatedAt")
VALUES
    ('10000000-0000-0000-0000-000000000001', 'Electronics', 'إلكترونيات', 'electronics', NULL, 0, '📱', true, 1, '2026-01-01T00:00:00Z'),
    ('10000000-0000-0000-0000-000000000002', 'Fashion', 'أزياء', 'fashion', NULL, 0, '👗', true, 2, '2026-01-01T00:00:00Z'),
    ('10000000-0000-0000-0000-000000000003', 'Home & Garden', 'المنزل والحديقة', 'home-garden', NULL, 0, '🏠', true, 3, '2026-01-01T00:00:00Z')
ON CONFLICT ("Id") DO NOTHING;

-- 6) Seed: Sub-categories
INSERT INTO "CommerceHubCategories" ("Id", "Name", "NameAr", "Slug", "ParentId", "Level", "IconEmoji", "IsActive", "SortOrder", "CreatedAt")
VALUES
    ('10000000-0000-0000-0000-000000000011', 'TVs', 'تلفزيونات', 'tvs', '10000000-0000-0000-0000-000000000001', 1, '📺', true, 1, '2026-01-01T00:00:00Z'),
    ('10000000-0000-0000-0000-000000000012', 'Mobile Phones', 'هواتف محمولة', 'mobile-phones', '10000000-0000-0000-0000-000000000001', 1, '📱', true, 2, '2026-01-01T00:00:00Z'),
    ('10000000-0000-0000-0000-000000000013', 'Laptops', 'لابتوب', 'laptops', '10000000-0000-0000-0000-000000000001', 1, '💻', true, 3, '2026-01-01T00:00:00Z'),
    ('10000000-0000-0000-0000-000000000021', 'Men', 'رجالي', 'mens-fashion', '10000000-0000-0000-0000-000000000002', 1, '👔', true, 1, '2026-01-01T00:00:00Z'),
    ('10000000-0000-0000-0000-000000000022', 'Women', 'نسائي', 'womens-fashion', '10000000-0000-0000-0000-000000000002', 1, '👗', true, 2, '2026-01-01T00:00:00Z'),
    ('10000000-0000-0000-0000-000000000031', 'Furniture', 'أثاث', 'furniture', '10000000-0000-0000-0000-000000000003', 1, '🛋️', true, 1, '2026-01-01T00:00:00Z'),
    ('10000000-0000-0000-0000-000000000032', 'Kitchen', 'مطبخ', 'kitchen', '10000000-0000-0000-0000-000000000003', 1, '🍳', true, 2, '2026-01-01T00:00:00Z')
ON CONFLICT ("Id") DO NOTHING;

-- 7) Seed: Category attributes (with IsFilterable/IsSearchable)
INSERT INTO "CommerceHubCategoryAttributes" ("Id", "CategoryId", "Name", "NameAr", "Type", "IsRequired", "IsFilterable", "IsSearchable", "OptionsJson", "SortOrder")
VALUES
    -- TVs
    ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000011', 'Brand', 'الماركة', 'select', true, true, true, '["Samsung","LG","Sony","TCL","Hisense","Toshiba","Panasonic"]', 1),
    ('20000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000011', 'Screen Size (inches)', 'حجم الشاشة (بوصة)', 'number', true, true, false, NULL, 2),
    ('20000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000011', 'Resolution', 'الدقة', 'select', false, true, true, '["HD","Full HD","4K UHD","8K"]', 3),
    -- Mobile Phones
    ('20000000-0000-0000-0000-000000000011', '10000000-0000-0000-0000-000000000012', 'Brand', 'الماركة', 'select', true, true, true, '["Apple","Samsung","Xiaomi","Huawei","Oppo","Vivo","Realme","OnePlus"]', 1),
    ('20000000-0000-0000-0000-000000000012', '10000000-0000-0000-0000-000000000012', 'Storage', 'السعة التخزينية', 'select', true, true, true, '["32GB","64GB","128GB","256GB","512GB","1TB"]', 2),
    ('20000000-0000-0000-0000-000000000013', '10000000-0000-0000-0000-000000000012', 'RAM', 'الذاكرة العشوائية', 'select', false, true, false, '["2GB","3GB","4GB","6GB","8GB","12GB","16GB"]', 3),
    -- Laptops
    ('20000000-0000-0000-0000-000000000021', '10000000-0000-0000-0000-000000000013', 'Brand', 'الماركة', 'select', true, true, true, '["Apple","Dell","HP","Lenovo","ASUS","Acer","MSI"]', 1),
    ('20000000-0000-0000-0000-000000000022', '10000000-0000-0000-0000-000000000013', 'Processor', 'المعالج', 'select', false, true, true, '["Intel Core i3","Intel Core i5","Intel Core i7","Intel Core i9","AMD Ryzen 5","AMD Ryzen 7","Apple M1","Apple M2","Apple M3"]', 2),
    ('20000000-0000-0000-0000-000000000023', '10000000-0000-0000-0000-000000000013', 'RAM', 'الذاكرة العشوائية', 'select', false, true, false, '["4GB","8GB","16GB","32GB","64GB"]', 3),
    -- Men's Fashion
    ('20000000-0000-0000-0000-000000000031', '10000000-0000-0000-0000-000000000021', 'Size', 'المقاس', 'select', true, true, false, '["XS","S","M","L","XL","XXL","3XL"]', 1),
    ('20000000-0000-0000-0000-000000000032', '10000000-0000-0000-0000-000000000021', 'Color', 'اللون', 'text', false, true, true, NULL, 2)
ON CONFLICT ("Id") DO UPDATE SET
    "IsFilterable" = EXCLUDED."IsFilterable",
    "IsSearchable" = EXCLUDED."IsSearchable";

-- 8) Record migration in EF history
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260318223800_AddCommerceHubGlobalCategories', '9.0.1')
ON CONFLICT DO NOTHING;

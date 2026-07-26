-- ═══════════════════════════════════════════════════════════════════
-- AF-30: Affiliate Marketplace Category System
-- Global taxonomy layer for marketplace product categorization
-- ═══════════════════════════════════════════════════════════════════

-- Step 1: Create AffiliateCategories table
CREATE TABLE IF NOT EXISTS "AffiliateCategories" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Name" VARCHAR(100) NOT NULL,
    "Slug" VARCHAR(100) NOT NULL,
    "ParentId" UUID NULL,
    "Level" INT NOT NULL,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "DisplayOrder" INT NOT NULL DEFAULT 0,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP NULL,
    "IsDeleted" BOOLEAN NOT NULL DEFAULT FALSE,
    
    -- Constraints
    CONSTRAINT "UQ_AffiliateCategories_Slug" UNIQUE ("Slug"),
    CONSTRAINT "UQ_AffiliateCategories_Parent_Name" UNIQUE ("ParentId", "Name"),
    CONSTRAINT "CK_AffiliateCategories_Level" CHECK ("Level" IN (1, 2)),
    CONSTRAINT "CK_AffiliateCategories_Level1_NoParent" CHECK (
        ("Level" = 1 AND "ParentId" IS NULL) OR 
        ("Level" = 2 AND "ParentId" IS NOT NULL)
    ),
    CONSTRAINT "FK_AffiliateCategories_Parent" FOREIGN KEY ("ParentId") 
        REFERENCES "AffiliateCategories"("Id") ON DELETE RESTRICT
);

-- Indexes
CREATE INDEX IF NOT EXISTS "IX_AffiliateCategories_ParentId" ON "AffiliateCategories"("ParentId");
CREATE INDEX IF NOT EXISTS "IX_AffiliateCategories_IsActive" ON "AffiliateCategories"("IsActive");
CREATE INDEX IF NOT EXISTS "IX_AffiliateCategories_DisplayOrder" ON "AffiliateCategories"("DisplayOrder");

-- Step 2: Add AffiliateCategoryId to AffiliateProductPolicies
ALTER TABLE "AffiliateProductPolicies" 
ADD COLUMN IF NOT EXISTS "AffiliateCategoryId" UUID NULL;

-- Add FK constraint
ALTER TABLE "AffiliateProductPolicies"
ADD CONSTRAINT "FK_AffiliateProductPolicies_Category" 
FOREIGN KEY ("AffiliateCategoryId") 
REFERENCES "AffiliateCategories"("Id") ON DELETE RESTRICT;

-- Add index for category filtering
CREATE INDEX IF NOT EXISTS "IX_AffiliateProductPolicies_CategoryId" 
ON "AffiliateProductPolicies"("AffiliateCategoryId");

-- Step 3: Seed initial categories (optional - can be done via Admin UI)
-- Main Categories (Level 1)
INSERT INTO "AffiliateCategories" ("Id", "Name", "Slug", "ParentId", "Level", "IsActive", "DisplayOrder", "CreatedAt")
VALUES 
    ('11111111-1111-1111-1111-111111111111', 'Electronics', 'electronics', NULL, 1, TRUE, 1, NOW()),
    ('22222222-2222-2222-2222-222222222222', 'Fashion', 'fashion', NULL, 1, TRUE, 2, NOW()),
    ('33333333-3333-3333-3333-333333333333', 'Home & Living', 'home-living', NULL, 1, TRUE, 3, NOW()),
    ('44444444-4444-4444-4444-444444444444', 'Beauty & Health', 'beauty-health', NULL, 1, TRUE, 4, NOW()),
    ('55555555-5555-5555-5555-555555555555', 'Sports & Outdoors', 'sports-outdoors', NULL, 1, TRUE, 5, NOW()),
    ('66666666-6666-6666-6666-666666666666', 'Books & Media', 'books-media', NULL, 1, TRUE, 6, NOW()),
    ('77777777-7777-7777-7777-777777777777', 'Food & Beverages', 'food-beverages', NULL, 1, TRUE, 7, NOW()),
    ('88888888-8888-8888-8888-888888888888', 'Toys & Games', 'toys-games', NULL, 1, TRUE, 8, NOW())
ON CONFLICT ("Slug") DO NOTHING;

-- Subcategories (Level 2) - Electronics
INSERT INTO "AffiliateCategories" ("Id", "Name", "Slug", "ParentId", "Level", "IsActive", "DisplayOrder", "CreatedAt")
VALUES 
    ('11111111-1111-1111-1111-111111111112', 'Mobile Phones', 'mobile-phones', '11111111-1111-1111-1111-111111111111', 2, TRUE, 1, NOW()),
    ('11111111-1111-1111-1111-111111111113', 'Laptops & Computers', 'laptops-computers', '11111111-1111-1111-1111-111111111111', 2, TRUE, 2, NOW()),
    ('11111111-1111-1111-1111-111111111114', 'Tablets', 'tablets', '11111111-1111-1111-1111-111111111111', 2, TRUE, 3, NOW()),
    ('11111111-1111-1111-1111-111111111115', 'Audio & Headphones', 'audio-headphones', '11111111-1111-1111-1111-111111111111', 2, TRUE, 4, NOW()),
    ('11111111-1111-1111-1111-111111111116', 'Cameras', 'cameras', '11111111-1111-1111-1111-111111111111', 2, TRUE, 5, NOW()),
    ('11111111-1111-1111-1111-111111111117', 'Smart Home', 'smart-home', '11111111-1111-1111-1111-111111111111', 2, TRUE, 6, NOW())
ON CONFLICT ("Slug") DO NOTHING;

-- Subcategories (Level 2) - Fashion
INSERT INTO "AffiliateCategories" ("Id", "Name", "Slug", "ParentId", "Level", "IsActive", "DisplayOrder", "CreatedAt")
VALUES 
    ('22222222-2222-2222-2222-222222222223', 'Men''s Clothing', 'mens-clothing', '22222222-2222-2222-2222-222222222222', 2, TRUE, 1, NOW()),
    ('22222222-2222-2222-2222-222222222224', 'Women''s Clothing', 'womens-clothing', '22222222-2222-2222-2222-222222222222', 2, TRUE, 2, NOW()),
    ('22222222-2222-2222-2222-222222222225', 'Shoes', 'shoes', '22222222-2222-2222-2222-222222222222', 2, TRUE, 3, NOW()),
    ('22222222-2222-2222-2222-222222222226', 'Bags & Accessories', 'bags-accessories', '22222222-2222-2222-2222-222222222222', 2, TRUE, 4, NOW()),
    ('22222222-2222-2222-2222-222222222227', 'Watches', 'watches', '22222222-2222-2222-2222-222222222222', 2, TRUE, 5, NOW())
ON CONFLICT ("Slug") DO NOTHING;

-- Subcategories (Level 2) - Home & Living
INSERT INTO "AffiliateCategories" ("Id", "Name", "Slug", "ParentId", "Level", "IsActive", "DisplayOrder", "CreatedAt")
VALUES 
    ('33333333-3333-3333-3333-333333333334', 'Furniture', 'furniture', '33333333-3333-3333-3333-333333333333', 2, TRUE, 1, NOW()),
    ('33333333-3333-3333-3333-333333333335', 'Kitchen & Dining', 'kitchen-dining', '33333333-3333-3333-3333-333333333333', 2, TRUE, 2, NOW()),
    ('33333333-3333-3333-3333-333333333336', 'Bedding', 'bedding', '33333333-3333-3333-3333-333333333333', 2, TRUE, 3, NOW()),
    ('33333333-3333-3333-3333-333333333337', 'Home Decor', 'home-decor', '33333333-3333-3333-3333-333333333333', 2, TRUE, 4, NOW()),
    ('33333333-3333-3333-3333-333333333338', 'Appliances', 'appliances', '33333333-3333-3333-3333-333333333333', 2, TRUE, 5, NOW())
ON CONFLICT ("Slug") DO NOTHING;

-- Subcategories (Level 2) - Beauty & Health
INSERT INTO "AffiliateCategories" ("Id", "Name", "Slug", "ParentId", "Level", "IsActive", "DisplayOrder", "CreatedAt")
VALUES 
    ('44444444-4444-4444-4444-444444444445', 'Skincare', 'skincare', '44444444-4444-4444-4444-444444444444', 2, TRUE, 1, NOW()),
    ('44444444-4444-4444-4444-444444444446', 'Makeup', 'makeup', '44444444-4444-4444-4444-444444444444', 2, TRUE, 2, NOW()),
    ('44444444-4444-4444-4444-444444444447', 'Haircare', 'haircare', '44444444-4444-4444-4444-444444444444', 2, TRUE, 3, NOW()),
    ('44444444-4444-4444-4444-444444444448', 'Fragrances', 'fragrances', '44444444-4444-4444-4444-444444444444', 2, TRUE, 4, NOW()),
    ('44444444-4444-4444-4444-444444444449', 'Supplements', 'supplements', '44444444-4444-4444-4444-444444444444', 2, TRUE, 5, NOW())
ON CONFLICT ("Slug") DO NOTHING;

-- Subcategories (Level 2) - Sports & Outdoors
INSERT INTO "AffiliateCategories" ("Id", "Name", "Slug", "ParentId", "Level", "IsActive", "DisplayOrder", "CreatedAt")
VALUES 
    ('55555555-5555-5555-5555-555555555556', 'Fitness Equipment', 'fitness-equipment', '55555555-5555-5555-5555-555555555555', 2, TRUE, 1, NOW()),
    ('55555555-5555-5555-5555-555555555557', 'Sportswear', 'sportswear', '55555555-5555-5555-5555-555555555555', 2, TRUE, 2, NOW()),
    ('55555555-5555-5555-5555-555555555558', 'Camping & Hiking', 'camping-hiking', '55555555-5555-5555-5555-555555555555', 2, TRUE, 3, NOW()),
    ('55555555-5555-5555-5555-555555555559', 'Cycling', 'cycling', '55555555-5555-5555-5555-555555555555', 2, TRUE, 4, NOW())
ON CONFLICT ("Slug") DO NOTHING;

COMMIT;

-- ═══════════════════════════════════════════════════════════════════
-- VERIFICATION QUERIES
-- ═══════════════════════════════════════════════════════════════════

-- Verify table structure
SELECT 
    table_name, 
    column_name, 
    data_type, 
    is_nullable,
    column_default
FROM information_schema.columns
WHERE table_name IN ('AffiliateCategories', 'AffiliateProductPolicies')
ORDER BY table_name, ordinal_position;

-- Verify constraints
SELECT 
    tc.constraint_name, 
    tc.table_name, 
    tc.constraint_type,
    kcu.column_name
FROM information_schema.table_constraints tc
LEFT JOIN information_schema.key_column_usage kcu 
    ON tc.constraint_name = kcu.constraint_name
WHERE tc.table_name IN ('AffiliateCategories', 'AffiliateProductPolicies')
ORDER BY tc.table_name, tc.constraint_type;

-- Verify seeded categories
SELECT 
    c1."Name" AS "MainCategory",
    c1."Slug" AS "MainSlug",
    c1."Level" AS "MainLevel",
    c2."Name" AS "Subcategory",
    c2."Slug" AS "SubSlug",
    c2."Level" AS "SubLevel"
FROM "AffiliateCategories" c1
LEFT JOIN "AffiliateCategories" c2 ON c2."ParentId" = c1."Id"
WHERE c1."Level" = 1
ORDER BY c1."DisplayOrder", c2."DisplayOrder";

// AF-33: Compound indexes for deterministic sorting on AffiliateProductIntelligence collection
// Run in MongoDB shell or Compass against the relevant database.
//
// These indexes support:
// - Category + Commission sorting (default sort)
// - Category + Time sorting (newest)
// - Category + Price sorting (price asc/desc)
// - Stock-aware commission sorting (in-stock first)
//
// Without these indexes, sorts on 100k+ docs cause collection scans.

// 1) Commission sorting (default + most common)
db.AffiliateProductIntelligence.createIndex(
    { "AffiliateCategorySlug": 1, "IsOutOfStock": 1, "CommissionRate": -1 },
    { name: "idx_category_stock_commission", background: true }
);

// 2) Parent category commission sorting (for main category filter)
db.AffiliateProductIntelligence.createIndex(
    { "AffiliateParentCategorySlug": 1, "IsOutOfStock": 1, "CommissionRate": -1 },
    { name: "idx_parentcategory_stock_commission", background: true }
);

// 3) Newest sorting
db.AffiliateProductIntelligence.createIndex(
    { "AffiliateCategorySlug": 1, "LastUpdatedUtc": -1 },
    { name: "idx_category_newest", background: true }
);

// 4) Price sorting
db.AffiliateProductIntelligence.createIndex(
    { "AffiliateCategorySlug": 1, "BasePrice": -1 },
    { name: "idx_category_price", background: true }
);

// 5) Global commission sorting (no category filter)
db.AffiliateProductIntelligence.createIndex(
    { "IsOutOfStock": 1, "CommissionRate": -1 },
    { name: "idx_stock_commission_global", background: true }
);

// 6) Global newest sorting
db.AffiliateProductIntelligence.createIndex(
    { "LastUpdatedUtc": -1 },
    { name: "idx_newest_global", background: true }
);

// 7) Global price sorting
db.AffiliateProductIntelligence.createIndex(
    { "BasePrice": -1 },
    { name: "idx_price_global", background: true }
);

// ═══════════════════════════════════════════════════════════════════
// AF-34: Ranking Score Indexes
// ═══════════════════════════════════════════════════════════════════

// 8) Global ranking sorting
db.AffiliateProductIntelligence.createIndex(
    { "RankingScore": -1 },
    { name: "idx_ranking_global", background: true }
);

// 9) Category + Ranking sorting
db.AffiliateProductIntelligence.createIndex(
    { "AffiliateCategorySlug": 1, "RankingScore": -1 },
    { name: "idx_category_ranking", background: true }
);

// 10) Parent Category + Ranking sorting
db.AffiliateProductIntelligence.createIndex(
    { "AffiliateParentCategorySlug": 1, "RankingScore": -1 },
    { name: "idx_parentcategory_ranking", background: true }
);

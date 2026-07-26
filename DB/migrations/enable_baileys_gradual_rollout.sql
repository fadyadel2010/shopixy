-- ============================================================================
-- WhatsApp Baileys Migration - Gradual Rollout Scripts
-- ============================================================================
-- These scripts enable Baileys transport for tenants in a controlled manner
-- ============================================================================

-- ============================================================================
-- STEP 1: Enable Baileys for Pilot Tenants (2-3 tenants)
-- ============================================================================
-- Purpose: Initial testing with a small group of tenants
-- Duration: 24-48 hours monitoring
-- Rollback: Set UseBaileys = false for these tenants
-- ============================================================================

-- Example: Enable for specific pilot tenants
-- IMPORTANT: Replace with actual tenant IDs from your database
/*
UPDATE "Tenants"
SET "UseBaileys" = true,
    "UpdatedAtUtc" = NOW()
WHERE "Id" IN (
    'REPLACE_WITH_PILOT_TENANT_ID_1',
    'REPLACE_WITH_PILOT_TENANT_ID_2',
    'REPLACE_WITH_PILOT_TENANT_ID_3'
);
*/

-- Verify pilot tenants
SELECT 
    "Id",
    "Name",
    "UseBaileys",
    "UpdatedAtUtc"
FROM "Tenants"
WHERE "UseBaileys" = true;

-- ============================================================================
-- STEP 2: Enable Baileys for 10% of Tenants
-- ============================================================================
-- Purpose: Expand to 10% of tenant base
-- Duration: 48 hours monitoring
-- Rollback: Run rollback script below
-- ============================================================================

-- Calculate 10% of tenants
SELECT COUNT(*) * 0.10 AS target_count
FROM "Tenants"
WHERE "IsActive" = true;

-- Enable for 10% of active tenants (excluding pilots)
-- Strategy: Select tenants with lowest message volume first (safer)
/*
WITH tenant_message_counts AS (
    SELECT 
        t."Id",
        t."Name",
        COUNT(wml."Id") AS message_count
    FROM "Tenants" t
    LEFT JOIN "WhatsAppMessageLogs" wml ON wml."TenantId" = t."Id"::text
    WHERE t."IsActive" = true
      AND t."UseBaileys" = false
    GROUP BY t."Id", t."Name"
    ORDER BY message_count ASC
    LIMIT (SELECT CAST(COUNT(*) * 0.10 AS INTEGER) FROM "Tenants" WHERE "IsActive" = true)
)
UPDATE "Tenants"
SET "UseBaileys" = true,
    "UpdatedAtUtc" = NOW()
WHERE "Id" IN (SELECT "Id" FROM tenant_message_counts);
*/

-- Verify 10% rollout
SELECT 
    COUNT(*) FILTER (WHERE "UseBaileys" = true) AS baileys_count,
    COUNT(*) FILTER (WHERE "UseBaileys" = false) AS playwright_count,
    ROUND(COUNT(*) FILTER (WHERE "UseBaileys" = true) * 100.0 / COUNT(*), 2) AS baileys_percentage
FROM "Tenants"
WHERE "IsActive" = true;

-- ============================================================================
-- STEP 3: Enable Baileys for 25% of Tenants
-- ============================================================================
-- Purpose: Expand to 25% of tenant base
-- Duration: 48 hours monitoring
-- ============================================================================

/*
WITH tenant_message_counts AS (
    SELECT 
        t."Id",
        t."Name",
        COUNT(wml."Id") AS message_count
    FROM "Tenants" t
    LEFT JOIN "WhatsAppMessageLogs" wml ON wml."TenantId" = t."Id"::text
    WHERE t."IsActive" = true
      AND t."UseBaileys" = false
    GROUP BY t."Id", t."Name"
    ORDER BY message_count ASC
    LIMIT (SELECT CAST(COUNT(*) * 0.15 AS INTEGER) FROM "Tenants" WHERE "IsActive" = true)
)
UPDATE "Tenants"
SET "UseBaileys" = true,
    "UpdatedAtUtc" = NOW()
WHERE "Id" IN (SELECT "Id" FROM tenant_message_counts);
*/

-- Verify 25% rollout
SELECT 
    COUNT(*) FILTER (WHERE "UseBaileys" = true) AS baileys_count,
    COUNT(*) FILTER (WHERE "UseBaileys" = false) AS playwright_count,
    ROUND(COUNT(*) FILTER (WHERE "UseBaileys" = true) * 100.0 / COUNT(*), 2) AS baileys_percentage
FROM "Tenants"
WHERE "IsActive" = true;

-- ============================================================================
-- STEP 4: Enable Baileys for 50% of Tenants
-- ============================================================================
-- Purpose: Expand to 50% of tenant base
-- Duration: 48 hours monitoring
-- ============================================================================

/*
WITH tenant_message_counts AS (
    SELECT 
        t."Id",
        t."Name",
        COUNT(wml."Id") AS message_count
    FROM "Tenants" t
    LEFT JOIN "WhatsAppMessageLogs" wml ON wml."TenantId" = t."Id"::text
    WHERE t."IsActive" = true
      AND t."UseBaileys" = false
    GROUP BY t."Id", t."Name"
    ORDER BY message_count ASC
    LIMIT (SELECT CAST(COUNT(*) * 0.25 AS INTEGER) FROM "Tenants" WHERE "IsActive" = true)
)
UPDATE "Tenants"
SET "UseBaileys" = true,
    "UpdatedAtUtc" = NOW()
WHERE "Id" IN (SELECT "Id" FROM tenant_message_counts);
*/

-- Verify 50% rollout
SELECT 
    COUNT(*) FILTER (WHERE "UseBaileys" = true) AS baileys_count,
    COUNT(*) FILTER (WHERE "UseBaileys" = false) AS playwright_count,
    ROUND(COUNT(*) FILTER (WHERE "UseBaileys" = true) * 100.0 / COUNT(*), 2) AS baileys_percentage
FROM "Tenants"
WHERE "IsActive" = true;

-- ============================================================================
-- STEP 5: Enable Baileys for 100% of Tenants
-- ============================================================================
-- Purpose: Complete migration to Baileys
-- Duration: Monitor for 2-4 weeks before decommissioning Playwright
-- ============================================================================

/*
UPDATE "Tenants"
SET "UseBaileys" = true,
    "UpdatedAtUtc" = NOW()
WHERE "IsActive" = true
  AND "UseBaileys" = false;
*/

-- Verify 100% rollout
SELECT 
    COUNT(*) FILTER (WHERE "UseBaileys" = true) AS baileys_count,
    COUNT(*) FILTER (WHERE "UseBaileys" = false) AS playwright_count,
    ROUND(COUNT(*) FILTER (WHERE "UseBaileys" = true) * 100.0 / COUNT(*), 2) AS baileys_percentage
FROM "Tenants"
WHERE "IsActive" = true;

-- ============================================================================
-- ROLLBACK SCRIPTS
-- ============================================================================

-- Rollback: Disable Baileys for all tenants (emergency)
/*
UPDATE "Tenants"
SET "UseBaileys" = false,
    "UpdatedAtUtc" = NOW()
WHERE "UseBaileys" = true;
*/

-- Rollback: Disable Baileys for specific tenant
/*
UPDATE "Tenants"
SET "UseBaileys" = false,
    "UpdatedAtUtc" = NOW()
WHERE "Id" = 'TENANT_ID_HERE';
*/

-- Rollback: Disable Baileys for tenants with high error rates
/*
WITH error_tenants AS (
    SELECT 
        "TenantId",
        COUNT(*) FILTER (WHERE "Status" = 'FAILED') AS failed_count,
        COUNT(*) AS total_count,
        ROUND(COUNT(*) FILTER (WHERE "Status" = 'FAILED') * 100.0 / COUNT(*), 2) AS error_rate
    FROM "WhatsAppMessageLogs"
    WHERE "CreatedAtUtc" >= NOW() - INTERVAL '24 hours'
    GROUP BY "TenantId"
    HAVING COUNT(*) FILTER (WHERE "Status" = 'FAILED') * 100.0 / COUNT(*) > 10
)
UPDATE "Tenants"
SET "UseBaileys" = false,
    "UpdatedAtUtc" = NOW()
WHERE "Id"::text IN (SELECT "TenantId" FROM error_tenants);
*/

-- ============================================================================
-- MONITORING QUERIES
-- ============================================================================
-- NOTE: WhatsApp message logs are stored in MongoDB, not PostgreSQL.
-- Use the MongoDB queries below for message monitoring.
-- PostgreSQL queries are only for tenant management.
-- ============================================================================

-- PostgreSQL: Get list of tenants using Baileys
SELECT 
    "Id",
    "Name",
    "UseBaileys",
    "UpdatedAtUtc"
FROM "Tenants"
WHERE "UseBaileys" = true
ORDER BY "UpdatedAtUtc" DESC;

-- PostgreSQL: Count tenants by transport
SELECT 
    "UseBaileys",
    COUNT(*) AS tenant_count
FROM "Tenants"
WHERE "IsActive" = true
GROUP BY "UseBaileys";

-- ============================================================================
-- MONGODB MONITORING QUERIES
-- ============================================================================
-- Run these queries in MongoDB (not PostgreSQL)
-- Connect to MongoDB: mongo mongodb://your-host:27017/ecommerce_read_db
-- ============================================================================

/*
// MongoDB: Monitor message delivery success rate by transport
// This requires joining with PostgreSQL tenant data
// Run this in your application code or use a script

db.WhatsAppMessageLogs.aggregate([
  {
    $match: {
      CreatedAtUtc: { $gte: new Date(Date.now() - 24 * 60 * 60 * 1000) }
    }
  },
  {
    $group: {
      _id: "$TenantId",
      total: { $sum: 1 },
      sent: { $sum: { $cond: [{ $eq: ["$Status", "SENT"] }, 1, 0] } },
      failed: { $sum: { $cond: [{ $eq: ["$Status", "FAILED"] }, 1, 0] } }
    }
  },
  {
    $project: {
      tenantId: "$_id",
      total: 1,
      sent: 1,
      failed: 1,
      successRate: { $multiply: [{ $divide: ["$sent", "$total"] }, 100] }
    }
  },
  {
    $sort: { successRate: 1 }
  }
])

// MongoDB: Monitor message send latency
db.WhatsAppMessageLogs.aggregate([
  {
    $match: {
      CreatedAtUtc: { $gte: new Date(Date.now() - 24 * 60 * 60 * 1000) },
      Status: "SENT"
    }
  },
  {
    $project: {
      TenantId: 1,
      latency: {
        $divide: [
          { $subtract: ["$UpdatedAtUtc", "$CreatedAtUtc"] },
          1000
        ]
      }
    }
  },
  {
    $group: {
      _id: "$TenantId",
      avgLatency: { $avg: "$latency" },
      maxLatency: { $max: "$latency" },
      minLatency: { $min: "$latency" },
      count: { $sum: 1 }
    }
  },
  {
    $sort: { avgLatency: -1 }
  }
])

// MongoDB: Monitor tenants with high error rates
db.WhatsAppMessageLogs.aggregate([
  {
    $match: {
      CreatedAtUtc: { $gte: new Date(Date.now() - 24 * 60 * 60 * 1000) }
    }
  },
  {
    $group: {
      _id: "$TenantId",
      total: { $sum: 1 },
      failed: { $sum: { $cond: [{ $eq: ["$Status", "FAILED"] }, 1, 0] } }
    }
  },
  {
    $project: {
      tenantId: "$_id",
      total: 1,
      failed: 1,
      errorRate: { $multiply: [{ $divide: ["$failed", "$total"] }, 100] }
    }
  },
  {
    $match: {
      errorRate: { $gt: 5 }
    }
  },
  {
    $sort: { errorRate: -1 }
  }
])

// MongoDB: Monitor circuit breaker activations
db.WhatsAppMessageLogs.aggregate([
  {
    $match: {
      CreatedAtUtc: { $gte: new Date(Date.now() - 24 * 60 * 60 * 1000) },
      Status: "FAILED",
      ErrorMessage: { $regex: /circuit breaker/i }
    }
  },
  {
    $group: {
      _id: "$TenantId",
      circuitBreakerFailures: { $sum: 1 }
    }
  },
  {
    $sort: { circuitBreakerFailures: -1 }
  }
])

// MongoDB: Get recent messages for a specific tenant
db.WhatsAppMessageLogs.find({
  TenantId: "YOUR_TENANT_ID_HERE",
  CreatedAtUtc: { $gte: new Date(Date.now() - 24 * 60 * 60 * 1000) }
}).sort({ CreatedAtUtc: -1 }).limit(100)

// MongoDB: Count messages by status (last 24 hours)
db.WhatsAppMessageLogs.aggregate([
  {
    $match: {
      CreatedAtUtc: { $gte: new Date(Date.now() - 24 * 60 * 60 * 1000) }
    }
  },
  {
    $group: {
      _id: "$Status",
      count: { $sum: 1 }
    }
  },
  {
    $sort: { count: -1 }
  }
])
*/

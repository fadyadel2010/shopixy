BEGIN;

CREATE TABLE IF NOT EXISTS "CommerceHubSubOrderStatusHistory" (
    "Id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "SubOrderId" uuid NOT NULL,
    "PreviousStatus" integer NOT NULL,
    "NewStatus" integer NOT NULL,
    "Reason" character varying(1000) NULL,
    "CorrelationId" character varying(200) NULL,
    "ChangedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubSubOrderStatusHistory" PRIMARY KEY ("Id")
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubSubOrderStatusHistory_CommerceHubSubOrders_SubOrderId'
    ) THEN
        ALTER TABLE "CommerceHubSubOrderStatusHistory"
            ADD CONSTRAINT "FK_CommerceHubSubOrderStatusHistory_CommerceHubSubOrders_SubOrderId"
            FOREIGN KEY ("SubOrderId")
            REFERENCES "CommerceHubSubOrders" ("Id")
            ON DELETE CASCADE;
    END IF;
END $$;

WITH ranked_items AS (
    SELECT
        item."Id",
        item."SubOrderId",
        item."VariantId",
        ROW_NUMBER() OVER (
            PARTITION BY item."SubOrderId", item."VariantId"
            ORDER BY item."CreatedAt", item."Id"
        ) AS row_num,
        SUM(item."Quantity") OVER (
            PARTITION BY item."SubOrderId", item."VariantId"
        ) AS total_quantity,
        SUM(item."Total") OVER (
            PARTITION BY item."SubOrderId", item."VariantId"
        ) AS total_price
    FROM "CommerceHubSubOrderItems" AS item
    WHERE item."IsDeleted" = false
)
UPDATE "CommerceHubSubOrderItems" AS target
SET
    "Quantity" = source.total_quantity,
    "Total" = source.total_price,
    "UnitPrice" = CASE
        WHEN source.total_quantity > 0 THEN ROUND(source.total_price / source.total_quantity, 2)
        ELSE target."UnitPrice"
    END,
    "UpdatedAt" = NOW()
FROM ranked_items AS source
WHERE target."Id" = source."Id"
  AND source.row_num = 1
  AND target."IsDeleted" = false;

WITH ranked_items AS (
    SELECT
        item."Id",
        ROW_NUMBER() OVER (
            PARTITION BY item."SubOrderId", item."VariantId"
            ORDER BY item."CreatedAt", item."Id"
        ) AS row_num
    FROM "CommerceHubSubOrderItems" AS item
    WHERE item."IsDeleted" = false
)
UPDATE "CommerceHubSubOrderItems" AS target
SET
    "IsDeleted" = true,
    "UpdatedAt" = NOW()
FROM ranked_items AS source
WHERE target."Id" = source."Id"
  AND source.row_num > 1
  AND target."IsDeleted" = false;

WITH recalculated_suborders AS (
    SELECT
        sub_order."Id" AS "SubOrderId",
        COALESCE(SUM(item."Total"), 0) AS "Subtotal"
    FROM "CommerceHubSubOrders" AS sub_order
    LEFT JOIN "CommerceHubSubOrderItems" AS item
        ON item."SubOrderId" = sub_order."Id"
       AND item."IsDeleted" = false
    WHERE sub_order."IsDeleted" = false
    GROUP BY sub_order."Id"
)
UPDATE "CommerceHubSubOrders" AS target
SET
    "Subtotal" = source."Subtotal",
    "Total" = source."Subtotal" + target."Shipping",
    "UpdatedAt" = NOW()
FROM recalculated_suborders AS source
WHERE target."Id" = source."SubOrderId"
  AND target."IsDeleted" = false;

WITH recalculated_orders AS (
    SELECT
        marketplace_order."Id" AS "MarketplaceOrderId",
        COALESCE(SUM(sub_order."Subtotal"), 0) AS "Subtotal",
        COALESCE(SUM(sub_order."Shipping"), 0) AS "ShippingTotal"
    FROM "CommerceHubMarketplaceOrders" AS marketplace_order
    LEFT JOIN "CommerceHubSubOrders" AS sub_order
        ON sub_order."MarketplaceOrderId" = marketplace_order."Id"
       AND sub_order."IsDeleted" = false
    WHERE marketplace_order."IsDeleted" = false
    GROUP BY marketplace_order."Id"
)
UPDATE "CommerceHubMarketplaceOrders" AS target
SET
    "Subtotal" = source."Subtotal",
    "ShippingTotal" = source."ShippingTotal",
    "TotalAmount" = source."Subtotal" + source."ShippingTotal",
    "UpdatedAt" = NOW()
FROM recalculated_orders AS source
WHERE target."Id" = source."MarketplaceOrderId"
  AND target."IsDeleted" = false;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubSubOrderItems_SubOrderId_VariantId"
    ON "CommerceHubSubOrderItems" ("SubOrderId", "VariantId")
    WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubSubOrderStatusHistory_SubOrderId"
    ON "CommerceHubSubOrderStatusHistory" ("SubOrderId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubSubOrderStatusHistory_SubOrderId_ChangedAtUtc"
    ON "CommerceHubSubOrderStatusHistory" ("SubOrderId", "ChangedAtUtc" DESC);

COMMIT;

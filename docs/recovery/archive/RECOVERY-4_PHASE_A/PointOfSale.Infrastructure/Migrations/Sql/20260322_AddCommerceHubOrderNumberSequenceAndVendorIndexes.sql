BEGIN;

DO $$
DECLARE max_order_number bigint;
BEGIN
    CREATE SEQUENCE IF NOT EXISTS "CommerceHubMarketplaceOrderNumberSeq"
        START WITH 1
        INCREMENT BY 1
        MINVALUE 1;

    SELECT COALESCE(MAX((regexp_match("OrderNumber", '^ORD-CH-(\d+)$'))[1]::bigint), 0)
    INTO max_order_number
    FROM "CommerceHubMarketplaceOrders"
    WHERE "OrderNumber" ~ '^ORD-CH-\d+$';

    IF max_order_number > 0 THEN
        PERFORM setval('"CommerceHubMarketplaceOrderNumberSeq"', max_order_number, true);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubSubOrders_StoreId_CreatedAt"
    ON "CommerceHubSubOrders" ("StoreId", "CreatedAt" DESC);

COMMIT;

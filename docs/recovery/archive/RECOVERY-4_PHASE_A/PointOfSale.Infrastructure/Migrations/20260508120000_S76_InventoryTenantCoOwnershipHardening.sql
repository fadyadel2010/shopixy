-- S76: Inventory tenant co-ownership hardening.
--
-- PostgreSQL is the inventory authority. These constraints make the tenant
-- ownership assumptions for stock rows, stock movements, and stock transfers
-- enforceable at the database boundary, including raw SQL and future bulk paths.

DO $$
DECLARE
    mismatch_count integer;
BEGIN
    SELECT COUNT(*)
    INTO mismatch_count
    FROM "ProductVariantStocks" stock
    LEFT JOIN "Warehouses" warehouse
      ON warehouse."Id" = stock."WarehouseId"
    WHERE stock."IsDeleted" = false
      AND (
        warehouse."Id" IS NULL OR
        warehouse."TenantId" <> stock."TenantId" OR
        warehouse."IsDeleted" = true
      );

    IF mismatch_count > 0 THEN
        RAISE EXCEPTION 'S76 preflight failed: ProductVariantStocks has % warehouse tenant mismatches', mismatch_count
            USING ERRCODE = '23514';
    END IF;

    SELECT COUNT(*)
    INTO mismatch_count
    FROM "ProductVariantStocks" stock
    LEFT JOIN "ProductVariants" variant
      ON variant."Id" = stock."VariantId"
    WHERE stock."IsDeleted" = false
      AND (
        variant."Id" IS NULL OR
        variant."TenantId" <> stock."TenantId" OR
        variant."IsDeleted" = true
      );

    IF mismatch_count > 0 THEN
        RAISE EXCEPTION 'S76 preflight failed: ProductVariantStocks has % variant tenant mismatches', mismatch_count
            USING ERRCODE = '23514';
    END IF;

    SELECT COUNT(*)
    INTO mismatch_count
    FROM "StockMovements" movement
    LEFT JOIN "Warehouses" warehouse
      ON warehouse."Id" = movement."WarehouseId"
    WHERE movement."IsDeleted" = false
      AND (
        warehouse."Id" IS NULL OR
        warehouse."TenantId" <> movement."TenantId" OR
        warehouse."IsDeleted" = true
      );

    IF mismatch_count > 0 THEN
        RAISE EXCEPTION 'S76 preflight failed: StockMovements has % warehouse tenant mismatches', mismatch_count
            USING ERRCODE = '23514';
    END IF;

    SELECT COUNT(*)
    INTO mismatch_count
    FROM "StockMovements" movement
    LEFT JOIN "Warehouses" warehouse
      ON warehouse."Id" = movement."ToWarehouseId"
    WHERE movement."IsDeleted" = false
      AND movement."ToWarehouseId" IS NOT NULL
      AND (
        warehouse."Id" IS NULL OR
        warehouse."TenantId" <> movement."TenantId" OR
        warehouse."IsDeleted" = true
      );

    IF mismatch_count > 0 THEN
        RAISE EXCEPTION 'S76 preflight failed: StockMovements has % target warehouse tenant mismatches', mismatch_count
            USING ERRCODE = '23514';
    END IF;

    SELECT COUNT(*)
    INTO mismatch_count
    FROM "StockMovements" movement
    LEFT JOIN "Products" product
      ON product."Id" = movement."ProductId"
    WHERE movement."IsDeleted" = false
      AND (
        product."Id" IS NULL OR
        product."TenantId" <> movement."TenantId" OR
        product."IsDeleted" = true
      );

    IF mismatch_count > 0 THEN
        RAISE EXCEPTION 'S76 preflight failed: StockMovements has % product tenant mismatches', mismatch_count
            USING ERRCODE = '23514';
    END IF;

    SELECT COUNT(*)
    INTO mismatch_count
    FROM "StockMovements" movement
    LEFT JOIN "ProductVariants" variant
      ON variant."Id" = movement."ProductVariantId"
    WHERE movement."IsDeleted" = false
      AND movement."ProductVariantId" IS NOT NULL
      AND (
        variant."Id" IS NULL OR
        variant."TenantId" <> movement."TenantId" OR
        variant."ProductId" <> movement."ProductId" OR
        variant."IsDeleted" = true
      );

    IF mismatch_count > 0 THEN
        RAISE EXCEPTION 'S76 preflight failed: StockMovements has % variant tenant/product mismatches', mismatch_count
            USING ERRCODE = '23514';
    END IF;

    SELECT COUNT(*)
    INTO mismatch_count
    FROM "StockTransfers" transfer
    LEFT JOIN "Warehouses" source_warehouse
      ON source_warehouse."Id" = transfer."FromWarehouseId"
    LEFT JOIN "Warehouses" target_warehouse
      ON target_warehouse."Id" = transfer."ToWarehouseId"
    WHERE transfer."IsDeleted" = false
      AND (
        source_warehouse."Id" IS NULL OR
        source_warehouse."TenantId" <> transfer."TenantId" OR
        source_warehouse."IsDeleted" = true OR
        target_warehouse."Id" IS NULL OR
        target_warehouse."TenantId" <> transfer."TenantId" OR
        target_warehouse."IsDeleted" = true
      );

    IF mismatch_count > 0 THEN
        RAISE EXCEPTION 'S76 preflight failed: StockTransfers has % warehouse tenant mismatches', mismatch_count
            USING ERRCODE = '23514';
    END IF;

    SELECT COUNT(*)
    INTO mismatch_count
    FROM "StockTransferItems" item
    JOIN "StockTransfers" transfer
      ON transfer."Id" = item."StockTransferId"
    LEFT JOIN "Products" product
      ON product."Id" = item."ProductId"
    WHERE transfer."IsDeleted" = false
      AND item."IsDeleted" = false
      AND (
        product."Id" IS NULL OR
        product."TenantId" <> transfer."TenantId" OR
        product."IsDeleted" = true
      );

    IF mismatch_count > 0 THEN
        RAISE EXCEPTION 'S76 preflight failed: StockTransferItems has % product tenant mismatches', mismatch_count
            USING ERRCODE = '23514';
    END IF;

    SELECT COUNT(*)
    INTO mismatch_count
    FROM "StockTransferItems" item
    JOIN "StockTransfers" transfer
      ON transfer."Id" = item."StockTransferId"
    LEFT JOIN "ProductVariants" variant
      ON variant."Id" = item."ProductVariantId"
    WHERE transfer."IsDeleted" = false
      AND item."IsDeleted" = false
      AND item."ProductVariantId" IS NOT NULL
      AND (
        variant."Id" IS NULL OR
        variant."TenantId" <> transfer."TenantId" OR
        variant."ProductId" <> item."ProductId" OR
        variant."IsDeleted" = true
      );

    IF mismatch_count > 0 THEN
        RAISE EXCEPTION 'S76 preflight failed: StockTransferItems has % variant tenant/product mismatches', mismatch_count
            USING ERRCODE = '23514';
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'UX_Warehouses_TenantId_Id'
    ) THEN
        ALTER TABLE "Warehouses"
            ADD CONSTRAINT "UX_Warehouses_TenantId_Id"
            UNIQUE ("TenantId", "Id");
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'UX_ProductVariants_TenantId_Id'
    ) THEN
        ALTER TABLE "ProductVariants"
            ADD CONSTRAINT "UX_ProductVariants_TenantId_Id"
            UNIQUE ("TenantId", "Id");
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'UX_Products_TenantId_Id'
    ) THEN
        ALTER TABLE "Products"
            ADD CONSTRAINT "UX_Products_TenantId_Id"
            UNIQUE ("TenantId", "Id");
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'UX_StockTransfers_TenantId_Id'
    ) THEN
        ALTER TABLE "StockTransfers"
            ADD CONSTRAINT "UX_StockTransfers_TenantId_Id"
            UNIQUE ("TenantId", "Id");
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_ProductVariantStocks_Warehouses_TenantId_WarehouseId'
    ) THEN
        ALTER TABLE "ProductVariantStocks"
            ADD CONSTRAINT "FK_ProductVariantStocks_Warehouses_TenantId_WarehouseId"
            FOREIGN KEY ("TenantId", "WarehouseId")
            REFERENCES "Warehouses" ("TenantId", "Id")
            NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_ProductVariantStocks_ProductVariants_TenantId_VariantId'
    ) THEN
        ALTER TABLE "ProductVariantStocks"
            ADD CONSTRAINT "FK_ProductVariantStocks_ProductVariants_TenantId_VariantId"
            FOREIGN KEY ("TenantId", "VariantId")
            REFERENCES "ProductVariants" ("TenantId", "Id")
            NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_StockMovements_Warehouses_TenantId_WarehouseId'
    ) THEN
        ALTER TABLE "StockMovements"
            ADD CONSTRAINT "FK_StockMovements_Warehouses_TenantId_WarehouseId"
            FOREIGN KEY ("TenantId", "WarehouseId")
            REFERENCES "Warehouses" ("TenantId", "Id")
            NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_StockMovements_Warehouses_TenantId_ToWarehouseId'
    ) THEN
        ALTER TABLE "StockMovements"
            ADD CONSTRAINT "FK_StockMovements_Warehouses_TenantId_ToWarehouseId"
            FOREIGN KEY ("TenantId", "ToWarehouseId")
            REFERENCES "Warehouses" ("TenantId", "Id")
            NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_StockMovements_Products_TenantId_ProductId'
    ) THEN
        ALTER TABLE "StockMovements"
            ADD CONSTRAINT "FK_StockMovements_Products_TenantId_ProductId"
            FOREIGN KEY ("TenantId", "ProductId")
            REFERENCES "Products" ("TenantId", "Id")
            NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_StockMovements_ProductVariants_TenantId_ProductVariantId'
    ) THEN
        ALTER TABLE "StockMovements"
            ADD CONSTRAINT "FK_StockMovements_ProductVariants_TenantId_ProductVariantId"
            FOREIGN KEY ("TenantId", "ProductVariantId")
            REFERENCES "ProductVariants" ("TenantId", "Id")
            NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_StockTransfers_Warehouses_TenantId_FromWarehouseId'
    ) THEN
        ALTER TABLE "StockTransfers"
            ADD CONSTRAINT "FK_StockTransfers_Warehouses_TenantId_FromWarehouseId"
            FOREIGN KEY ("TenantId", "FromWarehouseId")
            REFERENCES "Warehouses" ("TenantId", "Id")
            NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_StockTransfers_Warehouses_TenantId_ToWarehouseId'
    ) THEN
        ALTER TABLE "StockTransfers"
            ADD CONSTRAINT "FK_StockTransfers_Warehouses_TenantId_ToWarehouseId"
            FOREIGN KEY ("TenantId", "ToWarehouseId")
            REFERENCES "Warehouses" ("TenantId", "Id")
            NOT VALID;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_ProductVariantStocks_Warehouses_TenantId_WarehouseId'
          AND convalidated = false
    ) THEN
        ALTER TABLE "ProductVariantStocks"
            VALIDATE CONSTRAINT "FK_ProductVariantStocks_Warehouses_TenantId_WarehouseId";
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_ProductVariantStocks_ProductVariants_TenantId_VariantId'
          AND convalidated = false
    ) THEN
        ALTER TABLE "ProductVariantStocks"
            VALIDATE CONSTRAINT "FK_ProductVariantStocks_ProductVariants_TenantId_VariantId";
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_StockMovements_Warehouses_TenantId_WarehouseId'
          AND convalidated = false
    ) THEN
        ALTER TABLE "StockMovements"
            VALIDATE CONSTRAINT "FK_StockMovements_Warehouses_TenantId_WarehouseId";
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_StockMovements_Warehouses_TenantId_ToWarehouseId'
          AND convalidated = false
    ) THEN
        ALTER TABLE "StockMovements"
            VALIDATE CONSTRAINT "FK_StockMovements_Warehouses_TenantId_ToWarehouseId";
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_StockMovements_Products_TenantId_ProductId'
          AND convalidated = false
    ) THEN
        ALTER TABLE "StockMovements"
            VALIDATE CONSTRAINT "FK_StockMovements_Products_TenantId_ProductId";
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_StockMovements_ProductVariants_TenantId_ProductVariantId'
          AND convalidated = false
    ) THEN
        ALTER TABLE "StockMovements"
            VALIDATE CONSTRAINT "FK_StockMovements_ProductVariants_TenantId_ProductVariantId";
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_StockTransfers_Warehouses_TenantId_FromWarehouseId'
          AND convalidated = false
    ) THEN
        ALTER TABLE "StockTransfers"
            VALIDATE CONSTRAINT "FK_StockTransfers_Warehouses_TenantId_FromWarehouseId";
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_StockTransfers_Warehouses_TenantId_ToWarehouseId'
          AND convalidated = false
    ) THEN
        ALTER TABLE "StockTransfers"
            VALIDATE CONSTRAINT "FK_StockTransfers_Warehouses_TenantId_ToWarehouseId";
    END IF;
END $$;

CREATE OR REPLACE FUNCTION pos_validate_product_variant_stock_tenant()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM "Warehouses" warehouse
        WHERE warehouse."Id" = NEW."WarehouseId"
          AND warehouse."TenantId" = NEW."TenantId"
          AND warehouse."IsDeleted" = false
    ) THEN
        RAISE EXCEPTION 'ProductVariantStock warehouse tenant mismatch. TenantId %, WarehouseId %',
            NEW."TenantId", NEW."WarehouseId"
            USING ERRCODE = '23514';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM "ProductVariants" variant
        WHERE variant."Id" = NEW."VariantId"
          AND variant."TenantId" = NEW."TenantId"
          AND variant."IsDeleted" = false
    ) THEN
        RAISE EXCEPTION 'ProductVariantStock variant tenant mismatch. TenantId %, VariantId %',
            NEW."TenantId", NEW."VariantId"
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION pos_validate_stock_movement_tenant()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM "Warehouses" warehouse
        WHERE warehouse."Id" = NEW."WarehouseId"
          AND warehouse."TenantId" = NEW."TenantId"
          AND warehouse."IsDeleted" = false
    ) THEN
        RAISE EXCEPTION 'StockMovement warehouse tenant mismatch. TenantId %, WarehouseId %',
            NEW."TenantId", NEW."WarehouseId"
            USING ERRCODE = '23514';
    END IF;

    IF NEW."ToWarehouseId" IS NOT NULL AND NOT EXISTS (
        SELECT 1
        FROM "Warehouses" warehouse
        WHERE warehouse."Id" = NEW."ToWarehouseId"
          AND warehouse."TenantId" = NEW."TenantId"
          AND warehouse."IsDeleted" = false
    ) THEN
        RAISE EXCEPTION 'StockMovement target warehouse tenant mismatch. TenantId %, ToWarehouseId %',
            NEW."TenantId", NEW."ToWarehouseId"
            USING ERRCODE = '23514';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM "Products" product
        WHERE product."Id" = NEW."ProductId"
          AND product."TenantId" = NEW."TenantId"
          AND product."IsDeleted" = false
    ) THEN
        RAISE EXCEPTION 'StockMovement product tenant mismatch. TenantId %, ProductId %',
            NEW."TenantId", NEW."ProductId"
            USING ERRCODE = '23514';
    END IF;

    IF NEW."ProductVariantId" IS NOT NULL AND NOT EXISTS (
        SELECT 1
        FROM "ProductVariants" variant
        WHERE variant."Id" = NEW."ProductVariantId"
          AND variant."TenantId" = NEW."TenantId"
          AND variant."ProductId" = NEW."ProductId"
          AND variant."IsDeleted" = false
    ) THEN
        RAISE EXCEPTION 'StockMovement variant tenant/product mismatch. TenantId %, ProductId %, ProductVariantId %',
            NEW."TenantId", NEW."ProductId", NEW."ProductVariantId"
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION pos_validate_stock_transfer_tenant()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM "Warehouses" warehouse
        WHERE warehouse."Id" = NEW."FromWarehouseId"
          AND warehouse."TenantId" = NEW."TenantId"
          AND warehouse."IsDeleted" = false
    ) THEN
        RAISE EXCEPTION 'StockTransfer source warehouse tenant mismatch. TenantId %, FromWarehouseId %',
            NEW."TenantId", NEW."FromWarehouseId"
            USING ERRCODE = '23514';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM "Warehouses" warehouse
        WHERE warehouse."Id" = NEW."ToWarehouseId"
          AND warehouse."TenantId" = NEW."TenantId"
          AND warehouse."IsDeleted" = false
    ) THEN
        RAISE EXCEPTION 'StockTransfer target warehouse tenant mismatch. TenantId %, ToWarehouseId %',
            NEW."TenantId", NEW."ToWarehouseId"
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION pos_validate_stock_transfer_item_tenant()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    transfer_tenant_id uuid;
BEGIN
    SELECT transfer."TenantId"
    INTO transfer_tenant_id
    FROM "StockTransfers" transfer
    WHERE transfer."Id" = NEW."StockTransferId"
      AND transfer."IsDeleted" = false;

    IF transfer_tenant_id IS NULL THEN
        RAISE EXCEPTION 'StockTransferItem references missing transfer %. StockTransferItemId %',
            NEW."StockTransferId", NEW."Id"
            USING ERRCODE = '23514';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM "Products" product
        WHERE product."Id" = NEW."ProductId"
          AND product."TenantId" = transfer_tenant_id
          AND product."IsDeleted" = false
    ) THEN
        RAISE EXCEPTION 'StockTransferItem product tenant mismatch. TransferTenantId %, ProductId %',
            transfer_tenant_id, NEW."ProductId"
            USING ERRCODE = '23514';
    END IF;

    IF NEW."ProductVariantId" IS NOT NULL AND NOT EXISTS (
        SELECT 1
        FROM "ProductVariants" variant
        WHERE variant."Id" = NEW."ProductVariantId"
          AND variant."TenantId" = transfer_tenant_id
          AND variant."ProductId" = NEW."ProductId"
          AND variant."IsDeleted" = false
    ) THEN
        RAISE EXCEPTION 'StockTransferItem variant tenant/product mismatch. TransferTenantId %, ProductId %, ProductVariantId %',
            transfer_tenant_id, NEW."ProductId", NEW."ProductVariantId"
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_product_variant_stocks_tenant_coownership
ON "ProductVariantStocks";
CREATE CONSTRAINT TRIGGER trg_product_variant_stocks_tenant_coownership
AFTER INSERT OR UPDATE OF "TenantId", "WarehouseId", "VariantId"
ON "ProductVariantStocks"
DEFERRABLE INITIALLY IMMEDIATE
FOR EACH ROW
EXECUTE FUNCTION pos_validate_product_variant_stock_tenant();

DROP TRIGGER IF EXISTS trg_stock_movements_tenant_coownership
ON "StockMovements";
CREATE CONSTRAINT TRIGGER trg_stock_movements_tenant_coownership
AFTER INSERT OR UPDATE OF "TenantId", "WarehouseId", "ToWarehouseId", "ProductId", "ProductVariantId"
ON "StockMovements"
DEFERRABLE INITIALLY IMMEDIATE
FOR EACH ROW
EXECUTE FUNCTION pos_validate_stock_movement_tenant();

DROP TRIGGER IF EXISTS trg_stock_transfers_tenant_coownership
ON "StockTransfers";
CREATE CONSTRAINT TRIGGER trg_stock_transfers_tenant_coownership
AFTER INSERT OR UPDATE OF "TenantId", "FromWarehouseId", "ToWarehouseId"
ON "StockTransfers"
DEFERRABLE INITIALLY IMMEDIATE
FOR EACH ROW
EXECUTE FUNCTION pos_validate_stock_transfer_tenant();

DROP TRIGGER IF EXISTS trg_stock_transfer_items_tenant_coownership
ON "StockTransferItems";
CREATE CONSTRAINT TRIGGER trg_stock_transfer_items_tenant_coownership
AFTER INSERT OR UPDATE OF "StockTransferId", "ProductId", "ProductVariantId"
ON "StockTransferItems"
DEFERRABLE INITIALLY IMMEDIATE
FOR EACH ROW
EXECUTE FUNCTION pos_validate_stock_transfer_item_tenant();

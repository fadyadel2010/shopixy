START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115200647_AddStoreConfigOptimisticConcurrency') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260115200647_AddStoreConfigOptimisticConcurrency', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE TABLE "CouponUsages" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CouponId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "CustomerId" uuid,
        "CouponCode" text NOT NULL,
        "DiscountAmount" numeric(18,2) NOT NULL,
        "OrderSubtotal" numeric(18,2) NOT NULL,
        "UsedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CouponUsages" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CouponUsages_Coupons_CouponId" FOREIGN KEY ("CouponId") REFERENCES "Coupons" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CouponUsages_OnlineCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "OnlineCustomers" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_CouponUsages_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE TABLE "StoreConfigAuditEntries" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "UserId" uuid,
        "UserName" character varying(256),
        "ActionType" character varying(50) NOT NULL,
        "Target" character varying(100) NOT NULL,
        "BeforeSnapshotJson" text,
        "AfterSnapshotJson" text,
        "Version" integer,
        "TraceId" character varying(128),
        "IpAddress" character varying(45),
        "UserAgent" text,
        "TimestampUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_StoreConfigAuditEntries" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_CouponUsages_CouponId" ON "CouponUsages" ("CouponId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_CouponUsages_CustomerId" ON "CouponUsages" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_CouponUsages_OrderId" ON "CouponUsages" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_CouponUsages_TenantId_CouponId" ON "CouponUsages" ("TenantId", "CouponId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_CouponUsages_TenantId_CouponId_CustomerId" ON "CouponUsages" ("TenantId", "CouponId", "CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE UNIQUE INDEX "IX_CouponUsages_TenantId_OrderId" ON "CouponUsages" ("TenantId", "OrderId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_StoreConfigAuditEntries_ActionType" ON "StoreConfigAuditEntries" ("ActionType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_StoreConfigAuditEntries_TenantId" ON "StoreConfigAuditEntries" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_StoreConfigAuditEntries_TenantId_TimestampUtc" ON "StoreConfigAuditEntries" ("TenantId", "TimestampUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_StoreConfigAuditEntries_TimestampUtc" ON "StoreConfigAuditEntries" ("TimestampUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260115214438_AddCouponUsagesTable', '9.0.1');
    END IF;
END $EF$;
COMMIT;


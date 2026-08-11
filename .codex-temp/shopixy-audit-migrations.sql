START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809185514_SHOPIXY_CheckoutFieldDefinitions') THEN
    CREATE TABLE "CheckoutFieldDefinitions" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "FieldKind" character varying(16) NOT NULL,
        "FieldType" character varying(32) NOT NULL,
        "SystemFieldKey" character varying(64),
        "Label" character varying(120) NOT NULL,
        "HelpText" character varying(500),
        "IsEnabled" boolean NOT NULL,
        "IsRequired" boolean NOT NULL,
        "DisplayOrder" integer NOT NULL,
        "ConfigurationSchemaVersion" integer NOT NULL DEFAULT 1,
        "ConfigurationJson" jsonb NOT NULL DEFAULT '{}',
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CheckoutFieldDefinitions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CheckoutFieldDefinitions_RequiredEnabled" CHECK (NOT "IsRequired" OR "IsEnabled"),
        CONSTRAINT "CK_CheckoutFieldDefinitions_SystemFieldKey" CHECK (("FieldKind" = 'System' AND "SystemFieldKey" IS NOT NULL AND LENGTH(TRIM("SystemFieldKey")) > 0) OR ("FieldKind" = 'Custom' AND "SystemFieldKey" IS NULL)),
        CONSTRAINT "FK_CheckoutFieldDefinitions_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809185514_SHOPIXY_CheckoutFieldDefinitions') THEN
    CREATE INDEX "IX_CheckoutFieldDefinitions_TenantId" ON "CheckoutFieldDefinitions" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809185514_SHOPIXY_CheckoutFieldDefinitions') THEN
    CREATE INDEX "IX_CheckoutFieldDefinitions_TenantId_DisplayOrder" ON "CheckoutFieldDefinitions" ("TenantId", "DisplayOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809185514_SHOPIXY_CheckoutFieldDefinitions') THEN
    CREATE INDEX "IX_CheckoutFieldDefinitions_TenantId_IsDeleted" ON "CheckoutFieldDefinitions" ("TenantId", "IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809185514_SHOPIXY_CheckoutFieldDefinitions') THEN
    CREATE UNIQUE INDEX "UX_CheckoutFieldDefinitions_Tenant_SystemFieldKey_Active" ON "CheckoutFieldDefinitions" ("TenantId", "SystemFieldKey") WHERE "IsDeleted" = false AND "SystemFieldKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809185514_SHOPIXY_CheckoutFieldDefinitions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260809185514_SHOPIXY_CheckoutFieldDefinitions', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots') THEN
    CREATE TABLE "OrderCheckoutFieldSnapshots" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "CheckoutFieldDefinitionId" uuid NOT NULL,
        "FieldKind" character varying(16) NOT NULL,
        "FieldType" character varying(32) NOT NULL,
        "SystemFieldKey" character varying(64),
        "Label" character varying(120) NOT NULL,
        "HelpText" character varying(500),
        "IsRequired" boolean NOT NULL,
        "IsEnabled" boolean NOT NULL,
        "DisplayOrder" integer NOT NULL,
        "ConfigurationSchemaVersion" integer NOT NULL,
        "ConfigurationJson" jsonb NOT NULL DEFAULT '{}',
        "ValueJson" jsonb NOT NULL DEFAULT 'null',
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_OrderCheckoutFieldSnapshots" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_OrderCheckoutFieldSnapshots_ConfigurationSchemaVersion_Posi~" CHECK ("ConfigurationSchemaVersion" > 0),
        CONSTRAINT "CK_OrderCheckoutFieldSnapshots_SystemFieldKey" CHECK (("FieldKind" = 'System' AND "SystemFieldKey" IS NOT NULL AND LENGTH(TRIM("SystemFieldKey")) > 0) OR ("FieldKind" = 'Custom' AND "SystemFieldKey" IS NULL)),
        CONSTRAINT "FK_OrderCheckoutFieldSnapshots_Orders_TenantId_OrderId" FOREIGN KEY ("TenantId", "OrderId") REFERENCES "Orders" ("TenantId", "Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots') THEN
    CREATE INDEX "IX_OrderCheckoutFieldSnapshots_OrderId" ON "OrderCheckoutFieldSnapshots" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots') THEN
    CREATE INDEX "IX_OrderCheckoutFieldSnapshots_TenantId_OrderId" ON "OrderCheckoutFieldSnapshots" ("TenantId", "OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots') THEN
    CREATE INDEX "IX_OrderCheckoutFieldSnapshots_TenantId_OrderId_DisplayOrder" ON "OrderCheckoutFieldSnapshots" ("TenantId", "OrderId", "DisplayOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots') THEN
    CREATE UNIQUE INDEX "UX_OrderCheckoutFieldSnapshots_Tenant_Order_FieldDefinition_Active" ON "OrderCheckoutFieldSnapshots" ("TenantId", "OrderId", "CheckoutFieldDefinitionId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809203807_SHOPIXY_CheckoutAttachments') THEN
    CREATE TABLE "CheckoutAttachments" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CartId" uuid NOT NULL,
        "OwnerId" character varying(128) NOT NULL,
        "OwnerType" character varying(32) NOT NULL,
        "SessionId" character varying(128),
        "OnlineCustomerId" uuid,
        "CheckoutFieldDefinitionId" uuid NOT NULL,
        "OrderId" uuid,
        "ObjectKey" character varying(500) NOT NULL,
        "OriginalFileName" character varying(255) NOT NULL,
        "ContentType" character varying(255) NOT NULL,
        "MediaType" character varying(32) NOT NULL,
        "SizeBytes" bigint NOT NULL,
        "ExpectedSizeBytes" bigint NOT NULL,
        "Status" character varying(32) NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "UploadedAtUtc" timestamp with time zone,
        "AttachedAtUtc" timestamp with time zone,
        "MetadataJson" jsonb NOT NULL DEFAULT '{}',
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CheckoutAttachments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CheckoutAttachments_Orders_TenantId_OrderId" FOREIGN KEY ("TenantId", "OrderId") REFERENCES "Orders" ("TenantId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809203807_SHOPIXY_CheckoutAttachments') THEN
    CREATE INDEX "IX_CheckoutAttachments_Status_ExpiresAtUtc" ON "CheckoutAttachments" ("Status", "ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809203807_SHOPIXY_CheckoutAttachments') THEN
    CREATE INDEX "IX_CheckoutAttachments_Tenant_Cart_Owner_Field" ON "CheckoutAttachments" ("TenantId", "CartId", "OwnerId", "CheckoutFieldDefinitionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809203807_SHOPIXY_CheckoutAttachments') THEN
    CREATE INDEX "IX_CheckoutAttachments_Tenant_OrderId" ON "CheckoutAttachments" ("TenantId", "OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809203807_SHOPIXY_CheckoutAttachments') THEN
    CREATE UNIQUE INDEX "UX_CheckoutAttachments_ObjectKey" ON "CheckoutAttachments" ("ObjectKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809203807_SHOPIXY_CheckoutAttachments') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260809203807_SHOPIXY_CheckoutAttachments', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement') THEN
    ALTER TABLE "SslCertificateOperations" DROP CONSTRAINT "FK_SslCertificateOperations_DomainId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement') THEN
    ALTER TABLE "SslCertificates" DROP CONSTRAINT "FK_SslCertificates_DomainId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement') THEN
    ALTER TABLE "SslCertificates" DROP CONSTRAINT "CK_SslCertificates_Status_Canonical";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement') THEN
    DROP INDEX "IX_SslCertificateOperations_DomainId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "CK_SslCertificates_Status_Canonical" CHECK ("Status" IN (0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement') THEN
    ALTER TABLE "SslCertificateOperations" ADD CONSTRAINT "FK_SslCertificateOperations_DomainId" FOREIGN KEY ("TenantId", "DomainId") REFERENCES "Domains" ("TenantId", "Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "FK_SslCertificates_DomainId" FOREIGN KEY ("TenantId", "DomainId") REFERENCES "Domains" ("TenantId", "Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement', '9.0.1');
    END IF;
END $EF$;
COMMIT;


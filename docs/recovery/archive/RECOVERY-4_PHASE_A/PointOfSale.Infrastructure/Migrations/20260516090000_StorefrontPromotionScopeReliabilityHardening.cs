using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260516090000_StorefrontPromotionScopeReliabilityHardening")]
    public partial class StorefrontPromotionScopeReliabilityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Promotions"
                    ADD COLUMN IF NOT EXISTS "ProjectionMutationVersion" bigint NOT NULL DEFAULT 0;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "ProductRequestIdempotency"
                (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "Key" character varying(200) NOT NULL,
                    "Operation" character varying(32) NOT NULL,
                    "ProductId" uuid NOT NULL,
                    "ProductVariantId" uuid NULL,
                    "RequestFingerprint" character varying(64) NOT NULL,
                    "ResponseStatusCode" integer NULL,
                    "ResponseJson" text NULL,
                    "CompletedAtUtc" timestamp with time zone NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "CreatedBy" uuid NULL,
                    "UpdatedBy" uuid NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT false,
                    CONSTRAINT "PK_ProductRequestIdempotency" PRIMARY KEY ("Id")
                );
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductRequestIdempotency_TenantId_Key_Operation"
                    ON "ProductRequestIdempotency" ("TenantId", "Key", "Operation");
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_ProductRequestIdempotency_TenantId_ProductId"
                    ON "ProductRequestIdempotency" ("TenantId", "ProductId");
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_ProductRequestIdempotency_TenantId_ProductVariantId"
                    ON "ProductRequestIdempotency" ("TenantId", "ProductVariantId");
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "MarketplaceProjectionOutboxMessages"
                (
                    "Id" uuid NOT NULL,
                    "EventId" uuid NOT NULL,
                    "ConsumerName" character varying(200) NOT NULL,
                    "EventType" character varying(200) NOT NULL,
                    "ClrType" character varying(1000) NOT NULL,
                    "EntityId" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "CorrelationId" character varying(200) NOT NULL,
                    "EventVersion" integer NOT NULL,
                    "OccurredAtUtc" timestamp with time zone NOT NULL,
                    "PayloadJson" text NOT NULL,
                    "ProcessedAtUtc" timestamp with time zone NULL,
                    "ClaimedUntilUtc" timestamp with time zone NULL,
                    "AttemptCount" integer NOT NULL DEFAULT 0,
                    "LastError" character varying(2000) NULL,
                    "NextRetryAtUtc" timestamp with time zone NULL,
                    "IsFailed" boolean NOT NULL DEFAULT false,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "CreatedBy" uuid NULL,
                    "UpdatedBy" uuid NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT false,
                    CONSTRAINT "PK_MarketplaceProjectionOutboxMessages" PRIMARY KEY ("Id")
                );
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "MarketplaceProjectionOutboxMessages"
                    ADD COLUMN IF NOT EXISTS "ClrType" character varying(1000) NOT NULL DEFAULT '';
                ALTER TABLE "MarketplaceProjectionOutboxMessages"
                    ADD COLUMN IF NOT EXISTS "PayloadJson" text NOT NULL DEFAULT '{}';
                ALTER TABLE "MarketplaceProjectionOutboxMessages"
                    ADD COLUMN IF NOT EXISTS "EventVersion" integer NOT NULL DEFAULT 1;
                ALTER TABLE "MarketplaceProjectionOutboxMessages"
                    ADD COLUMN IF NOT EXISTS "ClaimedUntilUtc" timestamp with time zone NULL;
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_MarketplaceProjectionOutbox_Idempotency"
                    ON "MarketplaceProjectionOutboxMessages" ("EventId", "ConsumerName", "EventType")
                    WHERE "IsDeleted" = false;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_MarketplaceProjectionOutbox_ProcessedAtUtc"
                    ON "MarketplaceProjectionOutboxMessages" ("ProcessedAtUtc");
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_MarketplaceProjectionOutbox_TenantId_EntityId"
                    ON "MarketplaceProjectionOutboxMessages" ("TenantId", "EntityId");
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_MarketplaceProjectionOutbox_ProcessingStatus"
                    ON "MarketplaceProjectionOutboxMessages" ("TenantId", "ConsumerName", "ProcessedAtUtc", "NextRetryAtUtc", "ClaimedUntilUtc")
                    WHERE "ProcessedAtUtc" IS NULL AND "IsFailed" = false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_MarketplaceProjectionOutbox_ProcessingStatus";
                DROP INDEX IF EXISTS "IX_MarketplaceProjectionOutbox_TenantId_EntityId";
                DROP INDEX IF EXISTS "IX_MarketplaceProjectionOutbox_ProcessedAtUtc";
                DROP INDEX IF EXISTS "IX_MarketplaceProjectionOutbox_Idempotency";
                DROP TABLE IF EXISTS "MarketplaceProjectionOutboxMessages";
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_ProductRequestIdempotency_TenantId_ProductVariantId";
                DROP INDEX IF EXISTS "IX_ProductRequestIdempotency_TenantId_ProductId";
                DROP INDEX IF EXISTS "IX_ProductRequestIdempotency_TenantId_Key_Operation";
                DROP TABLE IF EXISTS "ProductRequestIdempotency";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "Promotions"
                    DROP COLUMN IF EXISTS "ProjectionMutationVersion";
                """);
        }
    }
}

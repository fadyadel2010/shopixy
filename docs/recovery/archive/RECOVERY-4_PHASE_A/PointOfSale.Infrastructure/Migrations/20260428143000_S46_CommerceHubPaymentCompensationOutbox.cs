using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260428143000_S46_CommerceHubPaymentCompensationOutbox")]
    public partial class S46_CommerceHubPaymentCompensationOutbox : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "CommerceHubCarts"
                    ADD COLUMN IF NOT EXISTS "CommerceHubCustomerId" uuid;

                ALTER TABLE "CommerceHubCheckoutIdempotency"
                    ADD COLUMN IF NOT EXISTS "CommerceHubCustomerId" uuid;
                """);

            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_CommerceHubPayments_Provider_ProviderIntentId";

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubPayments_Provider_ProviderIntentId"
                    ON "CommerceHubPayments" ("Provider", "ProviderIntentId")
                    WHERE "ProviderIntentId" IS NOT NULL;
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubCarts_TenantId_CommerceHubCustomerId_Active"
                    ON "CommerceHubCarts" ("TenantId", "CommerceHubCustomerId")
                    WHERE "CommerceHubCustomerId" IS NOT NULL AND "IsDeleted" = false;

                CREATE INDEX IF NOT EXISTS "IX_CommerceHubCheckoutIdempotency_CommerceHubCustomerId"
                    ON "CommerceHubCheckoutIdempotency" ("CommerceHubCustomerId");
                """);

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "CommerceHubPaymentCompensationOutboxMessages"
                (
                    "Id" uuid NOT NULL,
                    "PaymentId" uuid NOT NULL,
                    "OrderId" uuid NOT NULL,
                    "TenantId" uuid NULL,
                    "AffectedStoreIdsJson" character varying(2000) NOT NULL DEFAULT '[]',
                    "Provider" integer NOT NULL,
                    "ProviderIntentId" character varying(200) NULL,
                    "ProviderTransactionId" character varying(200) NULL,
                    "Amount" numeric(18,2) NOT NULL,
                    "Currency" character varying(10) NOT NULL,
                    "Reason" character varying(500) NOT NULL,
                    "IdempotencyKey" character varying(200) NOT NULL,
                    "CorrelationId" character varying(100) NULL,
                    "ProcessedAtUtc" timestamp with time zone NULL,
                    "AttemptCount" integer NOT NULL DEFAULT 0,
                    "LastError" character varying(2000) NULL,
                    "NextRetryAtUtc" timestamp with time zone NULL,
                    "ProcessingLeaseId" uuid NULL,
                    "ProcessingLeaseExpiresAtUtc" timestamp with time zone NULL,
                    "IsFailed" boolean NOT NULL DEFAULT false,
                    "GatewayReference" character varying(200) NULL,
                    "GatewayStatus" character varying(100) NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
                    "UpdatedAt" timestamp with time zone NULL,
                    "CreatedBy" uuid NULL,
                    "UpdatedBy" uuid NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT false,
                    CONSTRAINT "PK_CommerceHubPaymentCompensationOutboxMessages" PRIMARY KEY ("Id"),
                    CONSTRAINT "CK_CHPaymentCompOutbox_Amount" CHECK ("Amount" > 0)
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_CHPaymentCompOutbox_IdempotencyKey"
                    ON "CommerceHubPaymentCompensationOutboxMessages" ("IdempotencyKey");

                CREATE INDEX IF NOT EXISTS "IX_CHPaymentCompOutbox_Processing"
                    ON "CommerceHubPaymentCompensationOutboxMessages" ("IsFailed", "ProcessedAtUtc", "NextRetryAtUtc");

                CREATE INDEX IF NOT EXISTS "IX_CHPaymentCompOutbox_ProcessingLease"
                    ON "CommerceHubPaymentCompensationOutboxMessages" ("ProcessedAtUtc", "IsFailed", "NextRetryAtUtc", "ProcessingLeaseExpiresAtUtc", "CreatedAt", "Id");

                CREATE INDEX IF NOT EXISTS "IX_CHPaymentCompOutbox_PaymentId"
                    ON "CommerceHubPaymentCompensationOutboxMessages" ("PaymentId");

                CREATE INDEX IF NOT EXISTS "IX_CHPaymentCompOutbox_PaymentOrdering"
                    ON "CommerceHubPaymentCompensationOutboxMessages" ("PaymentId", "ProcessedAtUtc", "CreatedAt", "Id");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS "CommerceHubPaymentCompensationOutboxMessages";

                DROP INDEX IF EXISTS "UX_CommerceHubPayments_Provider_ProviderIntentId";
                CREATE INDEX IF NOT EXISTS "IX_CommerceHubPayments_Provider_ProviderIntentId"
                    ON "CommerceHubPayments" ("Provider", "ProviderIntentId")
                    WHERE "ProviderIntentId" IS NOT NULL;

                DROP INDEX IF EXISTS "UX_CommerceHubCarts_TenantId_CommerceHubCustomerId_Active";
                DROP INDEX IF EXISTS "IX_CommerceHubCheckoutIdempotency_CommerceHubCustomerId";

                ALTER TABLE "CommerceHubCheckoutIdempotency"
                    DROP COLUMN IF EXISTS "CommerceHubCustomerId";

                ALTER TABLE "CommerceHubCarts"
                    DROP COLUMN IF EXISTS "CommerceHubCustomerId";
                """);
        }
    }
}

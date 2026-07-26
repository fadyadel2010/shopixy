using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260423093000_S17_SettlementIntegrity")]
    public partial class S17SettlementIntegrity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "PaymentIntents"
                    ADD COLUMN IF NOT EXISTS "PayoutBatchId" character varying(200);

                CREATE TABLE IF NOT EXISTS "PaymentSettlementBatches" (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "SettlementType" character varying(32) NOT NULL,
                    "SettlementId" character varying(200) NOT NULL,
                    "Provider" character varying(100),
                    "PayoutBatchId" character varying(200),
                    "GrossAmount" numeric(18,2) NOT NULL,
                    "Fees" numeric(18,2) NOT NULL,
                    "Currency" character varying(3) NOT NULL,
                    "BankReference" character varying(200),
                    "OrderCount" integer NOT NULL,
                    "SettledAtUtc" timestamp with time zone NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone,
                    "CreatedBy" uuid,
                    "UpdatedBy" uuid,
                    "IsDeleted" boolean NOT NULL,
                    CONSTRAINT "PK_PaymentSettlementBatches" PRIMARY KEY ("Id")
                );

                CREATE TABLE IF NOT EXISTS "PaymentSettlementOrders" (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "SettlementBatchId" uuid NOT NULL,
                    "OrderId" uuid NOT NULL,
                    "PaymentIntentId" uuid,
                    "OrderTotal" numeric(18,2) NOT NULL,
                    "Currency" character varying(3) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone,
                    "CreatedBy" uuid,
                    "UpdatedBy" uuid,
                    "IsDeleted" boolean NOT NULL,
                    CONSTRAINT "PK_PaymentSettlementOrders" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_PaymentSettlementOrders_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_PaymentSettlementOrders_PaymentSettlementBatches_SettlementBatchId" FOREIGN KEY ("SettlementBatchId") REFERENCES "PaymentSettlementBatches" ("Id") ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS "IX_PaymentIntents_TenantId_PayoutBatchId"
                ON "PaymentIntents" ("TenantId", "PayoutBatchId");

                CREATE INDEX IF NOT EXISTS "IX_PaymentSettlementBatches_Tenant_PayoutBatchId"
                ON "PaymentSettlementBatches" ("TenantId", "PayoutBatchId");

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_PaymentSettlementBatches_Tenant_Type_SettlementId"
                ON "PaymentSettlementBatches" ("TenantId", "SettlementType", "SettlementId")
                WHERE "IsDeleted" = false;

                CREATE INDEX IF NOT EXISTS "IX_PaymentSettlementOrders_OrderId"
                ON "PaymentSettlementOrders" ("OrderId");

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_PaymentSettlementOrders_Tenant_Batch_Order"
                ON "PaymentSettlementOrders" ("TenantId", "SettlementBatchId", "OrderId")
                WHERE "IsDeleted" = false;

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_PaymentSettlementOrders_Tenant_Order"
                ON "PaymentSettlementOrders" ("TenantId", "OrderId")
                WHERE "IsDeleted" = false;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS "PaymentSettlementOrders";
                DROP TABLE IF EXISTS "PaymentSettlementBatches";
                DROP INDEX IF EXISTS "IX_PaymentIntents_TenantId_PayoutBatchId";
                ALTER TABLE "PaymentIntents" DROP COLUMN IF EXISTS "PayoutBatchId";
                """);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260423110000_S19_CheckoutDomainSimplification")]
    public partial class S19CheckoutDomainSimplification : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Orders"
                    ADD COLUMN IF NOT EXISTS "CheckoutState" integer NOT NULL DEFAULT 1,
                    ADD COLUMN IF NOT EXISTS "ManualDiscountAmount" numeric(18,2) NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS "PricingSnapshotJson" text,
                    ADD COLUMN IF NOT EXISTS "TotalDiscountAmount" numeric(18,2) NOT NULL DEFAULT 0;

                UPDATE "Orders"
                SET "TotalDiscountAmount" = COALESCE("DiscountAmount", 0) + COALESCE("PromotionDiscountAmount", 0),
                    "CheckoutState" = CASE
                        WHEN "PaymentStatus" IN (3, 4, 5, 8) THEN 3
                        WHEN "PaymentStatus" = 9 THEN 6
                        WHEN "Status" = 6 THEN 5
                        WHEN "PaymentReference" IS NOT NULL THEN 2
                        ELSE 1
                    END;

                CREATE TABLE IF NOT EXISTS "CouponReservations" (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "CouponId" uuid NOT NULL,
                    "CustomerId" uuid NOT NULL,
                    "OrderId" uuid NOT NULL,
                    "Status" integer NOT NULL,
                    "CorrelationId" character varying(100),
                    "ReservedAtUtc" timestamp with time zone NOT NULL,
                    "ConfirmedAtUtc" timestamp with time zone,
                    "ReleasedAtUtc" timestamp with time zone,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone,
                    "CreatedBy" uuid,
                    "UpdatedBy" uuid,
                    "IsDeleted" boolean NOT NULL,
                    CONSTRAINT "PK_CouponReservations" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_CouponReservations_Coupons_CouponId" FOREIGN KEY ("CouponId") REFERENCES "Coupons" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_CouponReservations_OnlineCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "OnlineCustomers" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_CouponReservations_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT
                );

                CREATE INDEX IF NOT EXISTS "IX_CouponReservations_CouponId"
                ON "CouponReservations" ("CouponId");

                CREATE INDEX IF NOT EXISTS "IX_CouponReservations_CustomerId"
                ON "CouponReservations" ("CustomerId");

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_CouponReservations_TenantId_CouponId_CustomerId"
                ON "CouponReservations" ("TenantId", "CouponId", "CustomerId")
                WHERE "IsDeleted" = false AND "Status" <> 2;

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_CouponReservations_TenantId_OrderId"
                ON "CouponReservations" ("TenantId", "OrderId")
                WHERE "IsDeleted" = false;

                CREATE INDEX IF NOT EXISTS "IX_Orders_TenantId_CheckoutState_CreatedAt"
                ON "Orders" ("TenantId", "CheckoutState", "CreatedAt");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS "CouponReservations";
                DROP INDEX IF EXISTS "IX_Orders_TenantId_CheckoutState_CreatedAt";

                ALTER TABLE "Orders"
                    DROP COLUMN IF EXISTS "CheckoutState",
                    DROP COLUMN IF EXISTS "ManualDiscountAmount",
                    DROP COLUMN IF EXISTS "PricingSnapshotJson",
                    DROP COLUMN IF EXISTS "TotalDiscountAmount";
                """);
        }
    }
}

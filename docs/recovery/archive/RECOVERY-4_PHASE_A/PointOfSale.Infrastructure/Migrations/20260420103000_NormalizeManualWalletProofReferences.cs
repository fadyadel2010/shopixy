using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260420103000_NormalizeManualWalletProofReferences")]
    public partial class NormalizeManualWalletProofReferences : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL-specific migration: ILIKE and substring with regex are Postgres-only
            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql(
                    """
                    -- PaymentIntents: Extract and validate tenant-scoped proof keys
                    -- Only preserve keys where the extracted tenant UUID matches the row's TenantId
                    UPDATE "PaymentIntents"
                    SET "ProofImageUrl" = substring("ProofImageUrl" from 'tenants/[^?#/]+/manual-wallet-proofs/[^?#]+')
                    WHERE "ProofImageUrl" ILIKE 'http%'
                      AND substring("ProofImageUrl" from 'tenants/([^?#/]+)/manual-wallet-proofs/') = "TenantId"::text;

                    -- PaymentIntents: Null out invalid or cross-tenant references
                    UPDATE "PaymentIntents"
                    SET "ProofImageUrl" = NULL,
                        "ProofImageDeleted" = TRUE
                    WHERE "ProofImageUrl" ILIKE 'http%'
                      AND (substring("ProofImageUrl" from 'tenants/([^?#/]+)/manual-wallet-proofs/') IS NULL
                           OR substring("ProofImageUrl" from 'tenants/([^?#/]+)/manual-wallet-proofs/') != "TenantId"::text);

                    -- CheckoutSessions: Extract and validate tenant-scoped proof keys
                    -- Only preserve keys where the extracted tenant UUID matches the row's TenantId
                    UPDATE "CheckoutSessions"
                    SET "ProofImageUrl" = substring("ProofImageUrl" from 'tenants/[^?#/]+/manual-wallet-proofs/[^?#]+')
                    WHERE "ProofImageUrl" ILIKE 'http%'
                      AND substring("ProofImageUrl" from 'tenants/([^?#/]+)/manual-wallet-proofs/') = "TenantId"::text;

                    -- CheckoutSessions: Null out invalid or cross-tenant references
                    UPDATE "CheckoutSessions"
                    SET "ProofImageUrl" = NULL,
                        "WalletPhoneNumber" = NULL,
                        "WalletType" = NULL
                    WHERE "ProofImageUrl" ILIKE 'http%'
                      AND (substring("ProofImageUrl" from 'tenants/([^?#/]+)/manual-wallet-proofs/') IS NULL
                           OR substring("ProofImageUrl" from 'tenants/([^?#/]+)/manual-wallet-proofs/') != "TenantId"::text);
                    """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

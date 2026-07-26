using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <summary>
    /// S52: Cleanup old cart owner unique index
    /// The S46 migration created a new index "UX_CommerceHubCarts_TenantId_CommerceHubCustomerId_Active"
    /// but did not drop the old index "UX_CommerceHubCarts_CommerceHubCustomerId_Active".
    /// This migration removes the old index to ensure all environments converge to the same state.
    /// S63 later removes IX_CommerceHubCarts_TenantId_OnlineCustomerId so legacy Storefront identity cannot
    /// remain an ownership index for CommerceHub carts.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260429130000_S52_CleanupOldCartOwnerIndex")]
    public partial class S52_CleanupOldCartOwnerIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- Drop old cart owner unique index if it exists
                -- The new index UX_CommerceHubCarts_TenantId_CommerceHubCustomerId_Active (created in S46) is the canonical one for CommerceHubCustomerId
                -- S63 drops IX_CommerceHubCarts_TenantId_OnlineCustomerId after CommerceHubCustomerId is fully authoritative.
                DROP INDEX IF EXISTS "UX_CommerceHubCarts_CommerceHubCustomerId_Active";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: We don't recreate the old indexes on rollback
            // The new index UX_CommerceHubCarts_TenantId_CommerceHubCustomerId_Active provides the same functionality
            // and the application code handles both old and new index names for backward compatibility
        }
    }
}

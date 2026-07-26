using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AF30A_FIX_D2_EnabledRequiresCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AffiliateCategoryId",
                table: "AffiliateProductPolicies",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AffiliateCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateCategories", x => x.Id);
                    table.CheckConstraint("CK_AffiliateCategories_Level", "\"Level\" IN (1, 2)");
                    table.CheckConstraint("CK_AffiliateCategories_Level1_NoParent", "(\"Level\" = 1 AND \"ParentId\" IS NULL) OR (\"Level\" = 2 AND \"ParentId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AffiliateCategories_AffiliateCategories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "AffiliateCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateProductPolicies_AffiliateCategoryId",
                table: "AffiliateProductPolicies",
                column: "AffiliateCategoryId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AffiliateProductPolicies_EnabledRequiresCategory",
                table: "AffiliateProductPolicies",
                sql: "NOT (\"IsEnabled\" = true AND \"AffiliateCategoryId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCategories_DisplayOrder",
                table: "AffiliateCategories",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCategories_IsActive",
                table: "AffiliateCategories",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCategories_ParentId",
                table: "AffiliateCategories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateCategories_Parent_Name",
                table: "AffiliateCategories",
                columns: new[] { "ParentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateCategories_Slug",
                table: "AffiliateCategories",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AffiliateProductPolicies_AffiliateCategories_AffiliateCateg~",
                table: "AffiliateProductPolicies",
                column: "AffiliateCategoryId",
                principalTable: "AffiliateCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AffiliateProductPolicies_AffiliateCategories_AffiliateCateg~",
                table: "AffiliateProductPolicies");

            migrationBuilder.DropTable(
                name: "AffiliateCategories");

            migrationBuilder.DropIndex(
                name: "IX_AffiliateProductPolicies_AffiliateCategoryId",
                table: "AffiliateProductPolicies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AffiliateProductPolicies_EnabledRequiresCategory",
                table: "AffiliateProductPolicies");

            migrationBuilder.DropColumn(
                name: "AffiliateCategoryId",
                table: "AffiliateProductPolicies");
        }
    }
}

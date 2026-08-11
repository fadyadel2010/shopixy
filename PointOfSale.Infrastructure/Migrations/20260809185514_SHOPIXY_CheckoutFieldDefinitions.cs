using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SHOPIXY_CheckoutFieldDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckoutFieldDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FieldType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SystemFieldKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    HelpText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationSchemaVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    ConfigurationJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutFieldDefinitions", x => x.Id);
                    table.CheckConstraint("CK_CheckoutFieldDefinitions_RequiredEnabled", "NOT \"IsRequired\" OR \"IsEnabled\"");
                    table.CheckConstraint("CK_CheckoutFieldDefinitions_SystemFieldKey", "(\"FieldKind\" = 'System' AND \"SystemFieldKey\" IS NOT NULL AND LENGTH(TRIM(\"SystemFieldKey\")) > 0) OR (\"FieldKind\" = 'Custom' AND \"SystemFieldKey\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_CheckoutFieldDefinitions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutFieldDefinitions_TenantId",
                table: "CheckoutFieldDefinitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutFieldDefinitions_TenantId_DisplayOrder",
                table: "CheckoutFieldDefinitions",
                columns: new[] { "TenantId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutFieldDefinitions_TenantId_IsDeleted",
                table: "CheckoutFieldDefinitions",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "UX_CheckoutFieldDefinitions_Tenant_SystemFieldKey_Active",
                table: "CheckoutFieldDefinitions",
                columns: new[] { "TenantId", "SystemFieldKey" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"SystemFieldKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckoutFieldDefinitions");
        }
    }
}

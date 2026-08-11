using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SHOPIXY_OrderCheckoutFieldSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderCheckoutFieldSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckoutFieldDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FieldType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SystemFieldKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    HelpText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    ValueJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "null"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderCheckoutFieldSnapshots", x => x.Id);
                    table.CheckConstraint("CK_OrderCheckoutFieldSnapshots_ConfigurationSchemaVersion_Posi~", "\"ConfigurationSchemaVersion\" > 0");
                    table.CheckConstraint("CK_OrderCheckoutFieldSnapshots_SystemFieldKey", "(\"FieldKind\" = 'System' AND \"SystemFieldKey\" IS NOT NULL AND LENGTH(TRIM(\"SystemFieldKey\")) > 0) OR (\"FieldKind\" = 'Custom' AND \"SystemFieldKey\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_OrderCheckoutFieldSnapshots_Orders_TenantId_OrderId",
                        columns: x => new { x.TenantId, x.OrderId },
                        principalTable: "Orders",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderCheckoutFieldSnapshots_OrderId",
                table: "OrderCheckoutFieldSnapshots",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderCheckoutFieldSnapshots_TenantId_OrderId",
                table: "OrderCheckoutFieldSnapshots",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderCheckoutFieldSnapshots_TenantId_OrderId_DisplayOrder",
                table: "OrderCheckoutFieldSnapshots",
                columns: new[] { "TenantId", "OrderId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "UX_OrderCheckoutFieldSnapshots_Tenant_Order_FieldDefinition_Active",
                table: "OrderCheckoutFieldSnapshots",
                columns: new[] { "TenantId", "OrderId", "CheckoutFieldDefinitionId" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderCheckoutFieldSnapshots");
        }
    }
}

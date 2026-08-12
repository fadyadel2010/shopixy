using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SHOPIXY_WarehouseResolutionAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "CarrierWarehouseAssignments" assignment
                        LEFT JOIN "CarrierAccounts" account
                          ON account."TenantId" = assignment."TenantId"
                         AND account."Id" = assignment."CarrierAccountId"
                        WHERE account."Id" IS NULL
                    ) THEN
                        RAISE EXCEPTION 'SHOPIXY warehouse authority migration blocked: carrier warehouse assignments contain missing or cross-tenant carrier accounts.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "CarrierWarehouseAssignments" assignment
                        LEFT JOIN "Warehouses" warehouse
                          ON warehouse."TenantId" = assignment."TenantId"
                         AND warehouse."Id" = assignment."WarehouseId"
                        WHERE warehouse."Id" IS NULL
                    ) THEN
                        RAISE EXCEPTION 'SHOPIXY warehouse authority migration blocked: carrier warehouse assignments contain missing or cross-tenant warehouses.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Warehouses_TenantId_Id",
                table: "Warehouses",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_CheckoutSessions_TenantId_Id",
                table: "CheckoutSessions",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_CarrierAccounts_TenantId_Id",
                table: "CarrierAccounts",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "WarehouseResolutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckoutSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ResolutionSource = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ResolutionVersion = table.Column<int>(type: "integer", nullable: false),
                    AlgorithmCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    InputsSnapshotHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InputsSnapshotJson = table.Column<string>(type: "text", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    InvalidationReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InvalidatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConvertedOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseResolutions", x => x.Id);
                    table.CheckConstraint("CK_WarehouseResolutions_FailedRequiresCode", "\"Status\" <> 'Failed' OR \"FailureCode\" IS NOT NULL");
                    table.CheckConstraint("CK_WarehouseResolutions_OrderedRequiresOrder", "\"Status\" <> 'Ordered' OR \"ConvertedOrderId\" IS NOT NULL");
                    table.CheckConstraint("CK_WarehouseResolutions_ResolutionVersion_Positive", "\"ResolutionVersion\" > 0");
                    table.CheckConstraint("CK_WarehouseResolutions_ResolvedStatusesRequireWarehouse", "\"Status\" NOT IN ('Resolved', 'Locked', 'Ordered') OR \"WarehouseId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_WarehouseResolutions_CheckoutSessions_TenantId_CheckoutSess~",
                        columns: x => new { x.TenantId, x.CheckoutSessionId },
                        principalTable: "CheckoutSessions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseResolutions_Orders_TenantId_ConvertedOrderId",
                        columns: x => new { x.TenantId, x.ConvertedOrderId },
                        principalTable: "Orders",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseResolutions_Warehouses_TenantId_WarehouseId",
                        columns: x => new { x.TenantId, x.WarehouseId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierWarehouseAssignments_TenantId_WarehouseId",
                table: "CarrierWarehouseAssignments",
                columns: new[] { "TenantId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseResolutions_InputsSnapshotHash",
                table: "WarehouseResolutions",
                column: "InputsSnapshotHash");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseResolutions_TenantId",
                table: "WarehouseResolutions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseResolutions_TenantId_Cart_Status",
                table: "WarehouseResolutions",
                columns: new[] { "TenantId", "CartId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseResolutions_TenantId_ConvertedOrderId",
                table: "WarehouseResolutions",
                columns: new[] { "TenantId", "ConvertedOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseResolutions_TenantId_Expires_Status",
                table: "WarehouseResolutions",
                columns: new[] { "TenantId", "ExpiresAtUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseResolutions_TenantId_Session_Active_Unique",
                table: "WarehouseResolutions",
                columns: new[] { "TenantId", "CheckoutSessionId" },
                unique: true,
                filter: "\"Status\" IN ('Resolved', 'Locked') AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseResolutions_TenantId_Session_Status",
                table: "WarehouseResolutions",
                columns: new[] { "TenantId", "CheckoutSessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseResolutions_TenantId_Warehouse_Status",
                table: "WarehouseResolutions",
                columns: new[] { "TenantId", "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_WarehouseResolutions_TenantId_Session_Version",
                table: "WarehouseResolutions",
                columns: new[] { "TenantId", "CheckoutSessionId", "ResolutionVersion" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CarrierWarehouseAssignments_CarrierAccounts_TenantId_Carrie~",
                table: "CarrierWarehouseAssignments",
                columns: new[] { "TenantId", "CarrierAccountId" },
                principalTable: "CarrierAccounts",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CarrierWarehouseAssignments_Warehouses_TenantId_WarehouseId",
                table: "CarrierWarehouseAssignments",
                columns: new[] { "TenantId", "WarehouseId" },
                principalTable: "Warehouses",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CarrierWarehouseAssignments_CarrierAccounts_TenantId_Carrie~",
                table: "CarrierWarehouseAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_CarrierWarehouseAssignments_Warehouses_TenantId_WarehouseId",
                table: "CarrierWarehouseAssignments");

            migrationBuilder.DropTable(
                name: "WarehouseResolutions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Warehouses_TenantId_Id",
                table: "Warehouses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CheckoutSessions_TenantId_Id",
                table: "CheckoutSessions");

            migrationBuilder.DropIndex(
                name: "IX_CarrierWarehouseAssignments_TenantId_WarehouseId",
                table: "CarrierWarehouseAssignments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CarrierAccounts_TenantId_Id",
                table: "CarrierAccounts");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShipmentCancellationAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShipmentCancellationOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorIdentity = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CarrierCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AwbNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalReferenceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CarrierRequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ConfigurationId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Environment = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ConfirmationBasis = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    EvidenceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EvidenceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentCancellationOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentCancellationOperations_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentCancellationOperations_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentCancellationOperations_OrderId",
                table: "ShipmentCancellationOperations",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentCancellationOperations_ShipmentId",
                table: "ShipmentCancellationOperations",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentCancellationOperations_TenantId_ShipmentId",
                table: "ShipmentCancellationOperations",
                columns: new[] { "TenantId", "ShipmentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipmentCancellationOperations");
        }
    }
}

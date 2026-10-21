using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyntheticQaAffiliateCertification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyntheticQaRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerifierId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionLinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Provenance = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyntheticQaRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyntheticQaRuns_AffiliatePromotionLinks_PromotionLinkId",
                        column: x => x.PromotionLinkId,
                        principalTable: "AffiliatePromotionLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyntheticQaRuns_OnlineCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "OnlineCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyntheticQaOrders",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrolledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyntheticQaOrders", x => x.OrderId);
                    table.ForeignKey(
                        name: "FK_SyntheticQaOrders_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyntheticQaOrders_SyntheticQaRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "SyntheticQaRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyntheticQaCommissionPostings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Debit = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Credit = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Provenance = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyntheticQaCommissionPostings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyntheticQaCommissionPostings_SyntheticQaOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "SyntheticQaOrders",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyntheticQaEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerifierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Nonce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Debit = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Credit = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Signature = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provenance = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyntheticQaEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyntheticQaEvidence_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyntheticQaEvidence_SyntheticQaOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "SyntheticQaOrders",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyntheticQaEvidence_SyntheticQaRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "SyntheticQaRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyntheticQaCommissionPostings_OrderId",
                table: "SyntheticQaCommissionPostings",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyntheticQaEvidence_Nonce",
                table: "SyntheticQaEvidence",
                column: "Nonce",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyntheticQaEvidence_OrderId_Kind",
                table: "SyntheticQaEvidence",
                columns: new[] { "OrderId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyntheticQaEvidence_RunId",
                table: "SyntheticQaEvidence",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_SyntheticQaEvidence_ShipmentId",
                table: "SyntheticQaEvidence",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SyntheticQaOrders_RunId",
                table: "SyntheticQaOrders",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_SyntheticQaRuns_CustomerId",
                table: "SyntheticQaRuns",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SyntheticQaRuns_PromotionLinkId",
                table: "SyntheticQaRuns",
                column: "PromotionLinkId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyntheticQaCommissionPostings");

            migrationBuilder.DropTable(
                name: "SyntheticQaEvidence");

            migrationBuilder.DropTable(
                name: "SyntheticQaOrders");

            migrationBuilder.DropTable(
                name: "SyntheticQaRuns");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CarrierObservedCodCollectionEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CarrierCodShipmentBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigurationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AccountId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeliveryReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExternalReferenceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierCodShipmentBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarrierCodShipmentBindings_CarrierConfigurations_Configurat~",
                        column: x => x.ConfigurationId,
                        principalTable: "CarrierConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierCodShipmentBindings_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierCodShipmentBindings_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CarrierCollectionEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigurationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentBindingId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedEvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CarrierCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AccountId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeliveryReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ObservedAmount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ProviderEventAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceContract = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TrustMethod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ObservationFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AdmissionOutcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AdmissionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AuditIdentity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierCollectionEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarrierCollectionEvidence_CarrierCodShipmentBindings_Shipme~",
                        column: x => x.ShipmentBindingId,
                        principalTable: "CarrierCodShipmentBindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierCollectionEvidence_CarrierCollectionEvidence_Reviewe~",
                        column: x => x.ReviewedEvidenceId,
                        principalTable: "CarrierCollectionEvidence",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierCollectionEvidence_CarrierConfigurations_Configurati~",
                        column: x => x.ConfigurationId,
                        principalTable: "CarrierConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierCollectionEvidence_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierCollectionEvidence_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierCodShipmentBindings_ConfigurationId",
                table: "CarrierCodShipmentBindings",
                column: "ConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierCodShipmentBindings_OrderId",
                table: "CarrierCodShipmentBindings",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierCodShipmentBindings_ShipmentId",
                table: "CarrierCodShipmentBindings",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierCodShipmentBindings_TenantId_ShipmentId",
                table: "CarrierCodShipmentBindings",
                columns: new[] { "TenantId", "ShipmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierCollectionEvidence_ConfigurationId",
                table: "CarrierCollectionEvidence",
                column: "ConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierCollectionEvidence_OrderId",
                table: "CarrierCollectionEvidence",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierCollectionEvidence_ReviewedEvidenceId",
                table: "CarrierCollectionEvidence",
                column: "ReviewedEvidenceId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierCollectionEvidence_ShipmentBindingId",
                table: "CarrierCollectionEvidence",
                column: "ShipmentBindingId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierCollectionEvidence_ShipmentId",
                table: "CarrierCollectionEvidence",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierCollectionEvidence_TenantId_CarrierCode_ObservationF~",
                table: "CarrierCollectionEvidence",
                columns: new[] { "TenantId", "CarrierCode", "ObservationFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierCollectionEvidence_TenantId_OrderId",
                table: "CarrierCollectionEvidence",
                columns: new[] { "TenantId", "OrderId" },
                unique: true,
                filter: "\"AdmissionOutcome\" = 'Qualified'");

            // Additive PostgreSQL enforcement: app guards are not the sole immutability barrier.
            migrationBuilder.Sql("""
                ALTER TABLE "CarrierCollectionEvidence" ADD CONSTRAINT "CK_CarrierCollectionEvidence_QualifiedMoney"
                CHECK ("AdmissionOutcome" IN ('Qualified', 'Review', 'RevalidationReview') AND
                    ("ObservedAmount" IS NULL OR "ObservedAmount" >= 0) AND
                    ("AdmissionOutcome" <> 'Qualified' OR
                        ("ObservedAmount" IS NOT NULL AND "ObservedAmount" > 0 AND "Currency" IS NOT NULL AND "AccountId" IS NOT NULL AND "ShipmentBindingId" IS NOT NULL AND
                         "ProviderEventAtUtc" <= "ReceivedAtUtc" + interval '5 minutes')));
                CREATE FUNCTION carrier_cod_evidence_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Carrier collection evidence and shipment money bindings are immutable permanent identities' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER carrier_collection_evidence_immutable BEFORE UPDATE OR DELETE ON "CarrierCollectionEvidence"
                    FOR EACH ROW EXECUTE FUNCTION carrier_cod_evidence_immutable();
                CREATE TRIGGER carrier_cod_binding_immutable BEFORE UPDATE OR DELETE ON "CarrierCodShipmentBindings"
                    FOR EACH ROW EXECUTE FUNCTION carrier_cod_evidence_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarrierCollectionEvidence");

            migrationBuilder.DropTable(
                name: "CarrierCodShipmentBindings");
            migrationBuilder.Sql("DROP FUNCTION carrier_cod_evidence_immutable();");
        }
    }
}

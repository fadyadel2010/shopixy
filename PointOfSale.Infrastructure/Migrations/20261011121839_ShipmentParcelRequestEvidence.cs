using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShipmentParcelRequestEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CarrierRequestFingerprint",
                table: "Shipments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION shopixy_guard_parcel_request_evidence() RETURNS trigger AS $$
                BEGIN
                  IF (OLD."ParcelEvidenceJson" IS NOT NULL AND NEW."ParcelEvidenceJson" IS DISTINCT FROM OLD."ParcelEvidenceJson")
                     OR (OLD."CarrierRequestSnapshotJson" IS NOT NULL AND
                         (NEW."CarrierRequestSnapshotJson" IS DISTINCT FROM OLD."CarrierRequestSnapshotJson"
                          OR NEW."CarrierRequestFingerprint" IS DISTINCT FROM OLD."CarrierRequestFingerprint")) THEN
                    RAISE EXCEPTION 'Immutable parcel/carrier request evidence cannot be changed' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER "TR_Shipments_ParcelRequestEvidence" BEFORE UPDATE ON "Shipments"
                  FOR EACH ROW EXECUTE FUNCTION shopixy_guard_parcel_request_evidence();
                """);

            migrationBuilder.AddColumn<string>(
                name: "CarrierRequestSnapshotJson",
                table: "Shipments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParcelEvidenceJson",
                table: "Shipments",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "TR_Shipments_ParcelRequestEvidence" ON "Shipments";
                DROP FUNCTION shopixy_guard_parcel_request_evidence();
                """);
            migrationBuilder.DropColumn(
                name: "CarrierRequestFingerprint",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "CarrierRequestSnapshotJson",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ParcelEvidenceJson",
                table: "Shipments");
        }
    }
}

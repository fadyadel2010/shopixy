using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [Migration("20260422224500_S16A_ShipmentCheckoutSafety")]
    public partial class S16AShipmentCheckoutSafety : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalReferenceId",
                table: "Shipments",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierRequestId",
                table: "Shipments",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalRegistrationState",
                table: "Shipments",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExternalRegistrationAttemptCount",
                table: "Shipments",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExternalRegistrationRequestedAtUtc",
                table: "Shipments",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExternalRegistrationLastAttemptAtUtc",
                table: "Shipments",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExternalRegistrationCompletedAtUtc",
                table: "Shipments",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExternalRegistrationFailedAtUtc",
                table: "Shipments",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalRegistrationError",
                table: "Shipments",
                maxLength: 500,
                nullable: true);

            if (ActiveProvider.Contains("SqlServer"))
            {
                migrationBuilder.Sql("""
                    CREATE UNIQUE INDEX [IX_Shipments_TenantId_CarrierCode_ExternalReferenceId]
                    ON [Shipments] ([TenantId], [CarrierCode], [ExternalReferenceId])
                    WHERE [IsDeleted] = 0 AND [ExternalReferenceId] IS NOT NULL;
                    """);
                migrationBuilder.Sql("""
                    CREATE UNIQUE INDEX [IX_Shipments_TenantId_CarrierCode_CarrierRequestId]
                    ON [Shipments] ([TenantId], [CarrierCode], [CarrierRequestId])
                    WHERE [IsDeleted] = 0 AND [CarrierRequestId] IS NOT NULL;
                    """);
            }
            else if (ActiveProvider.Contains("Sqlite"))
            {
                migrationBuilder.Sql("""
                    CREATE UNIQUE INDEX "IX_Shipments_TenantId_CarrierCode_ExternalReferenceId"
                    ON "Shipments" ("TenantId", "CarrierCode", "ExternalReferenceId")
                    WHERE "IsDeleted" = 0 AND "ExternalReferenceId" IS NOT NULL;
                    """);
                migrationBuilder.Sql("""
                    CREATE UNIQUE INDEX "IX_Shipments_TenantId_CarrierCode_CarrierRequestId"
                    ON "Shipments" ("TenantId", "CarrierCode", "CarrierRequestId")
                    WHERE "IsDeleted" = 0 AND "CarrierRequestId" IS NOT NULL;
                    """);
            }
            // PostgreSQL: Index creation is handled by the companion .sql file
            // (20260422224500_S16A_ShipmentCheckoutSafety.sql) using CREATE INDEX CONCURRENTLY
            // to avoid blocking writes during deployment. Run the .sql file manually after
            // applying this migration with 'dotnet ef database update'.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Contains("Npgsql"))
            {
                // PostgreSQL: Indexes IX_Shipments_TenantId_CarrierCode_ExternalReferenceId and
                // IX_Shipments_TenantId_CarrierCode_CarrierRequestId were created externally by the
                // companion SQL file (20260422224500_S16A_ShipmentCheckoutSafety.sql) using
                // CREATE INDEX CONCURRENTLY. To rollback this migration on PostgreSQL, you must
                // manually run the rollback section from the companion .sql file using
                // DROP INDEX CONCURRENTLY to avoid blocking writes.
                throw new InvalidOperationException(
                    "Migration 20260422224500_S16A_ShipmentCheckoutSafety cannot be rolled back automatically on PostgreSQL. " +
                    "The indexes IX_Shipments_TenantId_CarrierCode_ExternalReferenceId and " +
                    "IX_Shipments_TenantId_CarrierCode_CarrierRequestId were created using CREATE INDEX CONCURRENTLY " +
                    "via the companion SQL file. To rollback, you must manually execute the FULL rollback section from " +
                    "20260422224500_S16A_ShipmentCheckoutSafety.sql, which includes: " +
                    "(1) DROP INDEX CONCURRENTLY for both indexes, and " +
                    "(2) subsequent column removals (ExternalReferenceId, CarrierRequestId, etc.). " +
                    "The entire rollback must be performed via the SQL script to maintain consistency.");
            }
            else if (ActiveProvider.Contains("SqlServer") || ActiveProvider.Contains("Sqlite"))
            {
                migrationBuilder.DropIndex(
                    name: "IX_Shipments_TenantId_CarrierCode_ExternalReferenceId",
                    table: "Shipments");

                migrationBuilder.DropIndex(
                    name: "IX_Shipments_TenantId_CarrierCode_CarrierRequestId",
                    table: "Shipments");
            }

            migrationBuilder.DropColumn(
                name: "ExternalReferenceId",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "CarrierRequestId",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ExternalRegistrationState",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ExternalRegistrationAttemptCount",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ExternalRegistrationRequestedAtUtc",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ExternalRegistrationLastAttemptAtUtc",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ExternalRegistrationCompletedAtUtc",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ExternalRegistrationFailedAtUtc",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ExternalRegistrationError",
                table: "Shipments");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <summary>
    /// S51: Fixes schema drift in CommerceHubPaymentCompensationOutboxMessages.AffectedStoreIdsJson
    /// The model configuration specifies 'text' but the S46 migration created it as 'character varying(2000)'.
    /// This migration aligns the database schema with the model configuration.
    /// </summary>
    [Migration("20260429120000_S51_FixAffectedStoreIdsJsonColumnType")]
    public partial class S51_FixAffectedStoreIdsJsonColumnType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "AffectedStoreIdsJson",
                table: "CommerceHubPaymentCompensationOutboxMessages",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Safety guard: Check if any rows exceed the 2000 character limit before downgrading
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "CommerceHubPaymentCompensationOutboxMessages"
                        WHERE length("AffectedStoreIdsJson") > 2000
                    ) THEN
                        RAISE EXCEPTION 'Migration S51 Down failed: Found rows with AffectedStoreIdsJson exceeding 2000 characters. Cannot safely downgrade column type from text to varchar(2000). Manual intervention required.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "AffectedStoreIdsJson",
                table: "CommerceHubPaymentCompensationOutboxMessages",
                type: "character varying(2000)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}

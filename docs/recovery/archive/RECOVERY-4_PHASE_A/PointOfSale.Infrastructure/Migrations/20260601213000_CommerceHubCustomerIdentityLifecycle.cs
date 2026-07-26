using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

public partial class CommerceHubCustomerIdentityLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsEmailVerified",
            table: "CommerceHubCustomers",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "EmailVerifiedAtUtc",
            table: "CommerceHubCustomers",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "CommerceHubCustomerEmailVerificationTokens",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IsUsed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CommerceHubCustomerEmailVerificationTokens", x => x.Id);
                table.ForeignKey(
                    name: "FK_CHCustomerEmailVerificationTokens_CommerceHubCustomers_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "CommerceHubCustomers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "CommerceHubCustomerIdentityEvents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Metadata = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CommerceHubCustomerIdentityEvents", x => x.Id);
                table.ForeignKey(
                    name: "FK_CHCustomerIdentityEvents_CommerceHubCustomers_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "CommerceHubCustomers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CommerceHubCustomerPasswordResetTokens",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IsUsed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedByIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CommerceHubCustomerPasswordResetTokens", x => x.Id);
                table.ForeignKey(
                    name: "FK_CHCustomerPasswordResetTokens_CommerceHubCustomers_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "CommerceHubCustomers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CHCustomerEmailVerificationTokens_CustomerId",
            table: "CommerceHubCustomerEmailVerificationTokens",
            column: "CustomerId");

        migrationBuilder.CreateIndex(
            name: "IX_CHCustomerEmailVerificationTokens_TokenHash",
            table: "CommerceHubCustomerEmailVerificationTokens",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CHCustomerIdentityEvents_Customer_Occurred",
            table: "CommerceHubCustomerIdentityEvents",
            columns: new[] { "CustomerId", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_CHCustomerIdentityEvents_EventType_Occurred",
            table: "CommerceHubCustomerIdentityEvents",
            columns: new[] { "EventType", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_CHCustomerPasswordResetTokens_CustomerId",
            table: "CommerceHubCustomerPasswordResetTokens",
            column: "CustomerId");

        migrationBuilder.CreateIndex(
            name: "IX_CHCustomerPasswordResetTokens_TokenHash",
            table: "CommerceHubCustomerPasswordResetTokens",
            column: "TokenHash",
            unique: true);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_CommerceHubCustomerIdentityEvents_AppendOnly"
            BEFORE UPDATE OR DELETE OR TRUNCATE ON "CommerceHubCustomerIdentityEvents"
            FOR EACH STATEMENT
            EXECUTE FUNCTION forensic.reject_append_only_evidence_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(
            "CommerceHub customer identity evidence hardening is intentionally irreversible.");
    }
}

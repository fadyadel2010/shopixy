using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics")]
    public partial class DOMAIN_4_3_DomainVerificationEngineDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "DomainVerifications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolverEndpoint",
                table: "DomainVerifications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LatencyMilliseconds",
                table: "DomainVerifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Attempt",
                table: "DomainVerifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Ttl",
                table: "DomainVerifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedValue",
                table: "DomainVerifications",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "DomainVerifications");

            migrationBuilder.DropColumn(
                name: "ResolverEndpoint",
                table: "DomainVerifications");

            migrationBuilder.DropColumn(
                name: "LatencyMilliseconds",
                table: "DomainVerifications");

            migrationBuilder.DropColumn(
                name: "Attempt",
                table: "DomainVerifications");

            migrationBuilder.DropColumn(
                name: "Ttl",
                table: "DomainVerifications");

            migrationBuilder.DropColumn(
                name: "ResolvedValue",
                table: "DomainVerifications");
        }
    }
}

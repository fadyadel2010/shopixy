using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DOMAIN_Final_CertificateTenantIsolationAndRetirement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SslCertificateOperations_DomainId",
                table: "SslCertificateOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_SslCertificates_DomainId",
                table: "SslCertificates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SslCertificates_Status_Canonical",
                table: "SslCertificates");

            migrationBuilder.DropIndex(
                name: "IX_SslCertificateOperations_DomainId",
                table: "SslCertificateOperations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SslCertificates_Status_Canonical",
                table: "SslCertificates",
                sql: "\"Status\" IN (0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10)");

            migrationBuilder.AddForeignKey(
                name: "FK_SslCertificateOperations_DomainId",
                table: "SslCertificateOperations",
                columns: new[] { "TenantId", "DomainId" },
                principalTable: "Domains",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SslCertificates_DomainId",
                table: "SslCertificates",
                columns: new[] { "TenantId", "DomainId" },
                principalTable: "Domains",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SslCertificateOperations_DomainId",
                table: "SslCertificateOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_SslCertificates_DomainId",
                table: "SslCertificates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SslCertificates_Status_Canonical",
                table: "SslCertificates");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SslCertificates_Status_Canonical",
                table: "SslCertificates",
                sql: "\"Status\" IN (0, 1, 2, 3, 4, 5, 6, 7, 8, 9)");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificateOperations_DomainId",
                table: "SslCertificateOperations",
                column: "DomainId");

            migrationBuilder.AddForeignKey(
                name: "FK_SslCertificateOperations_DomainId",
                table: "SslCertificateOperations",
                column: "DomainId",
                principalTable: "Domains",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SslCertificates_DomainId",
                table: "SslCertificates",
                column: "DomainId",
                principalTable: "Domains",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DOMAIN_Final_ForensicHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Domains_RedirectTargetDomainId",
                table: "Domains");

            migrationBuilder.DropForeignKey(
                name: "FK_SslCertificateOperations_CertificateId",
                table: "SslCertificateOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_SslCertificates_ReplacementCertificateId",
                table: "SslCertificates");

            migrationBuilder.DropIndex(
                name: "IX_SslCertificates_ReplacementCertificateId",
                table: "SslCertificates");

            migrationBuilder.DropIndex(
                name: "IX_SslCertificateOperations_CertificateId",
                table: "SslCertificateOperations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Domains_Redirect_Cascade",
                table: "Domains");

            migrationBuilder.AddColumn<DateTime>(
                name: "VerificationTokenIssuedAtUtc",
                table: "Domains",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CertificateOperationFencingToken",
                table: "Domains",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(
                "UPDATE \"Domains\" SET \"VerificationTokenIssuedAtUtc\" = COALESCE(\"UpdatedAtUtc\", \"CreatedAtUtc\") WHERE \"VerificationTokenIssuedAtUtc\" IS NULL;");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SslCertificates_TenantId_DomainId_Id",
                table: "SslCertificates",
                columns: new[] { "TenantId", "DomainId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_TenantId_DomainId_ReplacementCertificateId",
                table: "SslCertificates",
                columns: new[] { "TenantId", "DomainId", "ReplacementCertificateId" });

            migrationBuilder.CreateIndex(
                name: "UQ_DomainVerifications_DomainId_Attempt",
                table: "DomainVerifications",
                columns: new[] { "DomainId", "Attempt" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Domains_TenantId_RedirectTargetDomainId",
                table: "Domains",
                columns: new[] { "TenantId", "RedirectTargetDomainId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Domains_Primary_Type_State_Equivalent",
                table: "Domains",
                sql: "((\"DomainType\" = 0 AND \"LifecycleState\" = 5) OR (\"DomainType\" <> 0 AND \"LifecycleState\" <> 5))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Domains_CertificateOperationFencingToken_NonNegative",
                table: "Domains",
                sql: "\"CertificateOperationFencingToken\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Domains_Redirect_Cascade",
                table: "Domains",
                sql: "((\"DomainType\" = 2 AND \"RedirectTargetDomainId\" IS NOT NULL AND \"RedirectTargetDomainId\" <> \"Id\" AND \"RedirectType\" <> 0) OR (\"DomainType\" <> 2 AND \"RedirectTargetDomainId\" IS NULL AND \"RedirectType\" = 0))");

            migrationBuilder.AddForeignKey(
                name: "FK_Domains_RedirectTargetDomainId",
                table: "Domains",
                columns: new[] { "TenantId", "RedirectTargetDomainId" },
                principalTable: "Domains",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SslCertificateOperations_CertificateId",
                table: "SslCertificateOperations",
                columns: new[] { "TenantId", "DomainId", "CertificateId" },
                principalTable: "SslCertificates",
                principalColumns: new[] { "TenantId", "DomainId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SslCertificates_ReplacementCertificateId",
                table: "SslCertificates",
                columns: new[] { "TenantId", "DomainId", "ReplacementCertificateId" },
                principalTable: "SslCertificates",
                principalColumns: new[] { "TenantId", "DomainId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Domains_RedirectTargetDomainId",
                table: "Domains");

            migrationBuilder.DropForeignKey(
                name: "FK_SslCertificateOperations_CertificateId",
                table: "SslCertificateOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_SslCertificates_ReplacementCertificateId",
                table: "SslCertificates");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SslCertificates_TenantId_DomainId_Id",
                table: "SslCertificates");

            migrationBuilder.DropIndex(
                name: "IX_SslCertificates_TenantId_DomainId_ReplacementCertificateId",
                table: "SslCertificates");

            migrationBuilder.DropIndex(
                name: "UQ_DomainVerifications_DomainId_Attempt",
                table: "DomainVerifications");

            migrationBuilder.DropIndex(
                name: "IX_Domains_TenantId_RedirectTargetDomainId",
                table: "Domains");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Domains_Primary_Type_State_Equivalent",
                table: "Domains");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Domains_CertificateOperationFencingToken_NonNegative",
                table: "Domains");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Domains_Redirect_Cascade",
                table: "Domains");

            migrationBuilder.DropColumn(
                name: "VerificationTokenIssuedAtUtc",
                table: "Domains");

            migrationBuilder.DropColumn(
                name: "CertificateOperationFencingToken",
                table: "Domains");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_ReplacementCertificateId",
                table: "SslCertificates",
                column: "ReplacementCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificateOperations_CertificateId",
                table: "SslCertificateOperations",
                column: "CertificateId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Domains_Redirect_Cascade",
                table: "Domains",
                sql: "(\"DomainType\" <> 2 OR (\"RedirectTargetDomainId\" IS NOT NULL AND \"RedirectTargetDomainId\" <> \"Id\" AND \"RedirectType\" <> 0))");

            migrationBuilder.AddForeignKey(
                name: "FK_Domains_RedirectTargetDomainId",
                table: "Domains",
                column: "RedirectTargetDomainId",
                principalTable: "Domains",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SslCertificateOperations_CertificateId",
                table: "SslCertificateOperations",
                column: "CertificateId",
                principalTable: "SslCertificates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SslCertificates_ReplacementCertificateId",
                table: "SslCertificates",
                column: "ReplacementCertificateId",
                principalTable: "SslCertificates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

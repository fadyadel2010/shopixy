using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260805215917_DOMAIN_2C_0_CreateDomainTables")]
    public partial class DOMAIN_2C_0_CreateDomainTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Domains",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    HostName = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    NormalizedHostName = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    DomainType = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LifecycleState = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    SslCertificateId = table.Column<Guid>(type: "uuid", nullable: true),
                    VerificationToken = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, defaultValue: ""),
                    VerificationMethod = table.Column<int>(type: "integer", nullable: true),
                    RedirectType = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RedirectTargetDomainId = table.Column<Guid>(type: "uuid", nullable: true),
                    CanonicalPolicy = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SslActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    DomainHistoryJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Domains", x => x.Id);
                    table.CheckConstraint("CK_Domains_HostName_Length", "\"HostName\" IS NOT NULL AND LENGTH(TRIM(\"HostName\")) >= 1 AND LENGTH(\"HostName\") <= 253");
                    table.CheckConstraint("CK_Domains_Redirect_Cascade", "(\"DomainType\" <> 2 OR (\"RedirectTargetDomainId\" IS NOT NULL AND \"RedirectTargetDomainId\" <> \"Id\" AND \"RedirectType\" <> 0))");
                    table.CheckConstraint("CK_Domains_Primary_Host_Ssl", "(\"LifecycleState\" <> 5 AND \"LifecycleState\" <> 4) OR \"SslCertificateId\" IS NOT NULL");
                    table.CheckConstraint("CK_Domains_System_NoTenant", "(\"DomainType\" NOT IN (5, 6)) OR \"TenantId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_Domains_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Domains_RedirectTargetDomainId",
                        column: x => x.RedirectTargetDomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DomainAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DomainId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActorType = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    BeforeSnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    TraceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DomainAuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DomainAuditEntries_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DomainVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DomainId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DomainVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DomainVerifications_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SslCertificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DomainId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CertificateArn = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Thumbprint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    NotBeforeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRenewalAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextRenewalUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SslCertificateHistoryJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SslCertificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SslCertificates_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // --------------------------------------------------------------------------
            // Domains indexes (DOMAIN-1.5 Section 10)
            // --------------------------------------------------------------------------

            migrationBuilder.CreateIndex(
                name: "UQ_Domains_NormalizedHostName",
                table: "Domains",
                column: "NormalizedHostName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Domains_TenantId",
                table: "Domains",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Domains_TenantId_LifecycleState",
                table: "Domains",
                columns: new[] { "TenantId", "LifecycleState" });

            migrationBuilder.CreateIndex(
                name: "IX_Domains_LifecycleState",
                table: "Domains",
                column: "LifecycleState");

            migrationBuilder.CreateIndex(
                name: "IX_Domains_DomainType",
                table: "Domains",
                column: "DomainType");

            migrationBuilder.CreateIndex(
                name: "IX_Domains_SslCertificateId",
                table: "Domains",
                column: "SslCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_Domains_RedirectTargetDomainId",
                table: "Domains",
                column: "RedirectTargetDomainId");

            migrationBuilder.CreateIndex(
                name: "IX_Domains_IsDeleted",
                table: "Domains",
                column: "IsDeleted",
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UQ_Domains_TenantId_Primary",
                table: "Domains",
                column: "TenantId",
                unique: true,
                filter: "\"DomainType\" = 0 AND \"LifecycleState\" = 5 AND \"IsDeleted\" = false");

            // --------------------------------------------------------------------------
            // SslCertificates indexes (DOMAIN-1.5 Section 10.3)
            // --------------------------------------------------------------------------

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_DomainId",
                table: "SslCertificates",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "UQ_SslCertificates_DomainId_UniqueActive",
                table: "SslCertificates",
                column: "DomainId",
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" IN (0, 1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_Status",
                table: "SslCertificates",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_NotAfterUtc",
                table: "SslCertificates",
                column: "NotAfterUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_NextRenewalUtc",
                table: "SslCertificates",
                column: "NextRenewalUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_IsDeleted",
                table: "SslCertificates",
                column: "IsDeleted",
                filter: "\"IsDeleted\" = false");

            // --------------------------------------------------------------------------
            // DomainVerifications indexes (DOMAIN-1.5 Section 10.4, 10.6)
            // --------------------------------------------------------------------------

            migrationBuilder.CreateIndex(
                name: "IX_DomainVerifications_DomainId",
                table: "DomainVerifications",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_DomainVerifications_DomainId_Status",
                table: "DomainVerifications",
                columns: new[] { "DomainId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DomainVerifications_AttemptedAtUtc",
                table: "DomainVerifications",
                column: "AttemptedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DomainVerifications_IsDeleted",
                table: "DomainVerifications",
                column: "IsDeleted",
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UQ_DomainVerifications_DomainId_Method",
                table: "DomainVerifications",
                columns: new[] { "DomainId", "Method" },
                unique: true,
                filter: "\"Status\" = 0");

            // --------------------------------------------------------------------------
            // DomainAuditEntries indexes (DOMAIN-1.5 Section 10.2, 10.5)
            // --------------------------------------------------------------------------

            migrationBuilder.CreateIndex(
                name: "IX_DomainAuditEntries_DomainId",
                table: "DomainAuditEntries",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_DomainAuditEntries_TenantId_TimestampUtc",
                table: "DomainAuditEntries",
                columns: new[] { "TenantId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DomainAuditEntries_TimestampUtc",
                table: "DomainAuditEntries",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DomainAuditEntries_TraceId",
                table: "DomainAuditEntries",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_DomainAuditEntries_Action",
                table: "DomainAuditEntries",
                column: "Action");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_domain_audit_entries_mutation()
                RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'DomainAuditEntries records are append-only and cannot be updated or deleted.';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_domain_audit_entries_append_only
                BEFORE UPDATE OR DELETE ON "DomainAuditEntries"
                FOR EACH ROW EXECUTE FUNCTION prevent_domain_audit_entries_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_domain_audit_entries_append_only ON "DomainAuditEntries";
                DROP FUNCTION IF EXISTS prevent_domain_audit_entries_mutation();
                """);

            migrationBuilder.DropTable(name: "DomainAuditEntries");
            migrationBuilder.DropTable(name: "DomainVerifications");
            migrationBuilder.DropTable(name: "SslCertificates");
            migrationBuilder.DropTable(name: "Domains");
        }
    }
}

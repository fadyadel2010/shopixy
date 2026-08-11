using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DOMAIN_5_2_CertificatePersistenceFreeze : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SslCertificates_DomainId",
                table: "SslCertificates");

            migrationBuilder.DropIndex(
                name: "IX_SslCertificates_NextRenewalUtc",
                table: "SslCertificates");

            migrationBuilder.DropIndex(
                name: "IX_SslCertificates_NotAfterUtc",
                table: "SslCertificates");

            migrationBuilder.DropIndex(
                name: "UQ_SslCertificates_DomainId_UniqueActive",
                table: "SslCertificates");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Domains_TenantId_Id",
                table: "Domains",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SslCertificates",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedHostName",
                table: "SslCertificates",
                type: "character varying(253)",
                maxLength: 253,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Generation",
                table: "SslCertificates",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "OperationPhase",
                table: "SslCertificates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "OperationId",
                table: "SslCertificates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "SslCertificates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderImplementation",
                table: "SslCertificates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderAccountReference",
                table: "SslCertificates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderEnvironment",
                table: "SslCertificates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderRequestReference",
                table: "SslCertificates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderReference",
                table: "SslCertificates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizationReference",
                table: "SslCertificates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChallengeReference",
                table: "SslCertificates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EdgeCertificateReference",
                table: "SslCertificates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CaddyStorageReference",
                table: "SslCertificates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Issuer",
                table: "SslCertificates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "SslCertificates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectAlternativeNamesJson",
                table: "SslCertificates",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                table: "SslCertificates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "SslCertificates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureAlgorithm",
                table: "SslCertificates",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IssuedAtUtc",
                table: "SslCertificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActivatedAtUtc",
                table: "SslCertificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiredAtUtc",
                table: "SslCertificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EdgeCluster",
                table: "SslCertificates",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EdgeIdentifier",
                table: "SslCertificates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeployedGeneration",
                table: "SslCertificates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ObservedGeneration",
                table: "SslCertificates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEdgeProbeAtUtc",
                table: "SslCertificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastObservedThumbprint",
                table: "SslCertificates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActivationStartedAtUtc",
                table: "SslCertificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedThumbprint",
                table: "SslCertificates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActivationFailureCode",
                table: "SslCertificates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActivationFailureReason",
                table: "SslCertificates",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActivationFailedAtUtc",
                table: "SslCertificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RenewalEligible",
                table: "SslCertificates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RenewalAttemptCount",
                table: "SslCertificates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetryAfterUtc",
                table: "SslCertificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BackoffReason",
                table: "SslCertificates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacementCertificateId",
                table: "SslCertificates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAtUtc",
                table: "SslCertificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevokedByActorType",
                table: "SslCertificates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RevokedByActorId",
                table: "SslCertificates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevocationReason",
                table: "SslCertificates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderRevocationReference",
                table: "SslCertificates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EdgeDeactivationAtUtc",
                table: "SslCertificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureCode",
                table: "SslCertificates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafeFailureReason",
                table: "SslCertificates",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderErrorClass",
                table: "SslCertificates",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Retryable",
                table: "SslCertificates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFailureAtUtc",
                table: "SslCertificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "SslCertificates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FencingToken",
                table: "SslCertificates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "LockOwnerReference",
                table: "SslCertificates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAtUtc",
                table: "SslCertificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                table: "SslCertificates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastOperationAuditId",
                table: "SslCertificates",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "SslCertificates" AS c
                SET "TenantId" = d."TenantId",
                    "NormalizedHostName" = d."NormalizedHostName"
                FROM "Domains" AS d
                WHERE c."DomainId" = d."Id";

                UPDATE "SslCertificates"
                SET "SubjectAlternativeNamesJson" = "CertificateArn"::jsonb
                WHERE "CertificateArn" IS NOT NULL
                  AND "CertificateArn" ~ '^[[:space:]]*\[';

                WITH ranked AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "TenantId", "DomainId"
                               ORDER BY "CreatedAt", "Id") AS rn
                    FROM "SslCertificates"
                    WHERE "IsDeleted" = false
                )
                UPDATE "SslCertificates" AS c
                SET "Generation" = ranked.rn
                FROM ranked
                WHERE c."Id" = ranked."Id";

                UPDATE "SslCertificates"
                SET "RevokedAtUtc" = COALESCE("UpdatedAt", "CreatedAt", NOW())
                WHERE "Status" = 3 AND "RevokedAtUtc" IS NULL;

                UPDATE "SslCertificates"
                SET "ExpiredAtUtc" = COALESCE("NotAfterUtc", "UpdatedAt", "CreatedAt", NOW())
                WHERE "Status" = 2 AND "ExpiredAtUtc" IS NULL AND "NotAfterUtc" IS NULL;

                UPDATE "SslCertificates"
                SET "FailureCode" = 'legacy_failed'
                WHERE "Status" = 4
                  AND "FailureCode" IS NULL
                  AND "SafeFailureReason" IS NULL
                  AND "FailureReason" IS NULL;
                """);

            migrationBuilder.CreateTable(
                name: "SslCertificateOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DomainId = table.Column<Guid>(type: "uuid", nullable: false),
                    CertificateId = table.Column<Guid>(type: "uuid", nullable: true),
                    NormalizedHostName = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    OperationType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProviderRequestReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EdgeCertificateReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FencingToken = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    LockOwnerReference = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RetryAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SafeFailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Retryable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SslCertificateOperations", x => x.Id);
                    table.CheckConstraint("CK_SslCertificateOperations_Fencing_NonNegative", "\"FencingToken\" >= 0");
                    table.CheckConstraint("CK_SslCertificateOperations_HostName_Length", "\"NormalizedHostName\" IS NOT NULL AND LENGTH(TRIM(\"NormalizedHostName\")) >= 1 AND LENGTH(\"NormalizedHostName\") <= 253");
                    table.CheckConstraint("CK_SslCertificateOperations_IdempotencyKey_NotBlank", "\"IdempotencyKey\" IS NOT NULL AND LENGTH(TRIM(\"IdempotencyKey\")) >= 1");
                    table.ForeignKey(
                        name: "FK_SslCertificateOperations_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "SslCertificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SslCertificateOperations_DomainId",
                        columns: x => new { x.TenantId, x.DomainId },
                        principalTable: "Domains",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_NextRenewalUtc_Status",
                table: "SslCertificates",
                columns: new[] { "NextRenewalUtc", "Status" },
                filter: "\"NextRenewalUtc\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_NotAfterUtc_Status",
                table: "SslCertificates",
                columns: new[] { "NotAfterUtc", "Status" },
                filter: "\"NotAfterUtc\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_RetryAfterUtc_Status",
                table: "SslCertificates",
                columns: new[] { "RetryAfterUtc", "Status" },
                filter: "\"RetryAfterUtc\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_Status_OperationPhase",
                table: "SslCertificates",
                columns: new[] { "Status", "OperationPhase" });

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_TenantId_DomainId_IsDeleted",
                table: "SslCertificates",
                columns: new[] { "TenantId", "DomainId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_TenantId_DomainId_OperationPhase",
                table: "SslCertificates",
                columns: new[] { "TenantId", "DomainId", "OperationPhase" },
                filter: "\"IsDeleted\" = false AND \"OperationPhase\" <> 0");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_TenantId_NormalizedHostName_Status",
                table: "SslCertificates",
                columns: new[] { "TenantId", "NormalizedHostName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_ReplacementCertificateId",
                table: "SslCertificates",
                column: "ReplacementCertificateId");

            migrationBuilder.CreateIndex(
                name: "UQ_SslCertificates_TenantId_DomainId_Active",
                table: "SslCertificates",
                columns: new[] { "TenantId", "DomainId" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "UQ_SslCertificates_TenantId_DomainId_ActiveActivatingGeneration",
                table: "SslCertificates",
                columns: new[] { "TenantId", "DomainId", "Generation" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" IN (1, 8)");

            migrationBuilder.CreateIndex(
                name: "UQ_SslCertificates_TenantId_DomainId_Generation",
                table: "SslCertificates",
                columns: new[] { "TenantId", "DomainId", "Generation" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UQ_SslCertificates_TenantId_DomainId_IdempotencyKey",
                table: "SslCertificates",
                columns: new[] { "TenantId", "DomainId", "IdempotencyKey" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SslCertificates_Activation_ExpectedThumbprint",
                table: "SslCertificates",
                sql: "\"Status\" <> 8 OR \"ExpectedThumbprint\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SslCertificates_Expired_Metadata",
                table: "SslCertificates",
                sql: "\"Status\" <> 2 OR \"ExpiredAtUtc\" IS NOT NULL OR \"NotAfterUtc\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SslCertificates_Failed_Metadata",
                table: "SslCertificates",
                sql: "\"Status\" <> 4 OR \"FailureCode\" IS NOT NULL OR \"SafeFailureReason\" IS NOT NULL OR \"FailureReason\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SslCertificates_Generation_Positive",
                table: "SslCertificates",
                sql: "\"Generation\" >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SslCertificates_HostName_Length",
                table: "SslCertificates",
                sql: "\"NormalizedHostName\" IS NOT NULL AND LENGTH(TRIM(\"NormalizedHostName\")) >= 1 AND LENGTH(\"NormalizedHostName\") <= 253");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SslCertificates_Revoked_Metadata",
                table: "SslCertificates",
                sql: "\"Status\" <> 3 OR \"RevokedAtUtc\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SslCertificates_Status_Canonical",
                table: "SslCertificates",
                sql: "\"Status\" IN (0, 1, 2, 3, 4, 5, 6, 7, 8, 9)");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificateOperations_CertificateId",
                table: "SslCertificateOperations",
                column: "CertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificateOperations_DomainId",
                table: "SslCertificateOperations",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificateOperations_LeaseExpiresAtUtc_Status",
                table: "SslCertificateOperations",
                columns: new[] { "LeaseExpiresAtUtc", "Status" },
                filter: "\"LeaseExpiresAtUtc\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificateOperations_RetryAfterUtc_Status",
                table: "SslCertificateOperations",
                columns: new[] { "RetryAfterUtc", "Status" },
                filter: "\"RetryAfterUtc\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificateOperations_Tenant_Domain_Certificate",
                table: "SslCertificateOperations",
                columns: new[] { "TenantId", "DomainId", "CertificateId" },
                filter: "\"IsDeleted\" = false AND \"CertificateId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificateOperations_Tenant_Domain_FencingToken",
                table: "SslCertificateOperations",
                columns: new[] { "TenantId", "DomainId", "FencingToken" },
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UQ_SslCertificateOperations_Tenant_Domain_Type_IdempotencyKey",
                table: "SslCertificateOperations",
                columns: new[] { "TenantId", "DomainId", "OperationType", "IdempotencyKey" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UQ_SslCertificateOperations_Tenant_Domain_Type_InProgress",
                table: "SslCertificateOperations",
                columns: new[] { "TenantId", "DomainId", "OperationType" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" IN (0, 1)");

            migrationBuilder.AddForeignKey(
                name: "FK_SslCertificates_DomainId",
                table: "SslCertificates",
                columns: new[] { "TenantId", "DomainId" },
                principalTable: "Domains",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SslCertificates_ReplacementCertificateId",
                table: "SslCertificates",
                column: "ReplacementCertificateId",
                principalTable: "SslCertificates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SslCertificates_DomainId",
                table: "SslCertificates");

            migrationBuilder.DropForeignKey(
                name: "FK_SslCertificates_ReplacementCertificateId",
                table: "SslCertificates");

            migrationBuilder.DropTable(
                name: "SslCertificateOperations");

            migrationBuilder.DropIndex(name: "IX_SslCertificates_NextRenewalUtc_Status", table: "SslCertificates");
            migrationBuilder.DropIndex(name: "IX_SslCertificates_NotAfterUtc_Status", table: "SslCertificates");
            migrationBuilder.DropIndex(name: "IX_SslCertificates_RetryAfterUtc_Status", table: "SslCertificates");
            migrationBuilder.DropIndex(name: "IX_SslCertificates_Status_OperationPhase", table: "SslCertificates");
            migrationBuilder.DropIndex(name: "IX_SslCertificates_TenantId_DomainId_IsDeleted", table: "SslCertificates");
            migrationBuilder.DropIndex(name: "IX_SslCertificates_TenantId_DomainId_OperationPhase", table: "SslCertificates");
            migrationBuilder.DropIndex(name: "IX_SslCertificates_TenantId_NormalizedHostName_Status", table: "SslCertificates");
            migrationBuilder.DropIndex(name: "IX_SslCertificates_ReplacementCertificateId", table: "SslCertificates");
            migrationBuilder.DropIndex(name: "UQ_SslCertificates_TenantId_DomainId_Active", table: "SslCertificates");
            migrationBuilder.DropIndex(name: "UQ_SslCertificates_TenantId_DomainId_ActiveActivatingGeneration", table: "SslCertificates");
            migrationBuilder.DropIndex(name: "UQ_SslCertificates_TenantId_DomainId_Generation", table: "SslCertificates");
            migrationBuilder.DropIndex(name: "UQ_SslCertificates_TenantId_DomainId_IdempotencyKey", table: "SslCertificates");

            migrationBuilder.DropCheckConstraint(name: "CK_SslCertificates_Activation_ExpectedThumbprint", table: "SslCertificates");
            migrationBuilder.DropCheckConstraint(name: "CK_SslCertificates_Expired_Metadata", table: "SslCertificates");
            migrationBuilder.DropCheckConstraint(name: "CK_SslCertificates_Failed_Metadata", table: "SslCertificates");
            migrationBuilder.DropCheckConstraint(name: "CK_SslCertificates_Generation_Positive", table: "SslCertificates");
            migrationBuilder.DropCheckConstraint(name: "CK_SslCertificates_HostName_Length", table: "SslCertificates");
            migrationBuilder.DropCheckConstraint(name: "CK_SslCertificates_Revoked_Metadata", table: "SslCertificates");
            migrationBuilder.DropCheckConstraint(name: "CK_SslCertificates_Status_Canonical", table: "SslCertificates");

            migrationBuilder.DropColumn(name: "ActivatedAtUtc", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "ActivationFailedAtUtc", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "ActivationFailureCode", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "ActivationFailureReason", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "ActivationStartedAtUtc", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "AuthorizationReference", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "BackoffReason", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "CaddyStorageReference", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "ChallengeReference", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "CorrelationId", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "CreatedBy", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "DeployedGeneration", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "EdgeCertificateReference", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "EdgeCluster", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "EdgeDeactivationAtUtc", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "EdgeIdentifier", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "ExpectedThumbprint", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "ExpiredAtUtc", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "FailureCode", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "FencingToken", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "Fingerprint", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "Generation", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "IdempotencyKey", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "IssuedAtUtc", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "Issuer", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "LastEdgeProbeAtUtc", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "LastFailureAtUtc", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "LastObservedThumbprint", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "LastOperationAuditId", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "LeaseExpiresAtUtc", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "LockOwnerReference", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "NormalizedHostName", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "ObservedGeneration", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "OperationId", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "OperationPhase", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "OrderReference", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "ProviderAccountReference", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "ProviderEnvironment", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "ProviderErrorClass", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "ProviderImplementation", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "ProviderRequestReference", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "ProviderRevocationReference", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "RenewalAttemptCount", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "RenewalEligible", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "ReplacementCertificateId", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "RetryAfterUtc", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "Retryable", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "RevocationReason", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "RevokedAtUtc", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "RevokedByActorId", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "RevokedByActorType", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "SafeFailureReason", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "SerialNumber", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "SignatureAlgorithm", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "Subject", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "SubjectAlternativeNamesJson", table: "SslCertificates");
            migrationBuilder.DropColumn(name: "TenantId", table: "SslCertificates");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Domains_TenantId_Id",
                table: "Domains");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_NextRenewalUtc",
                table: "SslCertificates",
                column: "NextRenewalUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_NotAfterUtc",
                table: "SslCertificates",
                column: "NotAfterUtc");

            migrationBuilder.CreateIndex(
                name: "UQ_SslCertificates_DomainId_UniqueActive",
                table: "SslCertificates",
                column: "DomainId",
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" IN (0, 1, 2)");

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

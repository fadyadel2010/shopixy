START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" DROP CONSTRAINT "FK_SslCertificates_DomainId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    DROP INDEX "IX_SslCertificates_NextRenewalUtc";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    DROP INDEX "IX_SslCertificates_NotAfterUtc";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    DROP INDEX "UQ_SslCertificates_DomainId_UniqueActive";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "Domains" ADD CONSTRAINT "AK_Domains_TenantId_Id" UNIQUE ("TenantId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "NormalizedHostName" character varying(253) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "Generation" integer NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "OperationPhase" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "OperationId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "IdempotencyKey" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ProviderImplementation" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ProviderAccountReference" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ProviderEnvironment" character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ProviderRequestReference" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "OrderReference" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "AuthorizationReference" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ChallengeReference" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "EdgeCertificateReference" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "CaddyStorageReference" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "Issuer" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "Subject" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "SubjectAlternativeNamesJson" jsonb NOT NULL DEFAULT '[]';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "Fingerprint" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "SerialNumber" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "SignatureAlgorithm" character varying(120);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "IssuedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ActivatedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ExpiredAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "EdgeCluster" character varying(120);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "EdgeIdentifier" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "DeployedGeneration" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ObservedGeneration" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "LastEdgeProbeAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "LastObservedThumbprint" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ActivationStartedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ExpectedThumbprint" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ActivationFailureCode" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ActivationFailureReason" character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ActivationFailedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "RenewalEligible" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "RenewalAttemptCount" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "RetryAfterUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "BackoffReason" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ReplacementCertificateId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "RevokedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "RevokedByActorType" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "RevokedByActorId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "RevocationReason" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ProviderRevocationReference" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "EdgeDeactivationAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "FailureCode" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "SafeFailureReason" character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ProviderErrorClass" character varying(120);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "Retryable" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "LastFailureAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "CorrelationId" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "FencingToken" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "LockOwnerReference" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "LeaseExpiresAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "CreatedBy" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "LastOperationAuditId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE TABLE "SslCertificateOperations" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "DomainId" uuid NOT NULL,
        "CertificateId" uuid,
        "NormalizedHostName" character varying(253) NOT NULL,
        "OperationType" integer NOT NULL,
        "Status" integer NOT NULL,
        "Phase" integer NOT NULL,
        "OperationId" uuid NOT NULL,
        "IdempotencyKey" character varying(200) NOT NULL,
        "RequestHash" character varying(128),
        "ProviderRequestReference" character varying(500),
        "EdgeCertificateReference" character varying(500),
        "FencingToken" bigint NOT NULL DEFAULT 0,
        "LockOwnerReference" uuid,
        "LeaseExpiresAtUtc" timestamp with time zone,
        "AttemptCount" integer NOT NULL DEFAULT 0,
        "RetryAfterUtc" timestamp with time zone,
        "FailureCode" character varying(100),
        "SafeFailureReason" character varying(1000),
        "Retryable" boolean NOT NULL DEFAULT FALSE,
        "CorrelationId" character varying(100),
        "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT (NOW()),
        "UpdatedAtUtc" timestamp with time zone,
        "CompletedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL DEFAULT FALSE,
        CONSTRAINT "PK_SslCertificateOperations" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_SslCertificateOperations_Fencing_NonNegative" CHECK ("FencingToken" >= 0),
        CONSTRAINT "CK_SslCertificateOperations_HostName_Length" CHECK ("NormalizedHostName" IS NOT NULL AND LENGTH(TRIM("NormalizedHostName")) >= 1 AND LENGTH("NormalizedHostName") <= 253),
        CONSTRAINT "CK_SslCertificateOperations_IdempotencyKey_NotBlank" CHECK ("IdempotencyKey" IS NOT NULL AND LENGTH(TRIM("IdempotencyKey")) >= 1),
        CONSTRAINT "FK_SslCertificateOperations_CertificateId" FOREIGN KEY ("CertificateId") REFERENCES "SslCertificates" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_SslCertificateOperations_DomainId" FOREIGN KEY ("TenantId", "DomainId") REFERENCES "Domains" ("TenantId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificates_NextRenewalUtc_Status" ON "SslCertificates" ("NextRenewalUtc", "Status") WHERE "NextRenewalUtc" IS NOT NULL AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificates_NotAfterUtc_Status" ON "SslCertificates" ("NotAfterUtc", "Status") WHERE "NotAfterUtc" IS NOT NULL AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificates_RetryAfterUtc_Status" ON "SslCertificates" ("RetryAfterUtc", "Status") WHERE "RetryAfterUtc" IS NOT NULL AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificates_Status_OperationPhase" ON "SslCertificates" ("Status", "OperationPhase");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificates_TenantId_DomainId_IsDeleted" ON "SslCertificates" ("TenantId", "DomainId", "IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificates_TenantId_DomainId_OperationPhase" ON "SslCertificates" ("TenantId", "DomainId", "OperationPhase") WHERE "IsDeleted" = false AND "OperationPhase" <> 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificates_TenantId_NormalizedHostName_Status" ON "SslCertificates" ("TenantId", "NormalizedHostName", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificates_ReplacementCertificateId" ON "SslCertificates" ("ReplacementCertificateId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE UNIQUE INDEX "UQ_SslCertificates_TenantId_DomainId_Active" ON "SslCertificates" ("TenantId", "DomainId") WHERE "IsDeleted" = false AND "Status" = 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE UNIQUE INDEX "UQ_SslCertificates_TenantId_DomainId_ActiveActivatingGeneration" ON "SslCertificates" ("TenantId", "DomainId", "Generation") WHERE "IsDeleted" = false AND "Status" IN (1, 8);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE UNIQUE INDEX "UQ_SslCertificates_TenantId_DomainId_Generation" ON "SslCertificates" ("TenantId", "DomainId", "Generation") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE UNIQUE INDEX "UQ_SslCertificates_TenantId_DomainId_IdempotencyKey" ON "SslCertificates" ("TenantId", "DomainId", "IdempotencyKey") WHERE "IsDeleted" = false AND "IdempotencyKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "CK_SslCertificates_Activation_ExpectedThumbprint" CHECK ("Status" <> 8 OR "ExpectedThumbprint" IS NOT NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "CK_SslCertificates_Expired_Metadata" CHECK ("Status" <> 2 OR "ExpiredAtUtc" IS NOT NULL OR "NotAfterUtc" IS NOT NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "CK_SslCertificates_Failed_Metadata" CHECK ("Status" <> 4 OR "FailureCode" IS NOT NULL OR "SafeFailureReason" IS NOT NULL OR "FailureReason" IS NOT NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "CK_SslCertificates_Generation_Positive" CHECK ("Generation" >= 1);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "CK_SslCertificates_HostName_Length" CHECK ("NormalizedHostName" IS NOT NULL AND LENGTH(TRIM("NormalizedHostName")) >= 1 AND LENGTH("NormalizedHostName") <= 253);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "CK_SslCertificates_Revoked_Metadata" CHECK ("Status" <> 3 OR "RevokedAtUtc" IS NOT NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "CK_SslCertificates_Status_Canonical" CHECK ("Status" IN (0, 1, 2, 3, 4, 5, 6, 7, 8, 9));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificateOperations_CertificateId" ON "SslCertificateOperations" ("CertificateId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificateOperations_DomainId" ON "SslCertificateOperations" ("DomainId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificateOperations_LeaseExpiresAtUtc_Status" ON "SslCertificateOperations" ("LeaseExpiresAtUtc", "Status") WHERE "LeaseExpiresAtUtc" IS NOT NULL AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificateOperations_RetryAfterUtc_Status" ON "SslCertificateOperations" ("RetryAfterUtc", "Status") WHERE "RetryAfterUtc" IS NOT NULL AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificateOperations_Tenant_Domain_Certificate" ON "SslCertificateOperations" ("TenantId", "DomainId", "CertificateId") WHERE "IsDeleted" = false AND "CertificateId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificateOperations_Tenant_Domain_FencingToken" ON "SslCertificateOperations" ("TenantId", "DomainId", "FencingToken") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE UNIQUE INDEX "UQ_SslCertificateOperations_Tenant_Domain_Type_IdempotencyKey" ON "SslCertificateOperations" ("TenantId", "DomainId", "OperationType", "IdempotencyKey") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE UNIQUE INDEX "UQ_SslCertificateOperations_Tenant_Domain_Type_InProgress" ON "SslCertificateOperations" ("TenantId", "DomainId", "OperationType") WHERE "IsDeleted" = false AND "Status" IN (0, 1);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "FK_SslCertificates_DomainId" FOREIGN KEY ("TenantId", "DomainId") REFERENCES "Domains" ("TenantId", "Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "FK_SslCertificates_ReplacementCertificateId" FOREIGN KEY ("ReplacementCertificateId") REFERENCES "SslCertificates" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze', '9.0.1');
    END IF;
END $EF$;
COMMIT;


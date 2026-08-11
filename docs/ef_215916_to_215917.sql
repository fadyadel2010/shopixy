START TRANSACTION;
CREATE TABLE "Domains" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "HostName" character varying(253) NOT NULL,
    "NormalizedHostName" character varying(253) NOT NULL,
    "DomainType" integer NOT NULL DEFAULT 0,
    "LifecycleState" integer NOT NULL DEFAULT 0,
    "SslCertificateId" uuid,
    "VerificationToken" character varying(256) NOT NULL DEFAULT '',
    "VerificationMethod" integer,
    "RedirectType" integer NOT NULL DEFAULT 0,
    "RedirectTargetDomainId" uuid,
    "CanonicalPolicy" integer NOT NULL DEFAULT 0,
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT (NOW()),
    "VerifiedAtUtc" timestamp with time zone,
    "SslActivatedAtUtc" timestamp with time zone,
    "UpdatedAtUtc" timestamp with time zone,
    "DeletedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DomainHistoryJson" jsonb NOT NULL DEFAULT '[]',
    CONSTRAINT "PK_Domains" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Domains_HostName_Length" CHECK ("HostName" IS NOT NULL AND LENGTH(TRIM("HostName")) >= 1 AND LENGTH("HostName") <= 253),
    CONSTRAINT "CK_Domains_Redirect_Cascade" CHECK (("DomainType" <> 2 OR ("RedirectTargetDomainId" IS NOT NULL AND "RedirectTargetDomainId" <> "Id" AND "RedirectType" <> 0))),
    CONSTRAINT "CK_Domains_Primary_Host_Ssl" CHECK (("LifecycleState" <> 5 AND "LifecycleState" <> 4) OR "SslCertificateId" IS NOT NULL),
    CONSTRAINT "CK_Domains_System_NoTenant" CHECK (("DomainType" NOT IN (5, 6)) OR "TenantId" IS NOT NULL),
    CONSTRAINT "FK_Domains_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Domains_RedirectTargetDomainId" FOREIGN KEY ("RedirectTargetDomainId") REFERENCES "Domains" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "DomainAuditEntries" (
    "Id" uuid NOT NULL,
    "DomainId" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Action" character varying(100) NOT NULL,
    "ActorType" integer NOT NULL,
    "ActorUserId" uuid,
    "BeforeSnapshotJson" jsonb,
    "AfterSnapshotJson" jsonb NOT NULL,
    "TraceId" character varying(128),
    "IpAddress" character varying(45),
    "UserAgent" character varying(500),
    "TimestampUtc" timestamp with time zone NOT NULL DEFAULT (NOW()),
    CONSTRAINT "PK_DomainAuditEntries" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_DomainAuditEntries_DomainId" FOREIGN KEY ("DomainId") REFERENCES "Domains" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "DomainVerifications" (
    "Id" uuid NOT NULL,
    "DomainId" uuid NOT NULL,
    "Method" integer NOT NULL,
    "Token" character varying(256) NOT NULL,
    "Status" integer NOT NULL,
    "AttemptedAtUtc" timestamp with time zone NOT NULL DEFAULT (NOW()),
    "VerifiedAtUtc" timestamp with time zone,
    "FailureReason" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
    "UpdatedAt" timestamp with time zone,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_DomainVerifications" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_DomainVerifications_DomainId" FOREIGN KEY ("DomainId") REFERENCES "Domains" ("Id") ON DELETE CASCADE
);

CREATE TABLE "SslCertificates" (
    "Id" uuid NOT NULL,
    "DomainId" uuid NOT NULL,
    "Provider" integer NOT NULL,
    "Status" integer NOT NULL,
    "CertificateArn" character varying(500),
    "Thumbprint" character varying(200),
    "NotBeforeUtc" timestamp with time zone,
    "NotAfterUtc" timestamp with time zone,
    "LastRenewalAttemptUtc" timestamp with time zone,
    "NextRenewalUtc" timestamp with time zone,
    "FailureReason" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
    "UpdatedAt" timestamp with time zone,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "SslCertificateHistoryJson" jsonb NOT NULL DEFAULT '[]',
    CONSTRAINT "PK_SslCertificates" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SslCertificates_DomainId" FOREIGN KEY ("DomainId") REFERENCES "Domains" ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "UQ_Domains_NormalizedHostName" ON "Domains" ("NormalizedHostName");

CREATE INDEX "IX_Domains_TenantId" ON "Domains" ("TenantId");

CREATE INDEX "IX_Domains_TenantId_LifecycleState" ON "Domains" ("TenantId", "LifecycleState");

CREATE INDEX "IX_Domains_LifecycleState" ON "Domains" ("LifecycleState");

CREATE INDEX "IX_Domains_DomainType" ON "Domains" ("DomainType");

CREATE INDEX "IX_Domains_SslCertificateId" ON "Domains" ("SslCertificateId");

CREATE INDEX "IX_Domains_RedirectTargetDomainId" ON "Domains" ("RedirectTargetDomainId");

CREATE INDEX "IX_Domains_IsDeleted" ON "Domains" ("IsDeleted") WHERE "IsDeleted" = false;

CREATE UNIQUE INDEX "UQ_Domains_TenantId_Primary" ON "Domains" ("TenantId") WHERE "DomainType" = 0 AND "LifecycleState" = 5 AND "IsDeleted" = false;

CREATE INDEX "IX_SslCertificates_DomainId" ON "SslCertificates" ("DomainId");

CREATE UNIQUE INDEX "UQ_SslCertificates_DomainId_UniqueActive" ON "SslCertificates" ("DomainId") WHERE "IsDeleted" = false AND "Status" IN (0, 1, 2);

CREATE INDEX "IX_SslCertificates_Status" ON "SslCertificates" ("Status");

CREATE INDEX "IX_SslCertificates_NotAfterUtc" ON "SslCertificates" ("NotAfterUtc");

CREATE INDEX "IX_SslCertificates_NextRenewalUtc" ON "SslCertificates" ("NextRenewalUtc");

CREATE INDEX "IX_SslCertificates_IsDeleted" ON "SslCertificates" ("IsDeleted") WHERE "IsDeleted" = false;

CREATE INDEX "IX_DomainVerifications_DomainId" ON "DomainVerifications" ("DomainId");

CREATE INDEX "IX_DomainVerifications_DomainId_Status" ON "DomainVerifications" ("DomainId", "Status");

CREATE INDEX "IX_DomainVerifications_AttemptedAtUtc" ON "DomainVerifications" ("AttemptedAtUtc");

CREATE INDEX "IX_DomainVerifications_IsDeleted" ON "DomainVerifications" ("IsDeleted") WHERE "IsDeleted" = false;

CREATE UNIQUE INDEX "UQ_DomainVerifications_DomainId_Method" ON "DomainVerifications" ("DomainId", "Method") WHERE "Status" = 0;

CREATE INDEX "IX_DomainAuditEntries_DomainId" ON "DomainAuditEntries" ("DomainId");

CREATE INDEX "IX_DomainAuditEntries_TenantId_TimestampUtc" ON "DomainAuditEntries" ("TenantId", "TimestampUtc");

CREATE INDEX "IX_DomainAuditEntries_TimestampUtc" ON "DomainAuditEntries" ("TimestampUtc");

CREATE INDEX "IX_DomainAuditEntries_TraceId" ON "DomainAuditEntries" ("TraceId");

CREATE INDEX "IX_DomainAuditEntries_Action" ON "DomainAuditEntries" ("Action");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260805215917_DOMAIN_2C_0_CreateDomainTables', '9.0.1');

COMMIT;


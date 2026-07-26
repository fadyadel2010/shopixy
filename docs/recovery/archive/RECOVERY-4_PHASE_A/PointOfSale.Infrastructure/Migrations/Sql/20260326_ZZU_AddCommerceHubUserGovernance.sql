BEGIN;

CREATE TABLE IF NOT EXISTS "UserGovernanceProfiles"
(
    "Id" uuid NOT NULL,
    "SubjectId" uuid NOT NULL,
    "Role" integer NOT NULL,
    "Status" integer NOT NULL DEFAULT 0,
    "RiskLevel" integer NOT NULL DEFAULT 0,
    "IsKycVerified" boolean NOT NULL DEFAULT FALSE,
    "SuspiciousFlags" integer NOT NULL DEFAULT 0,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "LastLoginAtUtc" timestamp with time zone NULL,
    "StatusChangedAtUtc" timestamp with time zone NULL,
    "LastRiskEvaluatedAtUtc" timestamp with time zone NULL,
    "LastModifiedByAdminId" uuid NULL,
    "StatusReason" character varying(1000) NULL,
    "LastRiskReason" character varying(1000) NULL,
    "LastIpAddress" character varying(128) NULL,
    "DeviceFingerprint" character varying(200) NULL,
    CONSTRAINT "PK_UserGovernanceProfiles" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_UserGovernanceProfiles_Role_SubjectId"
    ON "UserGovernanceProfiles" ("Role", "SubjectId");

CREATE INDEX IF NOT EXISTS "IX_UserGovernanceProfiles_Status"
    ON "UserGovernanceProfiles" ("Status");

CREATE INDEX IF NOT EXISTS "IX_UserGovernanceProfiles_RiskLevel"
    ON "UserGovernanceProfiles" ("RiskLevel");

CREATE TABLE IF NOT EXISTS "UserGovernanceAuditLogs"
(
    "Id" uuid NOT NULL,
    "TargetUserId" uuid NOT NULL,
    "TargetRole" integer NOT NULL,
    "AdminId" uuid NULL,
    "ActorType" character varying(50) NOT NULL,
    "ActionType" character varying(100) NOT NULL,
    "Reason" character varying(1000) NOT NULL,
    "BeforeState" jsonb NOT NULL,
    "AfterState" jsonb NOT NULL,
    "Metadata" jsonb NULL,
    "TimestampUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_UserGovernanceAuditLogs" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_UserGovernanceAuditLogs_TargetRole_TargetUserId_TimestampUtc"
    ON "UserGovernanceAuditLogs" ("TargetRole", "TargetUserId", "TimestampUtc" DESC);

CREATE INDEX IF NOT EXISTS "IX_UserGovernanceAuditLogs_AdminId"
    ON "UserGovernanceAuditLogs" ("AdminId");

CREATE OR REPLACE FUNCTION fn_user_governance_audit_logs_immutable()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'UserGovernanceAuditLogs is append-only. UPDATE and DELETE are forbidden.';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_user_governance_audit_logs_immutable
    ON "UserGovernanceAuditLogs";

CREATE TRIGGER trg_user_governance_audit_logs_immutable
    BEFORE UPDATE OR DELETE ON "UserGovernanceAuditLogs"
    FOR EACH ROW
    EXECUTE FUNCTION fn_user_governance_audit_logs_immutable();

CREATE TABLE IF NOT EXISTS "UserGovernanceBlacklistEntries"
(
    "Id" uuid NOT NULL,
    "Type" integer NOT NULL,
    "ValueNormalized" character varying(320) NOT NULL,
    "ValueDisplay" character varying(320) NOT NULL,
    "Reason" character varying(1000) NOT NULL,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "CreatedByAdminId" uuid NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "RevokedByAdminId" uuid NULL,
    "RevokedAtUtc" timestamp with time zone NULL,
    "RevokedReason" character varying(1000) NULL,
    CONSTRAINT "PK_UserGovernanceBlacklistEntries" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_UserGovernanceBlacklistEntries_Type_ValueNormalized"
    ON "UserGovernanceBlacklistEntries" ("Type", "ValueNormalized");

CREATE INDEX IF NOT EXISTS "IX_UserGovernanceBlacklistEntries_IsActive"
    ON "UserGovernanceBlacklistEntries" ("IsActive");

CREATE INDEX IF NOT EXISTS "IX_UserGovernanceBlacklistEntries_CreatedAtUtc"
    ON "UserGovernanceBlacklistEntries" ("CreatedAtUtc" DESC);

COMMIT;

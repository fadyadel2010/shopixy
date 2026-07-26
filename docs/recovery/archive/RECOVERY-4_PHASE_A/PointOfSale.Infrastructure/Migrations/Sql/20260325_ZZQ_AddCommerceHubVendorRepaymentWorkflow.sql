CREATE TABLE IF NOT EXISTS "CommerceHubVendorPaymentMethods"
(
    "Id" uuid NOT NULL,
    "Name" character varying(120) NOT NULL,
    "DestinationValue" character varying(200) NOT NULL,
    "Instructions" text NOT NULL,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "CreatedBy" uuid NOT NULL,
    CONSTRAINT "PK_CommerceHubVendorPaymentMethods" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubVendorPaymentMethods_Name_DestinationValue"
    ON "CommerceHubVendorPaymentMethods" ("Name", "DestinationValue");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorPaymentMethods_IsActive"
    ON "CommerceHubVendorPaymentMethods" ("IsActive");

CREATE TABLE IF NOT EXISTS "CommerceHubVendorPaymentRequests"
(
    "Id" uuid NOT NULL,
    "VendorId" uuid NOT NULL,
    "PaymentMethodId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL DEFAULT 'EGP',
    "Status" integer NOT NULL,
    "TransferReference" character varying(200),
    "IdempotencyKey" character varying(200) NOT NULL,
    "MethodNameSnapshot" character varying(120) NOT NULL,
    "DestinationValueSnapshot" character varying(200) NOT NULL,
    "InstructionsSnapshot" text NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "ApprovedAtUtc" timestamp with time zone NULL,
    "ApprovedBy" uuid NULL,
    "RejectedAtUtc" timestamp with time zone NULL,
    "RejectedBy" uuid NULL,
    "ReviewReason" text NULL,
    "LedgerCorrelationId" character varying(200) NULL,
    "LedgerIdempotencyKey" character varying(200) NULL,
    CONSTRAINT "PK_CommerceHubVendorPaymentRequests" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CommerceHubVendorPaymentRequests_Tenants_VendorId"
        FOREIGN KEY ("VendorId") REFERENCES "Tenants" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CommerceHubVendorPaymentRequests_CommerceHubVendorPaymentMethods_PaymentMethodId"
        FOREIGN KEY ("PaymentMethodId") REFERENCES "CommerceHubVendorPaymentMethods" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "CK_CommerceHubVendorPaymentRequests_Amount"
        CHECK ("Amount" > 0),
    CONSTRAINT "CK_CommerceHubVendorPaymentRequests_Status"
        CHECK ("Status" IN (0, 1, 2))
);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorPaymentRequests_VendorId_CreatedAtUtc"
    ON "CommerceHubVendorPaymentRequests" ("VendorId", "CreatedAtUtc" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorPaymentRequests_Status_CreatedAtUtc"
    ON "CommerceHubVendorPaymentRequests" ("Status", "CreatedAtUtc" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorPaymentRequests_VendorId_Status_CreatedAtUtc"
    ON "CommerceHubVendorPaymentRequests" ("VendorId", "Status", "CreatedAtUtc" DESC);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubVendorPaymentRequests_VendorId_IdempotencyKey"
    ON "CommerceHubVendorPaymentRequests" ("VendorId", "IdempotencyKey");

CREATE TABLE IF NOT EXISTS "CommerceHubVendorPaymentProofs"
(
    "Id" uuid NOT NULL,
    "PaymentRequestId" uuid NOT NULL,
    "ObjectKey" character varying(500) NOT NULL,
    "OriginalFileName" character varying(255) NOT NULL,
    "ContentType" character varying(255) NOT NULL,
    "SizeBytes" bigint NOT NULL,
    "UploadedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_CommerceHubVendorPaymentProofs" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CommerceHubVendorPaymentProofs_CommerceHubVendorPaymentRequests_PaymentRequestId"
        FOREIGN KEY ("PaymentRequestId") REFERENCES "CommerceHubVendorPaymentRequests" ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubVendorPaymentProofs_PaymentRequestId"
    ON "CommerceHubVendorPaymentProofs" ("PaymentRequestId");

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubVendorPaymentProofs_ObjectKey"
    ON "CommerceHubVendorPaymentProofs" ("ObjectKey");

CREATE TABLE IF NOT EXISTS "CommerceHubVendorPaymentAuditLogs"
(
    "Id" uuid NOT NULL,
    "PaymentRequestId" uuid NOT NULL,
    "AdminId" uuid NOT NULL,
    "Action" integer NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL DEFAULT 'EGP',
    "Reason" text NOT NULL,
    "TimestampUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_CommerceHubVendorPaymentAuditLogs" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CommerceHubVendorPaymentAuditLogs_CommerceHubVendorPaymentRequests_PaymentRequestId"
        FOREIGN KEY ("PaymentRequestId") REFERENCES "CommerceHubVendorPaymentRequests" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "CK_CommerceHubVendorPaymentAuditLogs_Amount"
        CHECK ("Amount" > 0),
    CONSTRAINT "CK_CommerceHubVendorPaymentAuditLogs_Action"
        CHECK ("Action" IN (0, 1))
);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorPaymentAuditLogs_PaymentRequestId_TimestampUtc"
    ON "CommerceHubVendorPaymentAuditLogs" ("PaymentRequestId", "TimestampUtc" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorPaymentAuditLogs_AdminId_TimestampUtc"
    ON "CommerceHubVendorPaymentAuditLogs" ("AdminId", "TimestampUtc" DESC);

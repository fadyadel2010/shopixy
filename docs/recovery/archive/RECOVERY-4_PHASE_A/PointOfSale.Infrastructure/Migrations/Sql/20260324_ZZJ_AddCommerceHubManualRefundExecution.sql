BEGIN;

CREATE TABLE IF NOT EXISTS "CommerceHubRefundExecutions"
(
    "Id" uuid NOT NULL,
    "RefundId" uuid NOT NULL,
    "Method" integer NOT NULL,
    "AccountSnapshot" character varying(500) NOT NULL,
    "TransactionReference" character varying(200) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL,
    "ExecutedBy" uuid NOT NULL,
    "ExecutedAt" timestamp with time zone NOT NULL,
    "Notes" character varying(1000) NULL,
    "IsSuccessful" boolean NOT NULL DEFAULT FALSE,
    "FailureReason" character varying(2000) NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_CommerceHubRefundExecutions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CommerceHubRefundExecutions_CommerceHubRefundRequests_RefundId"
        FOREIGN KEY ("RefundId")
        REFERENCES "CommerceHubRefundRequests" ("Id")
        ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubRefundExecutions_RefundId"
    ON "CommerceHubRefundExecutions" ("RefundId")
    WHERE "IsDeleted" = FALSE;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefundExecutions_ExecutedAt"
    ON "CommerceHubRefundExecutions" ("ExecutedAt" DESC);

COMMIT;

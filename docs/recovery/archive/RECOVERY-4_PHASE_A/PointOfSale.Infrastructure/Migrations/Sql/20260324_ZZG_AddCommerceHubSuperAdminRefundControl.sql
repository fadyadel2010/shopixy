BEGIN;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "ReviewedBy" uuid NULL;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "ReviewedAtUtc" timestamp with time zone NULL;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "ApprovedAtUtc" timestamp with time zone NULL;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "RejectedAtUtc" timestamp with time zone NULL;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "ProcessingStartedAtUtc" timestamp with time zone NULL;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "CompletedAtUtc" timestamp with time zone NULL;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "FailedAtUtc" timestamp with time zone NULL;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "AdminNote" character varying(1000) NULL;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "FailureReason" character varying(2000) NULL;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "ExecutionAttempts" integer NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefundRequests_Status_CreatedAt"
    ON "CommerceHubRefundRequests" ("Status", "CreatedAt" DESC);

ALTER TABLE "CommerceHubRefunds"
    ADD COLUMN IF NOT EXISTS "RefundRequestId" uuid NULL;

ALTER TABLE "CommerceHubRefunds"
    ADD COLUMN IF NOT EXISTS "OrderItemId" uuid NULL;

ALTER TABLE "CommerceHubRefunds"
    ADD COLUMN IF NOT EXISTS "Quantity" integer NULL;

ALTER TABLE "CommerceHubRefunds"
    ADD COLUMN IF NOT EXISTS "IsPostPayoutAdjustment" boolean NOT NULL DEFAULT false;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubRefunds_CommerceHubRefundRequests_RefundRequestId'
    ) THEN
        ALTER TABLE "CommerceHubRefunds"
            ADD CONSTRAINT "FK_CommerceHubRefunds_CommerceHubRefundRequests_RefundRequestId"
            FOREIGN KEY ("RefundRequestId")
            REFERENCES "CommerceHubRefundRequests" ("Id")
            ON DELETE RESTRICT;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubRefunds_CommerceHubSubOrderItems_OrderItemId'
    ) THEN
        ALTER TABLE "CommerceHubRefunds"
            ADD CONSTRAINT "FK_CommerceHubRefunds_CommerceHubSubOrderItems_OrderItemId"
            FOREIGN KEY ("OrderItemId")
            REFERENCES "CommerceHubSubOrderItems" ("Id")
            ON DELETE RESTRICT;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubRefunds_RefundRequestId"
    ON "CommerceHubRefunds" ("RefundRequestId")
    WHERE "RefundRequestId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefunds_OrderItemId"
    ON "CommerceHubRefunds" ("OrderItemId")
    WHERE "OrderItemId" IS NOT NULL;

COMMIT;

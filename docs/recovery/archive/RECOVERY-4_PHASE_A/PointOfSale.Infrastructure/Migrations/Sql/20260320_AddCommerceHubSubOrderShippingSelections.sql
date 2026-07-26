BEGIN;

ALTER TABLE "CommerceHubSubOrders"
    ADD COLUMN IF NOT EXISTS "ShippingMethodId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "ShippingMethodName" character varying(200) NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubSubOrders_ShippingMethodId"
    ON "CommerceHubSubOrders" ("ShippingMethodId");

COMMIT;

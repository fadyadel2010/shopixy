-- ============================================================================
-- WhatsApp Message Templates - Database Migration
-- ============================================================================
-- Run this in PgAdmin4

BEGIN;

CREATE TABLE IF NOT EXISTS "whatsapp_message_templates" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Type" character varying(50) NOT NULL,
    "Language" character varying(10) NOT NULL,
    "Content" text NOT NULL,
    "IsActive" boolean NOT NULL,
    "IsDefault" boolean NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    -- TenantEntity fields
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    
    CONSTRAINT "PK_whatsapp_message_templates" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_whatsapp_message_templates_Tenants_TenantId" FOREIGN KEY ("TenantId") 
        REFERENCES "Tenants" ("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_whatsapp_message_templates_TenantId_Type_Language" 
    ON "whatsapp_message_templates" ("TenantId", "Type", "Language");

CREATE INDEX IF NOT EXISTS "IX_whatsapp_message_templates_TenantId" 
    ON "whatsapp_message_templates" ("TenantId");
    
-- Insert Default Templates (System Tenant)
-- Note: Replace '00000000-0000-0000-0000-000000000000' with your actual System Tenant ID if different
-- Or rely on the code fallback which handles missing DB templates.

COMMIT;

-- S52: Cleanup old cart owner unique index
-- The S46 migration created a new index "UX_CommerceHubCarts_TenantId_CommerceHubCustomerId_Active"
-- but did not drop the old index "UX_CommerceHubCarts_CommerceHubCustomerId_Active".
-- This migration removes the old index to ensure all environments converge to the same state.
-- Also removes the even older "IX_CommerceHubCarts_TenantId_OnlineCustomerId" if it exists.

-- Drop old cart owner unique indexes if they exist
-- The new index UX_CommerceHubCarts_TenantId_CommerceHubCustomerId_Active (created in S46) is the canonical one
DROP INDEX IF EXISTS "UX_CommerceHubCarts_CommerceHubCustomerId_Active";
DROP INDEX IF EXISTS "IX_CommerceHubCarts_TenantId_OnlineCustomerId";

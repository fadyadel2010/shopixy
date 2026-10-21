import { POS_PERMISSIONS } from '../auth/posPermissions'

// Permission-complete, backend-shaped actor for the complete inventory, not a guessed role grant.
export const navigationOwner = {
    id: '11111111-1111-4111-8111-111111111111',
    tenantId: '22222222-2222-4222-8222-222222222222',
    role: 'Owner',
    effectivePermissions: [...POS_PERMISSIONS],
    navigationCapabilities: [
        'commercehub.orders.view', 'commercehub.catalog.manage', 'commercehub.finance.view',
        'commercehub.payments.view', 'commercehub.storefront.manage',
    ],
    vendorLifecycleAuthority: {
        vendorId: '22222222-2222-4222-8222-222222222222', status: 'Active', tenantActive: true,
        marketplaceApproved: true, kycVerified: true, debtStatus: 'Clear', isDebtBlocked: false,
        blocksCatalog: false, blocksOrders: false, blocksStorefront: false, blocksAnalytics: false,
        blocksPayments: false, blocksPayouts: false, blocksLogin: false,
    },
}

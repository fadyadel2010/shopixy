export const POS_PERMISSIONS = new Set([
    'dashboard.view',
    'catalog.view',
    'catalog.manage',
    'catalog.delete',
    'catalog.publish',
    'inventory.view',
    'inventory.adjust',
    'inventory.transfer',
    'inventory.approve',
    'customers.view',
    'customers.manage',
    'customers.balance.manage',
    'suppliers.view',
    'suppliers.manage',
    'suppliers.balance.manage',
    'sales.view',
    'sales.operate',
    'sales.refund',
    'sales.delete',
    'purchases.view',
    'purchases.manage',
    'purchases.return',
    'finance.view',
    'finance.operate',
    'finance.admin',
    'finance.initialize',
    'reports.operational.view',
    'reports.financial.view',
    'storefront.orders.view',
    'storefront.orders.attachments.download',
    'storefront.orders.manage',
    'storefront.payments.view',
    'storefront.payments.manage',
    'storefront.refunds.manage',
    'storefront.promotions.manage',
    'storefront.analytics.view',
    'storefront.analytics.configure',
    'storefront.design.manage',
    'storefront.domains.manage',
    'storefront.auth.manage',
    'shipping.view',
    'shipping.manage',
    'shipping.fulfill',
    'affiliates.manage',
    'affiliates.payout.manage',
    'settings.view',
    'settings.manage',
    'audit.view',
    'audit.export',
    'users.view',
    'users.invite',
    'users.profile.manage',
    'users.role.manage',
    'users.status.manage',
    'users.password-reset.initiate',
    'users.session.revoke',
    'users.ownership.transfer',
])

export const deriveEffectivePosPermissions = (value) => {
    if (
        !Array.isArray(value)
        || value.some((permission) => typeof permission !== 'string' || !POS_PERMISSIONS.has(permission))
        || new Set(value).size !== value.length
    ) {
        return null
    }

    return [...value]
}

export const hasPosPermission = (user, permission) =>
    POS_PERMISSIONS.has(permission)
    && Array.isArray(user?.effectivePermissions)
    && user.effectivePermissions.includes(permission)

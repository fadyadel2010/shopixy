const STOREFRONT_ADMIN_ROLES = new Set(['Owner', 'Admin'])

export const canManageStorefrontSettings = (user) => {
    return STOREFRONT_ADMIN_ROLES.has(user?.role)
}

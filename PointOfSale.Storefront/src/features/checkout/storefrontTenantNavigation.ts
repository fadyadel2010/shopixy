/** Preserve the explicit development tenant authority across full-page navigation. */
export function withCurrentTenantQuery(path: string, currentSearch: string): string {
    const tenantId = new URLSearchParams(currentSearch).get('tenant')?.trim();
    if (!tenantId) return path;

    const target = new URL(path, 'http://storefront.local');
    target.searchParams.set('tenant', tenantId);
    return `${target.pathname}${target.search}${target.hash}`;
}

export function checkoutNavigationTarget(
    isAdmitted: boolean,
    currentSearch: string,
): string {
    const checkoutPath = withCurrentTenantQuery('/checkout', currentSearch);
    if (isAdmitted) return checkoutPath;

    const loginPath = withCurrentTenantQuery('/login', currentSearch);
    const separator = loginPath.includes('?') ? '&' : '?';
    return `${loginPath}${separator}return=${encodeURIComponent(checkoutPath)}`;
}

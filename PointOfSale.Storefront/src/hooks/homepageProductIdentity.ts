export function normalizeHomepageProductIds(productIds?: string[] | null): string[] | undefined {
    if (!Array.isArray(productIds)) {
        return undefined;
    }

    return Array.from(
        new Set(
            productIds
                .map((productId) => String(productId ?? '').trim())
                .filter(Boolean)
        )
    );
}

export function getHomepageProductIdsIdentity(productIds?: string[] | null): string {
    return JSON.stringify(normalizeHomepageProductIds(productIds) ?? null);
}

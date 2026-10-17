export const AFFILIATE_ATTRIBUTION_WARNING_CODE: 'AFFILIATE_ATTRIBUTION_UNAVAILABLE';
export function resolveCurrentVendorIdentityAuthority(user: unknown): { tenantId: string; userId: string } | null;
export function isCurrentVendorTenantAuthority(value: unknown, currentUser: unknown): boolean;
export function deriveCurrentVendorScopedDto<T>(value: T, currentUser: unknown): T | null;
export function deriveCurrentVendorScopedArray<T>(values: T[], currentUser: unknown): T[];
export interface AffiliateAttributionPresentation { tenantId: string; orderId: string; attributionId: string; affiliateAccountId: string; referralCode: string; promotionLinkId: string | null; source: string; attributedAtUtc: string }
export type PresentedOrder<T> = Omit<T, 'affiliateAttribution' | 'AffiliateAttribution' | 'affiliateAttributionWarning'> & {
    affiliateAttribution?: AffiliateAttributionPresentation | null; AffiliateAttribution?: unknown;
    affiliateAttributionWarning?: { code: 'AFFILIATE_ATTRIBUTION_UNAVAILABLE'; message: string };
};
export function requireCurrentVendorOrderVisibilityAuthority<T>(value: T, currentUser: unknown, expectedOrderId: string): PresentedOrder<T>;
export function requireCurrentVendorOrderVisibilityListAuthority<T extends { items: unknown[] }>(response: T, currentUser: unknown): Omit<T, 'items'> & { items: PresentedOrder<T['items'][number]>[] };
export function requireCurrentVendorOrderVisibilityListAuthority(response: unknown, currentUser: unknown): unknown;
export function requireBackendOrderSourceAuthority(order: unknown): { source: string; label: 'Storefront' | 'Marketplace'; tone: 'slate' | 'sky' };
export function requireBackendMarketplaceLineageAuthority(order: unknown): {
    tenantId: string; orderId: string; marketplaceOrderId: string; marketplaceName: string; marketplaceOrderNumber: string;
    marketplaceSubOrderNumber: string; externalCustomerId: string | null; vendorId: string; vendorName: string;
    marketplaceSourceType: string; marketplaceStatus: string; marketplacePaymentStatus: string; currency: string;
    subtotal: number; shipping: number; total: number; paymentReferenceId: string | null; paymentSnapshotJson: string | null;
    shippingMethodId: string | null; shippingMethodName: string | null; shippingSnapshotJson: string | null; commissionSnapshotJson: string | null;
};
export function requireBackendAffiliateAttributionAuthority(order: unknown): AffiliateAttributionPresentation | null;
export function admitBackendAffiliateAttributionPresentation<T>(order: T): PresentedOrder<T>;
export function deriveCurrentStoreIdentityPresentation<T>(value: T, currentUser: unknown): (Omit<T, 'storeName' | 'storefrontStatus' | 'themeId' | 'rowVersion'> & { storeName: string; storefrontStatus: string; themeId: string; rowVersion: string }) | null;

import type { PosUser } from '../store/authStore';
export const POS_VENDOR_SESSION_AUTHORITY_LOST_EVENT: 'pos-vendor-session-authority-lost';
export const POS_VENDOR_SESSION_LOGOUT_STORAGE_KEY: 'pos_vendor_session_logout_at';
export const COMMERCEHUB_VENDOR_LIFECYCLE_STATUSES: Set<string>;
export const COMMERCEHUB_VENDOR_NAVIGATION_CAPABILITIES: Set<string>;
export interface VendorLifecycleAuthority {
    vendorId: string; status: string; governanceStatus: string | null; debtStatus: string;
    tenantActive: boolean; marketplaceApproved: boolean; kycVerified: boolean; isDebtBlocked: boolean;
    blocksCatalog: boolean; blocksOrders: boolean; blocksStorefront: boolean; blocksAnalytics: boolean;
    blocksPayments: boolean; blocksPayouts: boolean; blocksLogin: boolean;
}
export interface PosVendorUser extends PosUser {
    id: string; tenantId: string; role: string; effectivePermissions: string[]; navigationCapabilities: string[];
    vendorLifecycleAuthority: VendorLifecycleAuthority;
}
export function deriveCommerceHubVendorLifecyclePresentation(value: unknown, tenantId: string): VendorLifecycleAuthority | null;
export function derivePosVendorSessionUserAuthority(value: unknown): PosVendorUser | null;
export function requirePosVendorSessionUserAuthority(value: unknown): PosVendorUser;
export function hasCommerceHubVendorNavigationCapability(user: unknown, capability: string): boolean;
export const canAccessCommerceHubVendorNavigationCapability: typeof hasCommerceHubVendorNavigationCapability;
export function deriveCommerceHubVendorEligibilityPresentation(user: unknown, evidence?: unknown): {
    tenantId: string; userId: string; lifecycleStatus: string; canManageCatalog: boolean; canViewOrders: boolean;
    canOperateOrders: boolean; canViewFinance: boolean; canViewPayments: boolean; canViewAnalytics: boolean;
    canManageStorefront: boolean; canManageCms: boolean; canPreviewStorefront: boolean; canSubmitPayment: boolean; isDebtBlocked: boolean;
} | null;
export function resolvePosRouteRedirect(value: unknown, fallback?: string): string;
export function isCommerceHubVendorSessionAuthorityEndpoint(url?: string): boolean;
export function dispatchPosVendorSessionAuthorityLost(): void;
export function broadcastPosVendorLogout(): void;

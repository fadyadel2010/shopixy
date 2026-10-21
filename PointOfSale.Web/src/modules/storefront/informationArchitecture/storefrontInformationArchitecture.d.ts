export interface StorefrontNavigationNode {
    key?: string;
    application?: string;
    path?: string;
    labelKey: string;
    fallbackLabel: string;
    icon?: string;
    screenIds?: string[];
    permission?: string;
    capability?: string;
    requiredRoles?: string[];
    initialExpansion?: string;
    children?: StorefrontNavigationNode[];
}
export interface StorefrontScreen {
    id: string;
    titleKey: string;
    fallbackTitle: string;
    breadcrumbs: Array<{ labelKey: string; fallbackLabel: string; path?: string | null }>;
}
export const OPERATIONS_NAVIGATION: StorefrontNavigationNode & { children: StorefrontNavigationNode[] };
export const STOREFRONT_NAVIGATION: StorefrontNavigationNode & { children: StorefrontNavigationNode[] };
export const STOREFRONT_PRODUCT_NAVIGATION: StorefrontNavigationNode & { children: StorefrontNavigationNode[] };
export const SCREENS: Record<string, StorefrontScreen>;
export const STOREFRONT_ROUTE_COMPATIBILITY: Array<{ screenId: string; canonical: string; legacy: string[] }>;
export const STOREFRONT_SIDEBAR_STATE_KEY: string;
export const STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY: string;
export function resolveStorefrontScreen(pathname: string, search?: string): StorefrontScreen | null;
export function isStorefrontNodeActive(node: StorefrontNavigationNode, activeScreenId?: string | null): boolean;

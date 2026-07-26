/**
 * Store Configuration Types
 * Matches backend StorefrontStoreConfigDtos.cs
 */

export interface StoreConfig {
    storeName: string;
    storeDescription?: string;
    themeId: string;
    storefrontTheme?: string;
    theme: ThemeConfig;
    behavior: BehaviorConfig;
    /** Storefront operational status: "live", "maintenance", "disabled" */
    storefrontStatus: 'live' | 'maintenance' | 'disabled';
    /** Optional message to display on maintenance page */
    maintenanceMessage?: string;
    updatedAtUtc: string;
    pages?: StorefrontPages;
    productTemplates?: Record<string, StorefrontProductTemplate>;
    /** Currency code (ISO 4217) for price display (e.g., "EGP", "USD", "SAR") */
    currency: string;
    /** Default checkout country code (ISO 3166-1 alpha-2) from tenant/storefront config */
    defaultCountryCode: string;
    /** 
     * Phase 6: Commerce AI Feature Flag
     * When true, the AI chat widget is enabled on the storefront.
     * Controlled exclusively by Admin in /ecommerce/commerce-ai.
     */
    commerceAiEnabled?: boolean;
    commerceAiWidgetStyle?: 'minimal' | 'floating' | 'sales' | 'compact';
    commerceAiWidgetPosition?: 'bottom-right' | 'bottom-left' | 'middle-right';
    commerceAiAutoGreetingEnabled?: boolean;
    commerceAiAutoGreetingMessage?: string;
    commerceAiPersonality?: 'friendly' | 'professional' | 'sales-focused';
}

export interface StorefrontPages {
    home: StorefrontPageConfig;
    product?: StorefrontPageConfig;
    category?: StorefrontPageConfig;
}

export interface StorefrontPageConfig {
    sections: StorefrontSection[];
    /** Page-level settings (optional) */
    settings?: Record<string, any>;
}

export interface StorefrontProductTemplate {
    templateId: string;
    name: string;
    sections: StorefrontSection[];
}

export interface StorefrontSection {
    id: string;
    type: string;
    enabled: boolean;
    order: number;
    settings: Record<string, any>;
}

export interface ThemeConfig {
    colors: ColorsConfig;
    font: FontConfig;
    ui: UiConfig;
    layout: LayoutConfig;
}

export interface ColorsConfig {
    primary: string;
    secondary: string;
    background: string;
    text: string;
    muted: string;
}

export interface FontConfig {
    family: string;
    familyAr?: string;  // Arabic font family (optional, defaults to Cairo)
    baseSize: string;
}

export interface UiConfig {
    borderRadius: string;
    buttonStyle: 'solid' | 'outline';
}

export interface LayoutConfig {
    productCardVariant: 'default' | 'compact' | 'detailed';
    headerVariant: 'standard' | 'centered' | 'minimal';
}

export interface BehaviorConfig {
    enableGuestCheckout: boolean;
    showOutOfStockProducts: boolean;
    enableSearch: boolean;
}

/**
 * Default store config for fallback
 * 
 * PHASE 10.4.X: Currency Authority Lockdown
 * - currency is intentionally empty to trigger sync errors
 * - real currency MUST come from StoreConfig API
 */
export const DEFAULT_STORE_CONFIG: StoreConfig = {
    storeName: 'Store',
    storeDescription: undefined,
    themeId: 'default',
    storefrontTheme: 'modern',
    theme: {
        colors: {
            primary: '#2563eb',
            secondary: '#0f172a',
            background: '#ffffff',
            text: '#020617',
            muted: '#64748b',
        },
        font: {
            family: 'Inter',
            baseSize: '16px',
        },
        ui: {
            borderRadius: '8px',
            buttonStyle: 'solid',
        },
        layout: {
            productCardVariant: 'default',
            headerVariant: 'standard',
        },
    },
    behavior: {
        enableGuestCheckout: true,
        showOutOfStockProducts: false,
        enableSearch: true,
    },
    storefrontStatus: 'live',
    maintenanceMessage: undefined,
    updatedAtUtc: new Date().toISOString(),
    // PHASE 10.4.X: Empty string triggers currency loading check
    // Real currency MUST come from StoreConfig API
    currency: '',
    defaultCountryCode: '',
    // PHASE 6: AI disabled by default - Admin must explicitly enable
    commerceAiEnabled: false,
    commerceAiWidgetStyle: 'minimal',
    commerceAiWidgetPosition: 'bottom-right',
    commerceAiAutoGreetingEnabled: false,
    commerceAiAutoGreetingMessage: '',
    commerceAiPersonality: 'friendly',
};

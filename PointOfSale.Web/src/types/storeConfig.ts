export interface StoreConfig {
  storeName: string
  storeDescription?: string
  themeId: string
  storefrontTheme?: string
  theme: ThemeConfig
  behavior: BehaviorConfig
  storefrontStatus: 'live' | 'maintenance' | 'disabled'
  maintenanceMessage?: string
  updatedAtUtc: string
  pages?: StorefrontPages
  productTemplates?: Record<string, StorefrontProductTemplate>
  currency: string
  defaultCountryCode: string
  commerceAiEnabled?: boolean
  commerceAiWidgetStyle?: 'minimal' | 'floating' | 'sales' | 'compact'
  commerceAiWidgetPosition?: 'bottom-right' | 'bottom-left' | 'middle-right'
  commerceAiAutoGreetingEnabled?: boolean
  commerceAiAutoGreetingMessage?: string
  commerceAiPersonality?: 'friendly' | 'professional' | 'sales-focused'
}

export interface StorefrontPages {
  home: StorefrontPageConfig
  product?: StorefrontPageConfig
  category?: StorefrontPageConfig
}

export interface StorefrontPageConfig {
  sections: StorefrontSection[]
  settings?: Record<string, unknown>
}

export interface StorefrontProductTemplate {
  templateId: string
  name: string
  sections: StorefrontSection[]
}

export interface StorefrontSection {
  id: string
  type: string
  enabled: boolean
  order: number
  settings: Record<string, unknown>
}

export interface ThemeConfig {
  colors: ColorsConfig
  font: FontConfig
  ui: UiConfig
  layout: LayoutConfig
}

export interface ColorsConfig {
  primary: string
  secondary: string
  background: string
  text: string
  muted: string
}

export interface FontConfig {
  family: string
  familyAr?: string
  baseSize: string
}

export interface UiConfig {
  borderRadius: string
  buttonStyle: 'solid' | 'outline'
}

export interface LayoutConfig {
  productCardVariant: 'default' | 'compact' | 'detailed'
  headerVariant: 'standard' | 'centered' | 'minimal'
}

export interface BehaviorConfig {
  enableGuestCheckout: boolean
  showOutOfStockProducts: boolean
  enableSearch: boolean
}

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
  currency: '',
  defaultCountryCode: '',
  commerceAiEnabled: false,
  commerceAiWidgetStyle: 'minimal',
  commerceAiWidgetPosition: 'bottom-right',
  commerceAiAutoGreetingEnabled: false,
  commerceAiAutoGreetingMessage: '',
  commerceAiPersonality: 'friendly',
}

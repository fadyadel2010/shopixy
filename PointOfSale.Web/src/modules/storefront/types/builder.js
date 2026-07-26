/**
 * Storefront Builder Types (JSDoc for IDE support)
 * Phase 9.8: POS Integration
 * 
 * @typedef {'home' | 'product' | 'category'} PageKey
 * 
 * @typedef {Object} Section
 * @property {string} id
 * @property {string} type
 * @property {number} order
 * @property {boolean} enabled
 * @property {Object} settings
 * 
 * @typedef {'layout' | 'products' | 'content' | 'marketing' | 'social'} SectionCategory
 * 
 * @typedef {Object} SectionDefinition
 * @property {string} type
 * @property {string} displayName
 * @property {string} description
 * @property {React.ReactNode} icon
 * @property {Object} defaultSettings
 * @property {Array<Object>} [settingsSchema]
 * @property {React.ComponentType} editor
 * @property {React.ComponentType} rendererPreview
 * @property {SectionCategory} category
 * 
 * @typedef {Object} VersionSnapshot
 * @property {number} version
 * @property {string} themePresetKey
 * @property {string} themeLiveOverridesJson
 * @property {string} customizationJson
 * 
 * @typedef {Object} VersionHistoryEntry
 * @property {number} version
 * @property {string} publishedAt
 * @property {string} publishedBy
 * @property {string | null | undefined} [publishedByUserId]
 * @property {string} changeType
 * @property {string | null | undefined} [changeSummary]
 * @property {boolean | undefined} [isCurrent]
 */

export const PAGE_KEYS = {
    HOME: 'home',
    PRODUCT: 'product',
    CATEGORY: 'category',
}

export const SECTION_CATEGORIES = {
    LAYOUT: 'layout',
    PRODUCTS: 'products',
    CONTENT: 'content',
    MARKETING: 'marketing',
    SOCIAL: 'social',
}

export const categoryDisplayNames = {
    layout: { en: 'Layout', ar: 'التخطيط' },
    products: { en: 'Products', ar: 'المنتجات' },
    content: { en: 'Content', ar: 'المحتوى' },
    marketing: { en: 'Marketing', ar: 'التسويق' },
    social: { en: 'Social Proof', ar: 'الإثبات الاجتماعي' },
}

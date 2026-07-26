/**
 * Demo Sections Configuration
 * Phase 9.8: POS Integration
 * 
 * Default sections for new tenants to demonstrate the builder
 */

/**
 * Default home page sections
 */
export const defaultHomeSections = [
    {
        id: 'demo_hero_1',
        type: 'hero-banner',
        order: 0,
        enabled: true,
        settings: {
            title: 'Welcome to Your Store',
            subtitle: 'Discover amazing products at great prices. Shop our curated collection today.',
            ctaText: 'Shop Now',
            ctaLink: '/products',
            backgroundImage: '',
            backgroundColor: '#1e40af',
            overlayOpacity: 45,
            textColor: '#ffffff',
            alignment: 'center',
            height: 'large',
        },
    },
    {
        id: 'demo_products_1',
        type: 'featured-products',
        order: 1,
        enabled: true,
        settings: {
            title: 'Featured Products',
            productIds: [],
            limit: 8,
            showPrice: true,
            showRating: true,
            displayStyle: 'carousel',
            showBadge: true,
            badgeText: 'Featured',
        },
    },
    {
        id: 'demo_categories_1',
        type: 'category-grid',
        order: 2,
        enabled: true,
        settings: {
            title: 'Shop by Category',
            columns: 3,
            categoryIds: [],
            showProductCount: true,
            cardStyle: 'overlay',
        },
    },
    {
        id: 'demo_newsletter_1',
        type: 'newsletter-signup',
        order: 3,
        enabled: true,
        settings: {
            heading: 'Stay Updated',
            subheading: 'Subscribe to our newsletter for exclusive offers and updates',
            buttonText: 'Subscribe',
            backgroundColor: '#0f172a',
            textColor: '#ffffff',
            showName: false,
        },
    },
]

/**
 * Generate unique section ID
 */
const generateId = () => `section_${Date.now()}_${Math.random().toString(36).substring(2, 9)}`

/**
 * Get default sections for a page with unique IDs
 */
export function getDefaultSections(pageKey) {
    switch (pageKey) {
        case 'home':
            return defaultHomeSections.map((s, i) => ({
                ...s,
                id: generateId(),
            }))
        case 'product':
            return [{
                id: generateId(),
                type: 'product-grid',
                order: 0,
                enabled: true,
                settings: {
                    title: 'You May Also Like',
                    productSource: 'featured',
                    categoryId: null,
                    productIds: [],
                    columns: 4,
                    rows: 1,
                    showPrice: true,
                    showRating: true,
                    showAddToCart: true,
                },
            }]
        case 'category':
            return [
                {
                    id: generateId(),
                    type: 'category-grid',
                    order: 0,
                    enabled: true,
                    settings: {
                        title: 'Shop by Category',
                        columns: 3,
                        categoryIds: [],
                        showProductCount: true,
                        cardStyle: 'overlay',
                    },
                },
                {
                    id: generateId(),
                    type: 'subcategory-grid',
                    order: 1,
                    enabled: true,
                    settings: {
                        title: 'Subcategories',
                        columns: 3,
                        showProductCount: true,
                        cardStyle: 'card',
                    },
                },
            ]
        case 'product-template':
            return [
                {
                    id: generateId(),
                    type: 'product-hero',
                    order: 0,
                    enabled: true,
                    settings: {
                        showDescription: true,
                        showVariantSelector: true,
                        showAddToCart: true,
                        showSku: true,
                        showStockStatus: true,
                    },
                },
                {
                    id: generateId(),
                    type: 'product-specs',
                    order: 1,
                    enabled: true,
                    settings: {
                        title: 'Product Details',
                        useDescription: true,
                        items: [],
                    },
                },
                {
                    id: generateId(),
                    type: 'related-products',
                    order: 2,
                    enabled: true,
                    settings: {
                        title: 'You May Also Like',
                        limit: 4,
                        showPrice: true,
                        showRating: true,
                    },
                },
            ]
        default:
            return []
    }
}

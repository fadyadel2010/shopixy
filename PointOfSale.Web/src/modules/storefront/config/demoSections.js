/**
 * Demo Sections Configuration
 * Phase 9.8: POS Integration
 * 
 * Default sections for new tenants to demonstrate the builder
 */

import { materializeContractSettings } from '../builder/sectionContractRegistry'

function createDemoSection(id, type, order, settings = {}) {
    return {
        id,
        type,
        order,
        enabled: true,
        settings: materializeContractSettings(type, settings),
    }
}

/**
 * Default home page sections
 */
export const defaultHomeSections = [
    createDemoSection('demo_hero_1', 'hero-banner', 0, {
        title: 'Welcome to Your Store',
        subtitle: 'Discover amazing products at great prices. Shop our curated collection today.',
        height: 'large',
    }),
    createDemoSection('demo_products_1', 'featured-products', 1),
    createDemoSection('demo_categories_1', 'category-grid', 2),
    createDemoSection('demo_newsletter_1', 'newsletter-signup', 3, {
        subheading: 'Subscribe to our newsletter for exclusive offers and updates',
    }),
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
        case 'product-template':
            return [
                createDemoSection(generateId(), 'product-specs', 0),
                createDemoSection(generateId(), 'related-products', 1, {
                    title: 'You May Also Like',
                }),
            ]
        default:
            return []
    }
}

import { describe, expect, it } from 'vitest'
import sectionAuthority from '@theme-authority/shopixy-theme-authority.json'
import { sectionRegistry } from './sectionRegistry'
import { filterSectionsForPage, isSectionAvailableForPage } from './pageSectionAvailability'
import { sectionContracts, validateSectionsAgainstContract } from './sectionContractRegistry'
import { getDefaultSections } from '../config/demoSections'
import {
    composeProductTemplateSections,
    getBuiltinProductTemplates,
    getSupplementalProductTemplateSections,
} from '@web/storefront/products/ProductTemplateRegistry'

const requireSection = (type: string) => {
    const definition = sectionRegistry.get(type)
    if (!definition) throw new Error(`Missing section definition: ${type}`)
    return definition
}

describe('builder/runtime section contract authority', () => {
    it('derives builder defaults and setting schemas from the canonical section contract', () => {
        expect(sectionContracts).toEqual(sectionAuthority.sectionContracts)

        for (const contract of sectionAuthority.sectionContracts) {
            const builderDefinition = requireSection(contract.type)
            const contractKeys = contract.fields.map((field) => field.key)

            expect(builderDefinition).toBeDefined()
            expect(builderDefinition.defaultSettings).toEqual(contract.defaultSettings)
            expect(builderDefinition.settingsSchema.map((field) => field.key)).toEqual(contractKeys)
        }
    })

    it('keeps display style canonical from builder field through serialized JSON name', () => {
        const featuredProducts = requireSection('featured-products')
        const displayStyleField = featuredProducts.settingsSchema.find((field) => field.key === 'displayStyle')

        expect(featuredProducts.defaultSettings.displayStyle).toBe('carousel')
        expect(displayStyleField).toEqual(expect.objectContaining({
            key: 'displayStyle',
            type: 'select',
            defaultValue: 'carousel',
        }))
        expect(displayStyleField?.options?.map((option) => option.value)).toEqual(['carousel', 'grid'])
    })

    it('does not expose legacy productSource as a builder/runtime contract property', () => {
        const serializedContract = JSON.stringify(sectionAuthority.sectionContracts)
        const productGrid = requireSection('product-grid')

        expect(serializedContract).not.toContain('productSource')
        expect(productGrid.defaultSettings).toHaveProperty('source', 'all')
        expect(productGrid.defaultSettings).not.toHaveProperty('productSource')
        expect(productGrid.settingsSchema.map((field) => field.key)).toContain('source')
    })

    it('keeps Product Details capabilities out of authored settings', () => {
        const productHero = requireSection('product-hero')
        const productSpecs = requireSection('product-specs')
        const relatedProducts = requireSection('related-products')

        expect(productHero.defaultSettings).toEqual({ layoutVariant: 'default' })
        expect(productSpecs.defaultSettings).toEqual({ layoutVariant: 'default', title: 'Product Details' })
        expect(relatedProducts.defaultSettings).toEqual({ layoutVariant: 'default', title: 'Recommended Products', limit: 4 })
        expect(JSON.stringify(sectionAuthority.sectionContracts)).not.toMatch(/showPrice|showRating|showAddToCart|showMediaGallery|showSku|reviews/)
    })

    it('certifies section creation paths produce complete canonical settings', () => {
        for (const pageKey of ['home', 'product-template']) {
            expect(() => validateSectionsAgainstContract(getDefaultSections(pageKey))).not.toThrow()
        }

        for (const template of getBuiltinProductTemplates()) {
            expect(() => validateSectionsAgainstContract(template.sections)).not.toThrow()
        }
    })

    it('fails builder save validation before API submission for contract drift', () => {
        const section = {
            id: 'section_invalid_contract',
            type: 'featured-products',
            enabled: true,
            order: 0,
            settings: {
                ...requireSection('featured-products').defaultSettings,
                productSource: 'all',
            },
        }

        expect(() => validateSectionsAgainstContract([section])).toThrow(/unsupported setting "productSource"/)
    })

    it('keeps Runtime Product Hero out of every palette and supplemental Product Details sections out of home', () => {
        const productContextTypes = ['product-hero', 'product-gallery', 'product-specs', 'product-video', 'related-products']

        for (const sectionType of productContextTypes) {
            expect(isSectionAvailableForPage(sectionType, 'home')).toBe(false)
            expect(isSectionAvailableForPage(sectionType, 'product-template')).toBe(sectionType !== 'product-hero')
        }

        const mixedHomeSections = [
            { id: 'hero', type: 'hero-banner', order: 0 },
            { id: 'video', type: 'product-video', order: 1 },
            { id: 'products', type: 'product-grid', order: 2 },
        ]

        expect(filterSectionsForPage(mixedHomeSections, 'home')).toEqual([
            { id: 'hero', type: 'hero-banner', order: 0 },
            { id: 'products', type: 'product-grid', order: 1 },
        ])
    })

    it('accepts catalog GUID identities generated by the backend, including non-RFC UUID bits', () => {
        const settings = { ...requireSection('featured-products').defaultSettings,
            source: 'curated', productIds: ['cc20abdf-0a4f-0278-3c59-c4f10d17bc96'] }
        expect(() => validateSectionsAgainstContract([{ type: 'featured-products', settings }])).not.toThrow()
        expect(() => validateSectionsAgainstContract([{ type: 'featured-products', settings: {
            ...settings, productIds: ['00000000-0000-0000-0000-000000000000'],
        } }])).toThrow(/valid GUID/)
    })

    it('serializes exactly one mandatory Runtime hero before supplemental sections', () => {
        const supplemental = getSupplementalProductTemplateSections(getBuiltinProductTemplates()[0].sections)
        const composed = composeProductTemplateSections(supplemental, 'default')

        expect(composed[0]).toMatchObject({ type: 'product-hero', enabled: true, order: 0 })
        expect(composed.filter((section) => section.type === 'product-hero')).toHaveLength(1)
        expect(supplemental.some((section) => section.type === 'product-hero')).toBe(false)
    })

    it('rejects malformed testimonial objects and unsafe authored URLs', () => {
        const testimonials = requireSection('testimonials').defaultSettings
        expect(() => validateSectionsAgainstContract([{
            id: 'section_bad_testimonial', type: 'testimonials', enabled: true, order: 0,
            settings: { ...testimonials, testimonials: [{ name: 'A', avatar: 'javascript:alert(1)', text: 'Quote' }] },
        }])).toThrow(/safe URL/)
        expect(() => validateSectionsAgainstContract([{
            id: 'section_bad_testimonial_shape', type: 'testimonials', enabled: true, order: 0,
            settings: { ...testimonials, testimonials: [{ name: 'A', avatar: '', text: 'Quote', rating: 5 }] },
        }])).toThrow(/unsupported property "rating"/)

        expect(() => validateSectionsAgainstContract([{
            id: 'section_bad_color', type: 'hero-banner', enabled: true, order: 0,
            settings: { ...requireSection('hero-banner').defaultSettings, backgroundColor: 'red' },
        }])).toThrow(/hex color/)
        expect(() => validateSectionsAgainstContract([{
            id: 'section_bad_video', type: 'video-block', enabled: true, order: 0,
            settings: { ...requireSection('video-block').defaultSettings, videoUrl: 'https://example.com/not encoded.mp4' },
        }])).toThrow(/safe URL/)
    })
})

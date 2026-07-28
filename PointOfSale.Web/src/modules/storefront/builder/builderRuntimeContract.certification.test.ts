// @ts-nocheck
import { describe, expect, it } from 'vitest'
import sectionAuthority from '@theme-authority/shopixy-theme-authority.json'
import { sectionRegistry } from './sectionRegistry'
import { sectionContracts, validateSectionsAgainstContract } from './sectionContractRegistry'
import { getDefaultSections } from '../config/demoSections'
import { getBuiltinProductTemplates } from '@/storefront/products/ProductTemplateRegistry'

describe('builder/runtime section contract authority', () => {
    it('derives builder defaults and setting schemas from the canonical section contract', () => {
        expect(sectionContracts).toEqual(sectionAuthority.sectionContracts)

        for (const contract of sectionAuthority.sectionContracts) {
            const builderDefinition = sectionRegistry.get(contract.type)
            const contractKeys = contract.fields.map((field) => field.key)

            expect(builderDefinition).toBeDefined()
            expect(builderDefinition.defaultSettings).toEqual(contract.defaultSettings)
            expect(builderDefinition.settingsSchema.map((field) => field.key)).toEqual(contractKeys)
        }
    })

    it('keeps display style canonical from builder field through serialized JSON name', () => {
        const featuredProducts = sectionRegistry.get('featured-products')
        const displayStyleField = featuredProducts.settingsSchema.find((field) => field.key === 'displayStyle')

        expect(featuredProducts.defaultSettings.displayStyle).toBe('carousel')
        expect(displayStyleField).toEqual(expect.objectContaining({
            key: 'displayStyle',
            type: 'select',
            defaultValue: 'carousel',
        }))
        expect(displayStyleField.options.map((option) => option.value)).toEqual(['carousel', 'grid'])
    })

    it('does not expose legacy productSource as a builder/runtime contract property', () => {
        const serializedContract = JSON.stringify(sectionAuthority.sectionContracts)
        const productGrid = sectionRegistry.get('product-grid')

        expect(serializedContract).not.toContain('productSource')
        expect(productGrid.defaultSettings).toHaveProperty('source', 'all')
        expect(productGrid.defaultSettings).not.toHaveProperty('productSource')
        expect(productGrid.settingsSchema.map((field) => field.key)).toContain('source')
    })

    it('promotes runtime-consumed product template settings into the canonical contract', () => {
        const productHero = sectionRegistry.get('product-hero')
        const productSpecs = sectionRegistry.get('product-specs')
        const relatedProducts = sectionRegistry.get('related-products')

        expect(productHero.defaultSettings).toHaveProperty('layoutVariant', 'default')
        expect(productHero.defaultSettings).toHaveProperty('showMediaGallery', true)
        expect(productSpecs.defaultSettings).toHaveProperty('layoutVariant', 'default')
        expect(relatedProducts.defaultSettings).toHaveProperty('layoutVariant', 'default')
    })

    it('certifies section creation paths produce complete canonical settings', () => {
        for (const pageKey of ['home', 'product', 'category', 'product-template']) {
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
                ...sectionRegistry.get('featured-products').defaultSettings,
                productSource: 'all',
            },
        }

        expect(() => validateSectionsAgainstContract([section])).toThrow(/unsupported setting "productSource"/)
    })
})

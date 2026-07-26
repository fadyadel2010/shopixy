import { describe, expect, it } from 'vitest'

import type { HomepageSectionSchema } from './homepageService'
import {
    buildSaveHomepageDraftPayload,
    canAutoPersistReorder,
    createBuilderSection,
    validateBuilderSections,
} from './homepageBuilderUtils'

const schemas: HomepageSectionSchema[] = [
    {
        type: 'products',
        displayName: 'Products',
        description: 'Automatic or curated products.',
        version: 1,
        fields: [
            { key: 'title', label: 'Title', kind: 'text', required: true, options: [] },
            { key: 'subtitle', label: 'Subtitle', kind: 'textarea', required: false, options: [] },
            {
                key: 'source',
                label: 'Source',
                kind: 'select',
                required: true,
                options: ['all', 'random', 'curated'],
            },
            { key: 'maxItems', label: 'Items to Show', kind: 'number', required: false, options: [] },
            { key: 'productIds', label: 'Product IDs', kind: 'guid-list', required: false, options: [] },
            {
                key: 'layout',
                label: 'Layout',
                kind: 'select',
                required: true,
                options: ['grid', 'carousel'],
            },
        ],
    },
    {
        type: 'categories',
        displayName: 'Categories',
        description: 'Automatic or curated categories.',
        version: 1,
        fields: [
            { key: 'title', label: 'Title', kind: 'text', required: true, options: [] },
            { key: 'subtitle', label: 'Subtitle', kind: 'textarea', required: false, options: [] },
            {
                key: 'source',
                label: 'Source',
                kind: 'select',
                required: true,
                options: ['all', 'random', 'curated'],
            },
            { key: 'maxItems', label: 'Items to Show', kind: 'number', required: false, options: [] },
            { key: 'categoryIds', label: 'Category IDs', kind: 'guid-list', required: false, options: [] },
        ],
    },
]

describe('homepageBuilderUtils', () => {
    it('buildSaveHomepageDraftPayload serializes schema data and keeps section order normalized', () => {
        const draftSection = createBuilderSection(schemas[0], 4, null)

        draftSection.persistedId = 'section-1'
        draftSection.data.title = '  Featured Picks  '
        draftSection.data.subtitle = '  Fresh arrivals '
        draftSection.data.source = 'curated'
        draftSection.data.maxItems = '6'
        draftSection.data.productIds = [
            '6d7c9f7d-4f47-4e1a-b1f3-246b1ddc6f13',
            '2bcb5122-7fd1-4a1f-8f50-7ec2b04e10aa',
        ]
        draftSection.data.layout = 'carousel'

        const payload = buildSaveHomepageDraftPayload([draftSection], schemas, null)

        expect(payload.tenantId).toBeNull()
        expect(payload.sections).toEqual([
            {
                id: 'section-1',
                type: 'products',
                order: 0,
                schemaVersion: 1,
                isActive: true,
                data: {
                    title: 'Featured Picks',
                    subtitle: 'Fresh arrivals',
                    source: 'curated',
                    maxItems: '6',
                    productIds: [
                        '6d7c9f7d-4f47-4e1a-b1f3-246b1ddc6f13',
                        '2bcb5122-7fd1-4a1f-8f50-7ec2b04e10aa',
                    ],
                    layout: 'carousel',
                },
            },
        ])
    })

    it('validateBuilderSections rejects missing required values and invalid GUID lists', () => {
        const draftSection = createBuilderSection(schemas[0], 0, null)
        draftSection.data.title = ''
        draftSection.data.source = 'curated'
        draftSection.data.layout = 'grid'
        draftSection.data.productIds = ['not-a-guid']

        const validationMap = validateBuilderSections([draftSection], schemas)

        expect(validationMap[draftSection.clientId]).toEqual([
            { fieldKey: 'title', message: 'Title is required.' },
            {
                fieldKey: 'productIds',
                message: 'Product IDs must contain valid GUID values.',
            },
        ])
    })

    it('validateBuilderSections requires curated sections to include identifiers', () => {
        const draftSection = createBuilderSection(schemas[0], 0, null)
        draftSection.data.title = 'Homepage shelf'
        draftSection.data.source = 'curated'
        draftSection.data.layout = 'grid'

        const validationMap = validateBuilderSections([draftSection], schemas)

        expect(validationMap[draftSection.clientId]).toEqual([
            {
                fieldKey: 'productIds',
                message: 'Curated products require at least one product identifier.',
            },
        ])
    })

    it('canAutoPersistReorder requires only persisted sections and no pending deletions', () => {
        const persisted = createBuilderSection(schemas[0], 0, null)
        persisted.persistedId = 'section-1'

        const unsaved = createBuilderSection(schemas[0], 1, null)

        expect(canAutoPersistReorder([persisted], [])).toBe(true)
        expect(canAutoPersistReorder([persisted, unsaved], [])).toBe(false)
        expect(canAutoPersistReorder([persisted], ['section-2'])).toBe(false)
    })

    it('validateBuilderSections allows optional GUID lists to stay empty for starter template sections', () => {
        const draftSection = createBuilderSection(schemas[1], 0, null)
        draftSection.data.title = 'Shop by category'
        draftSection.data.source = 'all'
        draftSection.data.maxItems = '6'

        const validationMap = validateBuilderSections([draftSection], schemas)

        expect(validationMap[draftSection.clientId]).toBeUndefined()
    })
})

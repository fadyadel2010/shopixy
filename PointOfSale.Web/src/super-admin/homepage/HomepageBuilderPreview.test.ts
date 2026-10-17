import React from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import HomepageBuilderPreview from './HomepageBuilderPreview'
import { DEFAULT_STORE_CONFIG } from '@/types/storeConfig'
import type { HomepageBuilderSection } from './homepageBuilderUtils'
const sectionRendererSpy = vi.hoisted(() => vi.fn(() => null))

vi.mock('@/components/sections/SectionRenderer', () => {
    return {
        __esModule: true,
        SectionRenderer: sectionRendererSpy,
        default: sectionRendererSpy,
    }
})

vi.mock('@/storefront/layout/StorefrontLayout', () => ({
    __esModule: true,
    StorefrontLayout: ({ children }: { children: React.ReactNode }) => React.createElement(React.Fragment, null, children),
    default: ({ children }: { children: React.ReactNode }) => React.createElement(React.Fragment, null, children),
}))

vi.mock('@web/modules/storefront/components/builder/BuilderThemeProvider', () => ({
    __esModule: true,
    default: ({ children }: { children: React.ReactNode }) => React.createElement(React.Fragment, null, children),
}))

describe('HomepageBuilderPreview', () => {
    beforeEach(() => {
        sectionRendererSpy.mockClear()
    })

    it('uses the shared storefront SectionRenderer with normalized draft sections', () => {
        const sections: HomepageBuilderSection[] = [
            {
                clientId: 'draft-hero',
                persistedId: null,
                type: 'hero',
                order: 0,
                isActive: true,
                schemaVersion: 1,
                tenantId: 'tenant-1',
                data: {
                    title: 'Welcome to Shopixy',
                    subtitle: 'Discover amazing products',
                    imageUrl: 'https://cdn.example.com/hero.jpg',
                    ctaText: 'Shop Now',
                    ctaLink: '/products',
                },
            },
            {
                clientId: 'draft-banner',
                persistedId: null,
                type: 'banner',
                order: 1,
                isActive: false,
                schemaVersion: 1,
                tenantId: 'tenant-1',
                data: {
                    title: 'Launch your next best seller',
                    subtitle: 'Promote campaigns safely from draft.',
                    imageUrl: 'https://cdn.example.com/banner.jpg',
                    ctaText: 'Explore',
                    ctaLink: '/collections',
                },
            },
        ]

        renderToStaticMarkup(
            React.createElement(HomepageBuilderPreview, {
                sections,
                selectedSectionClientId: 'draft-hero',
                onSelectSection: () => {},
                storeConfig: DEFAULT_STORE_CONFIG,
                previewTenantId: 'tenant-1',
                previewTenantName: 'Tenant One',
            }),
        )

        expect(sectionRendererSpy).toHaveBeenCalledTimes(1)
        expect(sectionRendererSpy).toHaveBeenCalledWith(
            expect.objectContaining({
                sections: [
                    {
                        id: 'draft-hero',
                        type: 'hero',
                        order: 0,
                        enabled: true,
                        data: sections[0].data,
                    },
                    {
                        id: 'draft-banner',
                        type: 'banner',
                        order: 1,
                        enabled: false,
                        data: sections[1].data,
                    },
                ],
                selectedSectionId: 'draft-hero',
                onSectionSelect: expect.any(Function),
            }),
            expect.anything(),
        )
    })
})

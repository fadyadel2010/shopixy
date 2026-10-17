import { render, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

type FooterProps = { loadCmsPages: () => Promise<Array<{ slug: string; title: string }>>; scope: string; note: string }
const captures = vi.hoisted(() => ({ props: null as FooterProps | null }))
const cmsService = vi.hoisted(() => ({ getNavigationTargets: vi.fn() }))

vi.mock('../../../../shared/content/FooterConfigScreen', () => ({
    FooterConfigScreen: (props: FooterProps) => {
        captures.props = props
        return <div>Footer settings</div>
    },
}))
vi.mock('../services', () => ({ cmsService }))

import { StorefrontFooterSettings } from './StorefrontFooterSettings'

describe('StorefrontFooterSettings', () => {
    it('offers only published CMS targets to the Footer editor', async () => {
        cmsService.getNavigationTargets.mockResolvedValueOnce({ cmsPages: [
            { resolvedPath: '/published', label: 'Published', state: 'published' },
            { resolvedPath: '/draft-only', label: 'Draft only', state: 'draft' },
        ] })
        render(<StorefrontFooterSettings />)

        await waitFor(() => expect(captures.props).not.toBeNull())
        await expect(captures.props!.loadCmsPages()).resolves.toEqual([
            { slug: 'published', title: 'Published' },
        ])
        expect(captures.props!.scope).toBe('Tenant')
        expect(captures.props!.note).toMatch(/immediately/i)
    })

    it('uses the published route when a pending CMS draft has a different slug', async () => {
        cmsService.getNavigationTargets.mockResolvedValueOnce({ cmsPages: [
            { resolvedPath: '/original-live-slug', label: 'Renamed draft title', state: 'published' },
        ] })
        render(<StorefrontFooterSettings />)
        await expect(captures.props!.loadCmsPages()).resolves.toEqual([
            { slug: 'original-live-slug', title: 'Renamed draft title' },
        ])
    })
})

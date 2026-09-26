import { render, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

const captures = vi.hoisted(() => ({ props: null as any }))
const cmsService = vi.hoisted(() => ({ listPages: vi.fn() }))

vi.mock('../../../../shared/content/FooterConfigScreen', () => ({
    FooterConfigScreen: (props: any) => {
        captures.props = props
        return <div>Footer settings</div>
    },
}))
vi.mock('../services', () => ({ cmsService }))

import { StorefrontFooterSettings } from './StorefrontFooterSettings'

describe('StorefrontFooterSettings', () => {
    it('offers only published CMS targets to the Footer editor', async () => {
        cmsService.listPages.mockResolvedValueOnce([
            { slug: 'published', title: 'Published', hasPublished: true },
            { slug: 'draft-only', title: 'Draft only', hasPublished: false },
        ])
        render(<StorefrontFooterSettings />)

        await waitFor(() => expect(captures.props).not.toBeNull())
        await expect(captures.props.loadCmsPages()).resolves.toEqual([
            { slug: 'published', title: 'Published' },
        ])
        expect(captures.props.scope).toBe('Tenant')
        expect(captures.props.note).toMatch(/immediately/i)
    })
})

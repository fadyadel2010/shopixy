import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const navigate = vi.hoisted(() => vi.fn())
const cmsService = vi.hoisted(() => ({
    createPreviewLink: vi.fn(),
    deletePage: vi.fn(),
    listPages: vi.fn(),
    publishPage: vi.fn(),
    seedDefaultPages: vi.fn(),
}))

vi.mock('react-router-dom', () => ({ useNavigate: () => navigate }))
vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        i18n: { language: 'en' },
        t: (key: string, options?: { defaultValue?: string }) => ({
            'storefront.homepageEditor.keepEditing': 'Keep editing',
            'storefront.cms.pageList.title': 'Pages',
            'storefront.cms.pageList.edit': 'Edit',
            'storefront.cms.pageList.delete': 'Delete',
            'storefront.cms.pageList.deleteConfirmTitle': 'Delete page?',
            'storefront.cms.pageList.deletePage': 'Delete page',
        }[key] || options?.defaultValue || key),
    }),
}))
vi.mock('../services', () => ({ cmsService }))
vi.mock('../../../../store/authStore', () => ({
    useAuthStore: (selector: (state: unknown) => unknown) => selector({
        user: { tenantId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' },
    }),
}))

import { CmsPagesList } from './CmsPagesList'

const listPage = {
    id: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
    tenantId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    slug: 'about',
    title: 'About',
    contentType: 'RichText',
    status: 'Draft',
    hasDraft: true,
    hasPublished: false,
    draftVersion: 3,
    publishedVersion: null,
    updatedAtUtc: '2026-08-03T00:00:00.000Z',
}

describe('CmsPagesList authoring boundaries', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        vi.spyOn(console, 'error').mockImplementation(() => {})
        cmsService.listPages.mockResolvedValue([listPage])
    })

    afterEach(() => {
        vi.restoreAllMocks()
    })

    it('keeps preview and publish in the Page editor', async () => {
        render(<CmsPagesList />)
        await screen.findByText('About')
        expect(screen.queryByTitle('Publish')).not.toBeInTheDocument()
        expect(screen.queryByTitle('Preview')).not.toBeInTheDocument()
        fireEvent.click(screen.getByTitle('Edit'))
        expect(navigate).toHaveBeenCalledWith(`/storefront/content/pages/${listPage.id}`)
    })

    it('uses the canonical danger confirmation before deleting a Page', async () => {
        cmsService.deletePage.mockResolvedValue(undefined)
        render(<CmsPagesList />)
        await screen.findByText('About')
        fireEvent.click(screen.getByTitle('Delete'))
        expect(screen.getByRole('dialog', { name: 'Delete page?' })).toBeInTheDocument()
        fireEvent.click(screen.getByRole('button', { name: 'Delete page' }))
        expect(cmsService.deletePage).toHaveBeenCalledWith(listPage.id, 3)
    })

    it('distinguishes a committed delete from failed readback without announcing full success', async () => {
        cmsService.deletePage.mockResolvedValue(undefined)
        cmsService.listPages.mockResolvedValueOnce([listPage]).mockRejectedValueOnce(new Error('Readback unavailable'))
        render(<CmsPagesList />)
        await screen.findByText('About')
        fireEvent.click(screen.getByTitle('Delete'))
        fireEvent.click(screen.getByRole('button', { name: 'Delete page' }))
        await screen.findByText(/The change was saved, but the page list could not be reloaded/)
        expect(screen.queryByText('storefront.cms.pageList.deleted')).not.toBeInTheDocument()
        expect(cmsService.deletePage).toHaveBeenCalledTimes(1)
    })
})

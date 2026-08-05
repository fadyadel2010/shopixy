import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const navigate = vi.hoisted(() => vi.fn())
const cmsService = vi.hoisted(() => ({
    createPage: vi.fn(),
    createPreviewLink: vi.fn(),
    getPage: vi.fn(),
    getVersionHistory: vi.fn(),
    publishPage: vi.fn(),
    rollbackPage: vi.fn(),
    updatePage: vi.fn(),
}))
const extractCmsConflict = vi.hoisted(() => vi.fn((error: any) => {
    const response = error?.response
    if (!response || response.status !== 409 || response.data?.code !== 'CONCURRENCY_CONFLICT') {
        return null
    }

    return {
        code: response.data.code,
        message: response.data.message || 'Conflict',
        latestVersion: response.data.latestVersion ?? response.data.LatestVersion ?? null,
    }
}))

vi.mock('react-router-dom', () => ({
    useNavigate: () => navigate,
    useParams: () => ({ pageId: 'page-1' }),
}))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ i18n: { language: 'en' } }) }))
vi.mock('../services', () => ({ cmsService, extractCmsConflict }))
vi.mock('../cmsPageIdentityAuthority', () => ({
    deriveCurrentCmsPageIdentityPresentation: (value: unknown) => value,
    deriveCurrentCmsPageVersionHistoryPresentation: (value: unknown) => value,
}))
vi.mock('../../../../store/authStore', () => ({
    useAuthStore: (selector: (state: unknown) => unknown) => selector({ user: { tenantId: 'tenant-1' } }),
}))
vi.mock('@/utils/htmlSanitizer', () => ({ sanitizeRichTextHtml: (value: string) => value }))

import { CmsPageEditor } from './CmsPageEditor'

function page(version: number, title = 'About', published = false) {
    const snapshot = {
        slug: 'about',
        title,
        contentType: 'RichText',
        content: '<p>About</p>',
        schemaVersion: 1,
        version,
        seo: {},
    }
    return {
        id: 'page-1',
        draft: published ? null : snapshot,
        published: published ? snapshot : null,
    }
}

describe('CmsPageEditor authoring UX', () => {
    beforeEach(() => {
        Object.values(cmsService).forEach((mock) => mock.mockReset())
        extractCmsConflict.mockClear()
        cmsService.getPage.mockResolvedValue(page(1))
        cmsService.updatePage.mockImplementation(async (_id, request) => page(2, request.title))
        cmsService.publishPage.mockImplementation(async (_id, _version) => page(2, 'Changed title', true))
    })

    it('tracks unsaved state and keeps preview isolated from saves and versions', async () => {
        render(<CmsPageEditor />)
        await screen.findByText('Draft')

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Changed title' } })
        expect(screen.getByText('Unsaved changes')).toBeInTheDocument()
        expect(window.dispatchEvent(new Event('beforeunload', { cancelable: true }))).toBe(false)

        expect(screen.getByRole('button', { name: 'Preview Saved Draft' })).toBeDisabled()
        expect(cmsService.createPreviewLink).not.toHaveBeenCalled()
        expect(cmsService.updatePage).not.toHaveBeenCalled()
    })

    it('saves dirty form state before publish and adopts each accepted response', async () => {
        render(<CmsPageEditor />)
        await screen.findByText('Draft')
        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Changed title' } })

        fireEvent.click(screen.getByRole('button', { name: 'Publish Page' }))
        fireEvent.click(within(screen.getByRole('dialog', { name: 'Publish Page?' })).getByRole('button', { name: 'Publish Page' }))

        await waitFor(() => expect(cmsService.updatePage).toHaveBeenCalledWith(
            'page-1',
            expect.objectContaining({ title: 'Changed title', expectedVersion: 1 }),
        ))
        await waitFor(() => expect(cmsService.publishPage).toHaveBeenCalledWith('page-1', 2))
        await screen.findByText('Page published successfully.')
        expect(screen.getByText('Published')).toBeInTheDocument()
    })

    it('keeps publish disabled after a successful publish response removes the draft', async () => {
        render(<CmsPageEditor />)
        await screen.findByText('Draft')

        const publishButton = screen.getByRole('button', { name: 'Publish Page' })
        fireEvent.click(publishButton)
        fireEvent.click(within(screen.getByRole('dialog', { name: 'Publish Page?' })).getByRole('button', { name: 'Publish Page' }))

        await screen.findByText('Page published successfully.')
        expect(cmsService.publishPage).toHaveBeenCalledWith('page-1', 1)
        expect(publishButton).toBeDisabled()

        fireEvent.click(publishButton)
        expect(cmsService.publishPage).toHaveBeenCalledTimes(1)
    })

    it('retains the candidate on conflict and adopts LatestVersion only after Load Latest', async () => {
        cmsService.publishPage.mockRejectedValueOnce({
            response: {
                status: 409,
                data: {
                    code: 'CONCURRENCY_CONFLICT',
                    message: 'The page was already published.',
                    LatestVersion: page(2, 'Backend title', true),
                },
            },
        })

        render(<CmsPageEditor />)
        await screen.findByText('Draft')

        fireEvent.click(screen.getByRole('button', { name: 'Publish Page' }))
        fireEvent.click(within(screen.getByRole('dialog', { name: 'Publish Page?' })).getByRole('button', { name: 'Publish Page' }))

        await screen.findByRole('dialog', { name: 'A newer version is available' })
        expect(screen.getByLabelText('Title')).toHaveValue('About')
        fireEvent.click(screen.getByRole('button', { name: 'Load Latest' }))
        expect(screen.getByLabelText('Title')).toHaveValue('Backend title')
        expect(screen.getByText('Published')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Publish Page' })).toBeDisabled()
        expect(cmsService.getPage).toHaveBeenCalledTimes(1)

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Backend title edited' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))

        await waitFor(() => expect(cmsService.updatePage).toHaveBeenCalledWith(
            'page-1',
            expect.objectContaining({ title: 'Backend title edited', expectedVersion: 2 }),
        ))
    })
})

import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const navigate = vi.hoisted(() => vi.fn())
const windowOpen = vi.hoisted(() => vi.fn())
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
vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        i18n: { language: 'en' },
        t: (key: string, options?: { defaultValue?: string }) => ({
            'storefront.homepageEditor.status.draft': 'Draft',
            'storefront.homepageEditor.status.dirty': 'Unsaved changes',
            'storefront.homepageEditor.status.published': 'Published',
            'storefront.homepageEditor.keepEditing': 'Keep editing',
            'storefront.homepageEditor.cancel': 'Cancel',
            'storefront.homepageEditor.unsavedChanges': 'Unsaved changes',
            'storefront.homepageEditor.leaveWarning': 'Leaving now will discard changes that have not been saved.',
            'storefront.homepageEditor.discardAndLeave': 'Discard and leave',
            'storefront.homepageEditor.newerVersion': 'A newer version is available',
            'storefront.homepageEditor.concurrentChange': 'Someone else changed this content while you were editing. Your draft is still here.',
            'storefront.homepageEditor.conflictHint': 'Choose Load Latest to adopt the accepted server version, or Retry My Draft to reapply your retained changes against it.',
            'storefront.homepageEditor.loadLatest': 'Load Latest',
            'storefront.homepageEditor.retryDraft': 'Retry My Draft',
            'storefront.cms.pageEditor.preview': 'Preview Saved Draft',
            'storefront.cms.pageEditor.saveDraft': 'Save Draft',
            'storefront.cms.pageEditor.publishPage': 'Publish Page',
            'storefront.cms.pageEditor.publishConfirmTitle': 'Publish Page?',
            'storefront.cms.pageEditor.savedWithNewerEdits': 'Draft saved. Newer unsaved edits were retained.',
            'storefront.cms.pageEditor.saved': 'Draft saved.',
            'storefront.cms.pageEditor.published': 'Page published successfully.',
            'storefront.cms.pageEditor.titleLabel': 'Title',
        }[key] || options?.defaultValue || key),
    }),
}))
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

function deferred<T>() {
    let resolve!: (value: T) => void
    const promise = new Promise<T>((resolvePromise) => {
        resolve = resolvePromise
    })
    return { promise, resolve }
}

describe('CmsPageEditor authoring UX', () => {
    beforeEach(() => {
        Object.values(cmsService).forEach((mock) => mock.mockReset())
        windowOpen.mockReset()
        extractCmsConflict.mockClear()
        cmsService.getPage.mockResolvedValue(page(1))
        cmsService.updatePage.mockImplementation(async (_id, request) => page(2, request.title))
        cmsService.publishPage.mockImplementation(async (_id, _version) => page(2, 'Changed title', true))
        cmsService.createPreviewLink.mockResolvedValue({
            slug: 'about',
            previewToken: 'backend-token',
            previewUrl: 'https://shop.example.test/about?preview=true&previewToken=backend-token',
            expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
        })
        Object.defineProperty(window, 'open', {
            configurable: true,
            value: windowOpen,
        })
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

    it('opens only the backend-resolved CMS preview URL', async () => {
        render(<CmsPageEditor />)
        await screen.findByText('Draft')

        fireEvent.click(screen.getByRole('button', { name: 'Preview Saved Draft' }))

        await waitFor(() => expect(cmsService.createPreviewLink).toHaveBeenCalledWith('page-1'))
        expect(windowOpen).toHaveBeenCalledWith(
            'https://shop.example.test/about?preview=true&previewToken=backend-token',
            '_blank',
            'noopener,noreferrer',
        )
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

    it('does not let an older save response overwrite edits made while the request is in flight', async () => {
        const pendingSave = deferred<ReturnType<typeof page>>()
        cmsService.updatePage
            .mockImplementationOnce(() => pendingSave.promise)
            .mockImplementationOnce(async (_id, request) => page(3, request.title))

        render(<CmsPageEditor />)
        await screen.findByText('Draft')

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Submitted title' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))
        await waitFor(() => expect(cmsService.updatePage).toHaveBeenCalledWith(
            'page-1',
            expect.objectContaining({ title: 'Submitted title', expectedVersion: 1 }),
        ))

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Newer local title' } })
        pendingSave.resolve(page(2, 'Submitted title'))

        await screen.findByText('Draft saved. Newer unsaved edits were retained.')
        expect(screen.getByLabelText('Title')).toHaveValue('Newer local title')
        expect(screen.getByText('Unsaved changes')).toBeInTheDocument()

        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))
        await waitFor(() => expect(cmsService.updatePage).toHaveBeenLastCalledWith(
            'page-1',
            expect.objectContaining({ title: 'Newer local title', expectedVersion: 2 }),
        ))
    })

    it('authors Arabic with the shared slug and submits one aggregate snapshot', async () => {
        render(<CmsPageEditor />)
        await screen.findByText('Draft')

        fireEvent.click(screen.getByRole('tab', { name: 'Arabic (optional)' }))
        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'من نحن' } })

        const editor = document.querySelector('[contenteditable="true"]') as HTMLDivElement
        editor.innerHTML = '<p>المحتوى العربي</p>'
        fireEvent.blur(editor)
        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))

        await waitFor(() => expect(cmsService.updatePage).toHaveBeenCalledWith(
            'page-1',
            expect.objectContaining({
                slug: 'about',
                title: 'About',
                expectedVersion: 1,
                translations: {
                    en: expect.objectContaining({ title: 'About', content: '<p>About</p>' }),
                    ar: expect.objectContaining({ title: 'من نحن', content: '<p>المحتوى العربي</p>' }),
                },
            }),
        ))
    })
})

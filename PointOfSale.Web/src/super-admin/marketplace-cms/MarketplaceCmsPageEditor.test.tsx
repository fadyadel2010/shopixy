import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const service = vi.hoisted(() => ({
    createPage: vi.fn(),
    getPage: vi.fn(),
    getPageVersion: (page: any) => page?.draft?.version ?? page?.published?.version ?? null,
    isConcurrencyConflict: (error: any) => (
        error?.status === 409 && error?.code === 'MARKETPLACE_CMS_CONCURRENCY_CONFLICT'
    ),
    previewPage: vi.fn(),
    publishPage: vi.fn(),
    translateContent: vi.fn(),
    updatePage: vi.fn(),
}))

vi.mock('./marketplaceCmsService', () => ({ marketplaceCmsService: service }))
vi.mock('@/utils/htmlSanitizer', () => ({ sanitizeRichTextHtml: (value: string) => value }))

import { MarketplaceCmsPageEditor } from './MarketplaceCmsPageEditor'

const pageId = '11111111-1111-4111-8111-111111111111'

function draftResponse(version: number) {
    return pageResponse({ draft: version, published: null })
}

function publishedResponse(version: number) {
    return pageResponse({ draft: null, published: version })
}

function pageResponse(versions: { draft: number | null, published: number | null }) {
    const createVersion = (version: number) => ({
        version,
        slug: 'authority',
        title: 'Authority',
        titleLocalized: { en: 'Authority', ar: '' },
        contentType: 'RichText',
        content: `<p>v${version}</p>`,
        localizedContent: { en: `<p>v${version}</p>` },
        seo: {},
    })

    return {
        id: pageId,
        slug: 'authority',
        title: 'Authority',
        titleLocalized: { en: 'Authority', ar: '' },
        contentType: 'RichText',
        hasPendingChanges: versions.draft != null,
        draft: versions.draft == null ? null : createVersion(versions.draft),
        published: versions.published == null ? null : createVersion(versions.published),
    }
}

function renderEditor(entry: string) {
    return render(
        <MemoryRouter initialEntries={[entry]}>
            <Routes>
                <Route path="/super-admin/marketplace/pages/:pageId" element={<MarketplaceCmsPageEditor />} />
            </Routes>
        </MemoryRouter>,
    )
}

describe('MarketplaceCmsPageEditor version adoption', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        vi.spyOn(window, 'alert').mockImplementation(() => undefined)
        vi.spyOn(window, 'confirm').mockReturnValue(true)
    })

    afterEach(() => {
        vi.restoreAllMocks()
    })

    it('uses create response version evidence for an immediate publish', async () => {
        const created = draftResponse(1)
        service.createPage.mockResolvedValue(created)
        service.getPage.mockResolvedValue(created)
        service.publishPage.mockResolvedValue(publishedResponse(1))
        const { container } = renderEditor('/super-admin/marketplace/pages/new')
        const inputs = container.querySelectorAll('input')

        fireEvent.change(inputs[0], { target: { value: 'Authority' } })
        fireEvent.change(inputs[1], { target: { value: 'authority' } })
        fireEvent.click(screen.getByRole('button', { name: 'Publish' }))

        await waitFor(() => expect(service.createPage).toHaveBeenCalledTimes(1))
        await waitFor(() => expect(service.publishPage).toHaveBeenCalledWith(pageId, 1))
    })

    it('adopts each update response before repeated update and publish', async () => {
        service.getPage.mockResolvedValue(draftResponse(1))
        service.updatePage
            .mockResolvedValueOnce(draftResponse(2))
            .mockResolvedValueOnce(draftResponse(3))
        service.publishPage.mockResolvedValue(publishedResponse(3))
        renderEditor(`/super-admin/marketplace/pages/${pageId}`)

        const slug = await screen.findByDisplayValue('authority')
        fireEvent.change(slug, { target: { value: 'authority-two' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))
        await waitFor(() => expect(service.updatePage).toHaveBeenCalledTimes(1))
        fireEvent.change(screen.getByDisplayValue('authority'), { target: { value: 'authority-three' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))
        await waitFor(() => expect(service.updatePage).toHaveBeenCalledTimes(2))
        fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
        await waitFor(() => expect(service.publishPage).toHaveBeenCalledTimes(1))

        expect(service.updatePage.mock.calls[0][2]).toBe(1)
        expect(service.updatePage.mock.calls[1][2]).toBe(2)
        expect(service.publishPage).toHaveBeenCalledWith(pageId, 3)
    })

    it('refreshes and adopts the authoritative version after a conflict', async () => {
        const conflict = Object.assign(new Error('This page was modified by another admin.'), {
            status: 409,
            code: 'MARKETPLACE_CMS_CONCURRENCY_CONFLICT',
        })
        service.getPage.mockResolvedValueOnce(draftResponse(1)).mockResolvedValueOnce(draftResponse(4))
        service.updatePage.mockRejectedValueOnce(conflict).mockResolvedValueOnce(draftResponse(5))
        renderEditor(`/super-admin/marketplace/pages/${pageId}`)

        const slug = await screen.findByDisplayValue('authority')
        fireEvent.change(slug, { target: { value: 'my-draft' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))
        await screen.findByText(/latest page version has been loaded/i)
        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))
        await waitFor(() => expect(service.updatePage).toHaveBeenCalledTimes(2))

        expect(service.getPage).toHaveBeenCalledTimes(2)
        expect(service.updatePage.mock.calls[0][2]).toBe(1)
        expect(service.updatePage.mock.calls[1][2]).toBe(4)
    })

    it('keeps preview isolated while unsaved changes are present', async () => {
        service.getPage.mockResolvedValue(draftResponse(1))
        renderEditor(`/super-admin/marketplace/pages/${pageId}`)

        const slug = await screen.findByDisplayValue('authority')
        fireEvent.change(slug, { target: { value: 'preview-draft' } })
        expect(screen.getByText('Unsaved changes')).toBeInTheDocument()
        fireEvent.click(screen.getByRole('button', { name: 'Preview' }))

        expect(await screen.findByText(/Preview never saves or changes versions/)).toBeInTheDocument()
        expect(service.previewPage).not.toHaveBeenCalled()
        expect(service.updatePage).not.toHaveBeenCalled()
    })
})

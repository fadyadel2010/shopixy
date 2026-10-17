import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const service = vi.hoisted(() => ({
    deletePage: vi.fn(),
    getListItemVersion: (page: { draftVersion?: number | null; publishedVersion?: number | null } | null) => page?.draftVersion ?? page?.publishedVersion ?? null,
    isConcurrencyConflict: (error: { status?: number; code?: string } | null) => (
        error?.status === 409 && error?.code === 'MARKETPLACE_CMS_CONCURRENCY_CONFLICT'
    ),
    listPages: vi.fn(),
    previewPage: vi.fn(),
    publishPage: vi.fn(),
}))

vi.mock('./marketplaceCmsService', () => ({ marketplaceCmsService: service }))

import { MarketplaceCmsPagesPage } from './MarketplaceCmsPagesPage'

const pageId = '11111111-1111-4111-8111-111111111111'

function listItem(draftVersion: number | null, publishedVersion: number | null = null) {
    return {
        id: pageId,
        slug: 'authority',
        title: 'Authority',
        contentType: 'RichText',
        hasDraft: draftVersion != null,
        hasPublished: publishedVersion != null,
        hasPendingChanges: draftVersion != null,
        status: draftVersion != null ? 'Draft' : 'Published',
        draftVersion,
        publishedVersion,
        updatedAtUtc: '2026-08-03T00:00:00Z',
        publishedAtUtc: publishedVersion == null ? null : '2026-08-03T00:00:00Z',
    }
}

function publishedResponse(version: number) {
    return {
        ...listItem(null, version),
        draft: null,
        published: { version },
    }
}

function renderPage() {
    return render(
        <MemoryRouter>
            <MarketplaceCmsPagesPage />
        </MemoryRouter>,
    )
}

describe('MarketplaceCmsPagesPage version adoption', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        vi.spyOn(window, 'alert').mockImplementation(() => undefined)
        vi.spyOn(window, 'confirm').mockReturnValue(true)
    })

    afterEach(() => {
        vi.restoreAllMocks()
    })

    it('adopts the publish response version before delete', async () => {
        service.listPages.mockResolvedValue([listItem(3)])
        service.publishPage.mockResolvedValue(publishedResponse(3))
        service.deletePage.mockResolvedValue({ id: pageId, deleted: true })
        renderPage()

        expect(await screen.findByText('Authority')).toBeInTheDocument()
        fireEvent.click(screen.getByTitle('Publish'))
        await waitFor(() => expect(service.publishPage).toHaveBeenCalledWith(pageId, 3))
        fireEvent.click(screen.getByTitle('Delete'))
        await waitFor(() => expect(service.deletePage).toHaveBeenCalledWith(pageId, 3))

        expect(screen.queryByText('Authority')).not.toBeInTheDocument()
    })

    it('uses the latest version returned by refresh for the next mutation', async () => {
        service.listPages.mockResolvedValueOnce([listItem(1)]).mockResolvedValueOnce([listItem(2)])
        service.publishPage.mockResolvedValue(publishedResponse(2))
        renderPage()

        expect(await screen.findByText('Authority')).toBeInTheDocument()
        fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))
        await waitFor(() => expect(service.listPages).toHaveBeenCalledTimes(2))
        fireEvent.click(screen.getByTitle('Publish'))
        await waitFor(() => expect(service.publishPage).toHaveBeenCalledWith(pageId, 2))
    })
})

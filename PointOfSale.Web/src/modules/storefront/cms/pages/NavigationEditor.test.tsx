import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'

const cmsService = vi.hoisted(() => ({
    getNavigation: vi.fn(),
    getNavigationTargets: vi.fn(),
    publishNavigation: vi.fn(),
    updateNavigation: vi.fn(),
}))

vi.mock('../services', () => ({
    cmsService,
    extractNavigationAdmissionError: () => null,
    extractNavigationConflict: () => null,
}))
vi.mock('../../../../services/api', () => ({
    isRequestCanceled: () => false,
}))
vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        i18n: { language: 'en' },
        t: (key: string) => key,
    }),
}))

import { AuthoringPlatformProvider } from '../../authoringPlatform'
import { useCmsStore } from '../store'
import { NavigationEditor } from './NavigationEditor'

const pageId = '33333333-3333-4333-8333-333333333333'
const replacementPageId = '44444444-4444-4444-8444-444444444444'

function snapshot(rowVersion = '1', hasPendingChanges = false) {
    return {
        rowVersion,
        draft: {
            header: [
                { label: 'Home', labelAr: null, type: 'custom', path: '/', enabled: true, order: 0 },
                { label: 'Shop', labelAr: null, type: 'collection', collectionKey: 'all-products', path: '/products', enabled: true, order: 1 },
            ],
            footer: [
                { label: 'About', labelAr: null, type: 'cms', targetId: pageId, slug: 'about', enabled: true, order: 0 },
            ],
        },
        live: {
            header: [
                { label: 'Home', labelAr: null, type: 'custom', path: '/', enabled: true, order: 0 },
                { label: 'Shop', labelAr: null, type: 'collection', collectionKey: 'all-products', path: '/products', enabled: true, order: 1 },
            ],
            footer: [
                { label: 'About', labelAr: null, type: 'cms', targetId: pageId, slug: 'about', enabled: true, order: 0 },
            ],
        },
        hasPendingChanges,
        validationWarnings: [],
        brokenLinks: [],
    }
}

function deferred<T>() {
    let resolve!: (value: T) => void
    const promise = new Promise<T>((resolvePromise) => {
        resolve = resolvePromise
    })
    return { promise, resolve }
}

function renderEditor(route = '/storefront/navigation?view=header') {
    return render(
        <MemoryRouter initialEntries={[route]}>
            <AuthoringPlatformProvider>
                <NavigationEditor />
            </AuthoringPlatformProvider>
        </MemoryRouter>,
    )
}

describe('NavigationEditor', () => {
    beforeEach(() => {
        Object.values(cmsService).forEach((mock) => mock.mockReset())
        useCmsStore.getState().reset()
        cmsService.getNavigation.mockResolvedValue(snapshot())
        cmsService.getNavigationTargets.mockResolvedValue({
            categories: [],
            products: [],
            cmsPages: [
                { type: 'cms', targetId: pageId, label: 'About', state: 'published' },
                { type: 'cms', targetId: replacementPageId, label: 'Contact', state: 'published' },
            ],
            collections: [{ type: 'collection', collectionKey: 'all-products', label: 'All products', state: 'available' }],
        })
    })

    it('disables duplicate publication when draft and live are identical', async () => {
        renderEditor()

        await screen.findByRole('heading', { name: 'Navigation' })

        expect(screen.getByRole('button', { name: 'Publish Header and Footer Navigation' })).toBeDisabled()
        expect(cmsService.publishNavigation).not.toHaveBeenCalled()
    })

    it('edits backend-issued targets, enabled state, and an explicit Arabic label', async () => {
        renderEditor('/storefront/navigation?view=footer')
        await screen.findByText('About')

        fireEvent.click(screen.getByRole('button', { name: 'Edit navigation link' }))
        fireEvent.change(screen.getByLabelText('Target'), { target: { value: replacementPageId } })
        fireEvent.change(screen.getByLabelText('Arabic label'), { target: { value: 'اتصل بنا' } })
        fireEvent.click(screen.getByLabelText('Enabled'))
        fireEvent.click(screen.getByRole('button', { name: 'Save navigation link edits' }))

        expect(useCmsStore.getState().navigation.draft.footer[0]).toMatchObject({
            targetId: replacementPageId,
            labelAr: 'اتصل بنا',
            enabled: false,
        })
        expect(screen.getByText('Disabled')).toBeInTheDocument()
    })

    it('retains edits made during a save and uses the rotated RowVersion for the next save', async () => {
        const pendingSave = deferred<ReturnType<typeof snapshot>>()
        cmsService.updateNavigation
            .mockImplementationOnce(() => pendingSave.promise)
            .mockImplementationOnce(async (candidate) => ({ ...candidate, rowVersion: '3', hasPendingChanges: true }))

        renderEditor()
        await screen.findByText('Home')

        fireEvent.click(screen.getAllByRole('button', { name: 'Edit navigation link' })[0])
        fireEvent.change(screen.getByLabelText('Link label'), { target: { value: 'Submitted Home' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save navigation link edits' }))
        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))
        await waitFor(() => expect(cmsService.updateNavigation).toHaveBeenCalledTimes(1))

        fireEvent.click(screen.getAllByRole('button', { name: 'Edit navigation link' })[1])
        fireEvent.change(screen.getByLabelText('Link label'), { target: { value: 'Newer Shop' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save navigation link edits' }))

        const accepted = snapshot('2', true)
        accepted.draft.header[0].label = 'Submitted Home'
        pendingSave.resolve(accepted)

        await waitFor(() => {
            expect(useCmsStore.getState().navigation.rowVersion).toBe('2')
            expect(useCmsStore.getState().navigation.draft.header[1].label).toBe('Newer Shop')
            expect(useCmsStore.getState().isNavigationDirty).toBe(true)
        })

        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))
        await waitFor(() => expect(cmsService.updateNavigation).toHaveBeenCalledTimes(2))
        expect(cmsService.updateNavigation.mock.calls[1][0]).toMatchObject({
            rowVersion: '2',
            draft: { header: [{ label: 'Submitted Home' }, { label: 'Newer Shop' }] },
        })
    })
})

import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
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
        t: (key: string, options?: { count?: number; defaultValue?: string }) => ({
            'storefront.cms.navigationEditor.title': 'Navigation',
            'storefront.cms.navigationEditor.publish': 'Publish Header and Footer Navigation',
            'storefront.cms.navigationEditor.editLink': 'Edit navigation link',
            'storefront.cms.navigationEditor.englishLabel': 'English label',
            'storefront.cms.navigationEditor.arabicLabelShort': 'Arabic label',
            'storefront.cms.navigationEditor.target': 'Target',
            'storefront.cms.navigationEditor.selectPage': 'Select page',
            'storefront.cms.navigationEditor.enabled': 'Enabled',
            'storefront.cms.navigationEditor.saveLinkEdits': 'Save navigation link edits',
            'storefront.cms.navigationEditor.cancelLinkEdits': 'Cancel navigation link edits',
            'storefront.cms.navigationEditor.disabled': 'Disabled',
            'storefront.cms.navigationEditor.saveDraft': 'Save Draft',
            'storefront.cms.navigationEditor.addLink': 'Add Link',
            'storefront.cms.navigationEditor.targets.noPages': 'No pages are available to link.',
            'storefront.cms.navigationEditor.targets.createPage': 'Create page',
            'storefront.cms.navigationEditor.targets.refreshPages': 'Refresh pages',
            'storefront.cms.navigationEditor.repair': 'Repair',
            'storefront.cms.navigationEditor.remove': 'Remove',
            'storefront.cms.navigationEditor.deleteLink': 'Delete navigation link',
            'storefront.cms.navigationEditor.deleteConfirmTitle': 'Delete Navigation link?',
        }[key] || options?.defaultValue || key),
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

    it('discards a saved announcement draft by saving the published snapshot and stays clean', async () => {
        const published = { enabled: false, text: 'Published', textAr: '', link: '', backgroundColor: '', textColor: '' }
        const data = { ...snapshot('4', true), draft: { ...snapshot().draft, announcement: { ...published, enabled: true, text: 'Saved draft' } }, live: { ...snapshot().live, announcement: published } }
        cmsService.getNavigation.mockResolvedValue(data)
        cmsService.updateNavigation.mockImplementation(async (candidate) => ({ ...candidate, rowVersion: '5', hasPendingChanges: false }))
        renderEditor()
        await screen.findByRole('heading', { name: 'Navigation' })
        fireEvent.click(screen.getByRole('button', { name: 'storefront.cms.navigationEditor.discard' }))
        await waitFor(() => expect(cmsService.updateNavigation).toHaveBeenCalledWith(expect.objectContaining({ draft: data.live }), expect.anything()))
        await waitFor(() => expect(useCmsStore.getState().isNavigationDirty).toBe(false))
        expect(useCmsStore.getState().navigation.draft.announcement).toEqual(published)
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
        fireEvent.change(screen.getByLabelText('English label'), { target: { value: 'Submitted Home' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save navigation link edits' }))
        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))
        await waitFor(() => expect(cmsService.updateNavigation).toHaveBeenCalledTimes(1))

        fireEvent.click(screen.getAllByRole('button', { name: 'Edit navigation link' })[1])
        fireEvent.change(screen.getByLabelText('English label'), { target: { value: 'Newer Shop' } })
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

    it('shows a genuine empty-page state and refreshes after creating a page from Navigation', async () => {
        const open = vi.spyOn(window, 'open').mockReturnValue({ focus: vi.fn() } as unknown as Window)
        cmsService.getNavigationTargets
            .mockResolvedValueOnce({
                categories: [],
                products: [],
                cmsPages: [],
                collections: [{ type: 'collection', collectionKey: 'all-products', label: 'All products', state: 'available' }],
            })
            .mockResolvedValueOnce({
                categories: [],
                products: [],
                cmsPages: [{ type: 'cms', targetId: replacementPageId, label: 'New page', state: 'draft' }],
                collections: [{ type: 'collection', collectionKey: 'all-products', label: 'All products', state: 'available' }],
            })

        renderEditor()
        await screen.findByText('Home')
        fireEvent.click(screen.getByRole('button', { name: 'Add Link' }))

        expect(await screen.findByText('No pages are available to link.')).toBeInTheDocument()
        fireEvent.click(screen.getByRole('button', { name: 'Create page' }))
        expect(open).toHaveBeenCalledWith('/storefront/content/pages/new?returnTo=navigation', '_blank')

        await act(async () => {
            window.dispatchEvent(new MessageEvent('message', {
                origin: window.location.origin,
                data: { type: 'shopixy:cms-page-created', pageId: replacementPageId },
            }))
        })

        await waitFor(() => expect(screen.getByLabelText(/^Select page/)).toHaveValue(replacementPageId))
        expect(screen.getByLabelText(/^English label/)).toHaveValue('New page')
        open.mockRestore()
    })

    it('distinguishes target-load failure from an empty page collection and supports retry', async () => {
        cmsService.getNavigationTargets
            .mockRejectedValueOnce(new Error('network unavailable'))
            .mockResolvedValueOnce({
                categories: [],
                products: [],
                cmsPages: [{ type: 'cms', targetId: pageId, label: 'About', state: 'published' }],
                collections: [],
            })

        renderEditor()
        await screen.findByText('Home')
        fireEvent.click(screen.getByRole('button', { name: 'Add Link' }))

        expect(await screen.findByRole('alert')).toHaveTextContent('storefront.cms.navigationEditor.targets.loadFailed')
        fireEvent.click(screen.getByRole('button', { name: 'storefront.cms.navigationEditor.retry' }))
        await waitFor(() => expect(screen.getByLabelText(/^Select page/)).toBeInTheDocument())
        expect(cmsService.getNavigationTargets).toHaveBeenCalledTimes(2)
    })

    it('offers direct repair and removal for an invalid CMS target', async () => {
        const broken = snapshot()
        broken.draft.footer[0] = {
            ...broken.draft.footer[0],
            targetId: null,
            validationStatus: 'invalid',
            validationCode: 'NAVIGATION_CMS_PAGE_NOT_FOUND',
            validationMessage: 'The selected CMS page no longer exists.',
        }
        broken.validationWarnings = [{
            code: 'NAVIGATION_CMS_PAGE_NOT_FOUND',
            message: 'The selected CMS page no longer exists.',
            snapshot: 'draft',
            menu: 'footer',
            index: 0,
            linkType: 'cms',
            status: 'invalid',
        }]
        cmsService.getNavigation.mockResolvedValue(broken)

        renderEditor('/storefront/navigation?view=footer')
        await screen.findByRole('button', { name: 'Repair' })
        fireEvent.click(screen.getByRole('button', { name: 'Repair' }))
        expect(screen.getByLabelText('Target')).toBeInTheDocument()
        fireEvent.click(screen.getByRole('button', { name: 'Cancel navigation link edits' }))

        fireEvent.click(screen.getByRole('button', { name: 'Delete navigation link' }))
        fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete navigation link' }))
        expect(useCmsStore.getState().navigation.draft.footer).toEqual([])
        expect(useCmsStore.getState().isNavigationDirty).toBe(true)
    })
})

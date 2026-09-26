import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const service = vi.hoisted(() => ({
    loadFooterConfiguration: vi.fn(),
    saveFooterConfiguration: vi.fn(),
}))

vi.mock('./footerAuthoringService', () => service)
vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (key: string, options?: { defaultValue?: string }) => ({
            'storefront.homepageEditor.concurrentChange': 'Another author saved first.',
            'storefront.homepageEditor.conflictHint': 'Choose Load Latest to adopt the accepted server version, or Retry My Draft to reapply your retained changes against it.',
            'storefront.homepageEditor.keepEditing': 'Keep editing',
            'storefront.homepageEditor.loadLatest': 'Load Latest',
            'storefront.homepageEditor.newerVersion': 'A newer version is live',
            'storefront.homepageEditor.retryDraft': 'Retry My Draft',
        }[key] || options?.defaultValue || key),
    }),
}))

import { FooterConfigScreen } from './FooterConfigScreen'

const firstRevision = 'a'.repeat(64)
const latestRevision = 'b'.repeat(64)
const savedRevision = 'c'.repeat(64)

function configuration(revision = firstRevision, title = 'Company') {
    return {
        scope: 'Tenant',
        revision,
        updatedAtUtc: '2026-08-03T00:00:00Z',
        sections: [{
            id: 'section-id',
            title,
            titleLocalized: { en: title, ar: '' },
            order: 0,
            isActive: true,
            links: [{
                id: 'link-id',
                label: 'About',
                labelLocalized: { en: 'About', ar: '' },
                type: 'Cms',
                url: null,
                cmsPageSlug: 'about',
                openInNewTab: false,
                order: 0,
                isActive: true,
            }],
        }],
        socialLinks: [],
    }
}

function renderEditor() {
    return render(
        <FooterConfigScreen
            scope="Tenant"
            title="Footer editor"
            description="Description"
            note="Immediate publication"
            localized
            loadCmsPages={async () => [{ slug: 'about', title: 'About' }]}
        />,
    )
}

describe('FooterConfigScreen', () => {
    beforeEach(() => {
        service.loadFooterConfiguration.mockReset()
        service.saveFooterConfiguration.mockReset()
        service.loadFooterConfiguration.mockResolvedValue(configuration())
        service.saveFooterConfiguration.mockImplementation(async (_scope, request) => (
            configuration(savedRevision, request.sections[0].title)
        ))
    })

    it('tracks dirty state, guards unload, cancels changes, and confirms deletion', async () => {
        renderEditor()
        await screen.findByText('Live')

        const title = screen.getByPlaceholderText('Section title')
        fireEvent.change(title, { target: { value: 'Changed company' } })
        expect(screen.getByText('Unsaved changes')).toBeInTheDocument()
        expect(window.dispatchEvent(new Event('beforeunload', { cancelable: true }))).toBe(false)

        fireEvent.click(screen.getByRole('button', { name: 'Discard' }))
        expect(screen.getByRole('dialog', { name: 'Discard Footer changes?' })).toBeInTheDocument()
        fireEvent.click(screen.getByRole('button', { name: 'Discard Changes' }))
        await waitFor(() => expect(screen.getByPlaceholderText('Section title')).toHaveValue('Company'))

        fireEvent.click(screen.getByRole('button', { name: /delete section/i }))
        expect(screen.getByRole('dialog', { name: 'Delete Footer item?' })).toBeInTheDocument()
        fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }))
        expect(screen.getByText('Footer Sections')).toBeInTheDocument()
    })

    it('prevents invalid authoring before HTTP and adopts the save response as the new baseline', async () => {
        renderEditor()
        await screen.findByText('Live')

        fireEvent.change(screen.getByPlaceholderText('Section title'), { target: { value: '' } })
        expect(screen.getByText('Section 1 needs an English title.')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Save live Footer' })).toBeDisabled()
        expect(service.saveFooterConfiguration).not.toHaveBeenCalled()

        fireEvent.change(screen.getByPlaceholderText('Section title'), { target: { value: 'Updated company' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save live Footer' }))
        fireEvent.click(within(screen.getByRole('dialog', { name: 'Save Footer live?' })).getByRole('button', { name: 'Save live Footer' }))

        await screen.findByText('Footer saved live')
        expect(service.saveFooterConfiguration).toHaveBeenCalledWith(
            'Tenant',
            expect.objectContaining({ expectedRevision: firstRevision }),
        )
        expect(screen.getByText('Live')).toBeInTheDocument()

        fireEvent.change(screen.getByPlaceholderText('Section title'), { target: { value: 'Updated again' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save live Footer' }))
        fireEvent.click(within(screen.getByRole('dialog', { name: 'Save Footer live?' })).getByRole('button', { name: 'Save live Footer' }))
        await waitFor(() => expect(service.saveFooterConfiguration).toHaveBeenLastCalledWith(
            'Tenant',
            expect.objectContaining({ expectedRevision: savedRevision }),
        ))
    })

    it('preserves the draft on conflict and retries only against the fetched latest revision', async () => {
        service.saveFooterConfiguration
            .mockRejectedValueOnce({ status: 409, code: 'FOOTER_CONCURRENCY_CONFLICT', message: 'Another author saved first.' })
            .mockResolvedValueOnce(configuration(savedRevision, 'My draft'))
        service.loadFooterConfiguration
            .mockResolvedValueOnce(configuration())
            .mockResolvedValueOnce(configuration(latestRevision, 'Other author'))

        renderEditor()
        await screen.findByText('Live')
        fireEvent.change(screen.getByPlaceholderText('Section title'), { target: { value: 'My draft' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save live Footer' }))
        fireEvent.click(within(screen.getByRole('dialog', { name: 'Save Footer live?' })).getByRole('button', { name: 'Save live Footer' }))

        await screen.findByText(/Another author saved first\.?/)
        expect(screen.getByPlaceholderText('Section title')).toHaveValue('My draft')
        fireEvent.click(screen.getByRole('button', { name: 'Retry My Draft' }))

        await screen.findByText('Footer saved live')
        expect(service.saveFooterConfiguration).toHaveBeenLastCalledWith(
            'Tenant',
            expect.objectContaining({ expectedRevision: latestRevision }),
        )
        expect(screen.getByText('Live')).toBeInTheDocument()
    })

    it('fails closed when the authoritative load fails', async () => {
        service.loadFooterConfiguration.mockRejectedValueOnce({ status: 403, code: 'FORBIDDEN' })
        renderEditor()

        await screen.findByText('Permission required')
        expect(screen.queryByRole('button', { name: 'Save live Footer' })).not.toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Retry loading' })).toBeInTheDocument()
    })

    it('preserves unsaved localized edits across focus and visibility changes', async () => {
        renderEditor()
        await screen.findByText('Live')

        const title = screen.getByRole('textbox', { name: 'Section title EN' })
        fireEvent.change(title, { target: { value: 'Unsaved focus-safe title' } })
        fireEvent.blur(title)
        window.dispatchEvent(new Event('blur'))
        window.dispatchEvent(new Event('focus'))
        document.dispatchEvent(new Event('visibilitychange'))

        expect(screen.getByRole('textbox', { name: 'Section title EN' })).toHaveValue('Unsaved focus-safe title')
        expect(screen.getByText('Unsaved changes')).toBeInTheDocument()
        expect(service.loadFooterConfiguration).toHaveBeenCalledTimes(1)
        expect(service.saveFooterConfiguration).not.toHaveBeenCalled()
    })

    it('exposes labelled reorder, toggle, localized, target, and social controls', async () => {
        const configured = configuration()
        configured.socialLinks = [{
            id: 'social-id', platform: 'Instagram', url: 'https://instagram.com/store', icon: null, order: 0, isActive: true,
        }]
        service.loadFooterConfiguration.mockResolvedValueOnce(configured)
        renderEditor()
        await screen.findByText('Live')

        expect(screen.getByRole('checkbox', { name: 'Section 1 active' })).toBeChecked()
        expect(screen.getByRole('combobox', { name: 'Link 1 type' })).toHaveValue('Cms')
        expect(screen.getByRole('combobox', { name: 'Link 1 CMS page' })).toHaveValue('about')
        expect(screen.getByRole('checkbox', { name: 'Link 1 opens in new tab' })).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Move link 1 up' })).toBeDisabled()
        expect(screen.getByRole('textbox', { name: 'Social link 1 platform' })).toHaveValue('Instagram')
    })
})

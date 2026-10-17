import { afterEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'

import { StorefrontBuilderPage } from './StorefrontBuilderPage'
import { useBuilderStore } from '../store'
import { storeConfigService } from '../services'

const tenantId = '11111111-1111-4111-8111-111111111111'
const userId = '22222222-2222-4222-8222-222222222222'
const currentUser = { id: userId, tenantId }

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (key: string) => key,
        i18n: { language: 'en' },
    }),
}))

vi.mock('../../../store/authStore', () => ({
    useAuthStore: (selector: (state: { user: { id: string; tenantId: string } }) => unknown) => selector({
        user: currentUser,
    }),
}))

vi.mock('../components/builder', () => ({
    BuilderTopBar: ({ onSave, onDiscard }: { onSave: () => void; onDiscard: () => void }) => (
        <><button onClick={onSave}>Save Draft</button><button onClick={onDiscard}>Discard</button></>
    ),
    SectionPalette: () => null,
    BuilderCanvas: ({ storeConfig }: { storeConfig?: { storeName?: string } | null }) => (
        <div data-testid="builder-canvas">{storeConfig?.storeName}</div>
    ),
    SettingsPanel: () => null,
    PublishModal: () => null,
    RollbackModal: () => null,
    PreviewModal: () => null,
}))

vi.mock('../services', () => ({
    storeConfigService: {
        getStoreConfig: vi.fn(),
        getRuntimeStoreConfig: vi.fn(),
        updatePageLayout: vi.fn(),
        getPreviewConfig: vi.fn(),
        publishPage: vi.fn(),
        rollback: vi.fn(),
        getVersionHistory: vi.fn(),
        deleteProductTemplate: vi.fn(),
    },
}))

const baseConfig = {
        tenantId,
        storeName: 'Loop Probe Store',
        storefrontStatus: 'live',
        themeId: 'modern',
        storefrontTheme: 'modern',
        rowVersion: 'rv-1',
        currency: 'EGP',
        pages: {
            home: { sections: [] },
        },
        productTemplates: {},
}
function admittedConfig(): typeof baseConfig;
function admittedConfig<T extends Record<string, import('../../../services/apiJson').ApiJson>>(overrides: T): typeof baseConfig & T;
function admittedConfig(overrides = {}) {
    return { ...baseConfig, ...overrides }
}

afterEach(() => {
    vi.clearAllMocks()
    useBuilderStore.getState().reset()
})

describe('StorefrontBuilderPage runtime load stability', () => {
    it('retains the candidate and does not announce latest-loaded when conflict reload fails', async () => {
        vi.mocked(storeConfigService.getStoreConfig).mockResolvedValueOnce(admittedConfig()).mockRejectedValueOnce(new Error('Read unavailable'))
        vi.mocked(storeConfigService.getRuntimeStoreConfig).mockResolvedValue(admittedConfig())
        vi.mocked(storeConfigService.updatePageLayout).mockRejectedValueOnce({ response: { status: 409, data: { message: 'Another administrator saved.' } } })
        render(<MemoryRouter initialEntries={['/storefront/homepage']}><StorefrontBuilderPage /></MemoryRouter>)
        await screen.findByTestId('builder-canvas')
        act(() => useBuilderStore.getState().addSection('text-block', undefined, { get: () => ({ type: 'text-block' }) }))
        const candidate = useBuilderStore.getState().sections
        fireEvent.click(screen.getByText('Save Draft'))
        await screen.findByRole('dialog')
        fireEvent.click(screen.getByText('storefront.homepageEditor.loadLatest'))
        await waitFor(() => expect(storeConfigService.getStoreConfig).toHaveBeenCalledTimes(2))
        expect(useBuilderStore.getState().sections).toEqual(candidate)
        expect(useBuilderStore.getState().isDirty).toBe(true)
        expect(screen.queryByText('storefront.homepageEditor.latestLoaded')).not.toBeInTheDocument()
    })

    it('discards a saved media draft through the existing save authority and restores the published reference locally', async () => {
        const imageA = '/api/storefront/homepage-media/11111111-1111-4111-8111-111111111111'
        const imageB = '/api/storefront/homepage-media/22222222-2222-4222-8222-222222222222'
        const section = { id: 'section_12345678', type: 'hero-banner', enabled: true, order: 0, settings: { title: 'Hero', subtitle: '', ctaText: 'Shop', ctaLink: '/products', backgroundImage: imageB, backgroundColor: '#ffffff', overlayOpacity: 20, textColor: '#000000', alignment: 'left', height: 'large', highlights: [] } }
        const published = { ...section, settings: { ...section.settings, backgroundImage: imageA } }
        const config = admittedConfig({ pages: { home: { sections: [section] } }, livePages: { home: { sections: [published] } } })
        vi.mocked(storeConfigService.getStoreConfig).mockResolvedValue(config)
        vi.mocked(storeConfigService.getRuntimeStoreConfig).mockResolvedValue(config)
        vi.mocked(storeConfigService.updatePageLayout).mockResolvedValue({ ...config, rowVersion: 'rv-2', pages: config.livePages })
        render(<MemoryRouter initialEntries={['/storefront/homepage']}><StorefrontBuilderPage /></MemoryRouter>)
        await screen.findByTestId('builder-canvas')
        await waitFor(() => expect(useBuilderStore.getState().sections).toHaveLength(1))
        fireEvent.click(screen.getByRole('button', { name: 'Discard' }))
        await waitFor(() => expect(storeConfigService.updatePageLayout).toHaveBeenCalled())
        await waitFor(() => expect(useBuilderStore.getState().sections[0].settings.backgroundImage).toBe(imageA))
        await waitFor(() => expect(useBuilderStore.getState().isSaving).toBe(false))
        expect(useBuilderStore.getState().isDirty).toBe(false)
    })

    it('does not reload the backend when admitting storeConfig updates local state', async () => {
        vi.mocked(storeConfigService.getStoreConfig).mockResolvedValue(admittedConfig())
        vi.mocked(storeConfigService.getRuntimeStoreConfig).mockResolvedValue(admittedConfig({ rowVersion: 'runtime-rv' }))

        render(
            <MemoryRouter initialEntries={['/storefront/builder?page=home']}>
                <Routes>
                    <Route path="/storefront/builder" element={<StorefrontBuilderPage />} />
                </Routes>
            </MemoryRouter>
        )

        expect(await screen.findByTestId('builder-canvas')).toHaveTextContent('Loop Probe Store')

        await waitFor(() => {
            expect(storeConfigService.getStoreConfig).toHaveBeenCalledTimes(1)
            expect(storeConfigService.getRuntimeStoreConfig).toHaveBeenCalledTimes(1)
        })

        await new Promise((resolve) => setTimeout(resolve, 50))

        expect(storeConfigService.getStoreConfig).toHaveBeenCalledTimes(1)
        expect(storeConfigService.getRuntimeStoreConfig).toHaveBeenCalledTimes(1)
    })

    it('keeps unsaved work local across focus and visibility changes until Save Draft', async () => {
        vi.mocked(storeConfigService.getStoreConfig).mockResolvedValue(admittedConfig())
        vi.mocked(storeConfigService.getRuntimeStoreConfig).mockResolvedValue(admittedConfig())

        render(<MemoryRouter initialEntries={['/storefront/homepage']}><Routes>
            <Route path="/storefront/homepage" element={<StorefrontBuilderPage />} />
        </Routes></MemoryRouter>)
        await screen.findByTestId('builder-canvas')
        act(() => useBuilderStore.getState().addSection('text-block', undefined, {
            get: () => ({ type: 'text-block' }),
        }))
        expect(useBuilderStore.getState().isDirty).toBe(true)
        act(() => {
            window.dispatchEvent(new Event('blur'))
            window.dispatchEvent(new Event('focus'))
            document.dispatchEvent(new Event('visibilitychange'))
        })
        await new Promise((resolve) => setTimeout(resolve, 1700))
        expect(storeConfigService.updatePageLayout).not.toHaveBeenCalled()
        expect(storeConfigService.getStoreConfig).toHaveBeenCalledTimes(1)
        expect(useBuilderStore.getState().sections).toHaveLength(1)
        fireEvent.click(screen.getByText('Discard'))
        expect(useBuilderStore.getState().sections).toHaveLength(0)
    })

    it('does not overwrite an edit made while a Save Draft request is in flight', async () => {
        vi.mocked(storeConfigService.getStoreConfig).mockResolvedValue(admittedConfig())
        vi.mocked(storeConfigService.getRuntimeStoreConfig).mockResolvedValue(admittedConfig())
        let resolveSave!: (value: ReturnType<typeof admittedConfig>) => void
        vi.mocked(storeConfigService.updatePageLayout).mockImplementation(() => new Promise((resolve) => {
            resolveSave = resolve
        }))

        render(<MemoryRouter initialEntries={['/storefront/homepage']}><Routes>
            <Route path="/storefront/homepage" element={<StorefrontBuilderPage />} />
        </Routes></MemoryRouter>)
        await screen.findByTestId('builder-canvas')
        act(() => useBuilderStore.getState().addSection('text-block', undefined, {
            get: () => ({ type: 'text-block' }),
        }))
        const firstId = useBuilderStore.getState().sections[0].id
        fireEvent.click(screen.getByText('Save Draft'))
        await waitFor(() => expect(storeConfigService.updatePageLayout).toHaveBeenCalledTimes(1))
        act(() => useBuilderStore.getState().updateSectionSettings(firstId, { text: 'newer edit' }))
        await act(async () => resolveSave(admittedConfig({ rowVersion: '2', pages: {
            home: { sections: [{ ...useBuilderStore.getState().sections[0], settings: { text: 'older edit' } }] },
        } })))
        expect(useBuilderStore.getState().sections[0].settings.text).toBe('newer edit')
        expect(useBuilderStore.getState().isDirty).toBe(true)
    })
})

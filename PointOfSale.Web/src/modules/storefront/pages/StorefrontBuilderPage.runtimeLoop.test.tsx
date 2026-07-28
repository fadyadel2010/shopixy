import { afterEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { render, screen, waitFor } from '@testing-library/react'

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
    BuilderTopBar: () => null,
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

function admittedConfig(overrides = {}) {
    return {
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
        ...overrides,
    }
}

afterEach(() => {
    vi.clearAllMocks()
    useBuilderStore.getState().reset()
})

describe('StorefrontBuilderPage runtime load stability', () => {
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
})

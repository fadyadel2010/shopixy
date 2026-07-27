import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const {
    getStoreConfig,
    getRuntimeStoreConfig,
    mockCurrentUser,
} = vi.hoisted(() => ({
    getStoreConfig: vi.fn(),
    getRuntimeStoreConfig: vi.fn(),
    mockCurrentUser: {
        tenantId: 'tenant-1',
        role: 'Owner',
    },
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (key: string) => key,
        i18n: { language: 'en' },
    }),
}))

vi.mock('@dnd-kit/core', () => ({
    DndContext: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
    DragOverlay: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
    closestCenter: vi.fn(),
    PointerSensor: vi.fn(),
    KeyboardSensor: vi.fn(),
    useSensor: vi.fn(() => ({})),
    useSensors: vi.fn(() => []),
}))

vi.mock('@dnd-kit/sortable', () => ({
    sortableKeyboardCoordinates: vi.fn(),
}))

vi.mock('../components/builder', () => ({
    BuilderTopBar: () => <div data-testid="builder-topbar" />,
    SectionPalette: () => <div data-testid="section-palette" />,
    BuilderCanvas: () => <div data-testid="builder-canvas" />,
    SettingsPanel: () => <div data-testid="settings-panel" />,
    PublishModal: () => null,
    RollbackModal: () => null,
    PreviewModal: () => null,
}))

vi.mock('../builder', () => ({
    sectionRegistry: {
        get: vi.fn(),
    },
}))

vi.mock('../config', () => ({
    getDefaultSections: vi.fn(() => []),
}))

vi.mock('@/storefront/products/ProductTemplateRegistry', () => ({
    getBuiltinProductTemplate: vi.fn(() => null),
    getProductTemplateOptions: vi.fn(() => []),
}))

vi.mock('../../../store/authStore', () => ({
    useAuthStore: (selector: (state: { user: typeof mockCurrentUser }) => unknown) => selector({ user: mockCurrentUser }),
}))

vi.mock('../../../auth/commerceHubVendorIdentityAuthority', () => ({
    deriveCurrentStoreIdentityPresentation: vi.fn((response) => response),
    deriveCurrentVendorScopedDto: vi.fn((response) => response),
}))

vi.mock('../../../services/api', () => ({
    isRequestCanceled: vi.fn(() => false),
}))

vi.mock('../services', () => ({
    storeConfigService: {
        getStoreConfig,
        getRuntimeStoreConfig,
        updatePageLayout: vi.fn(),
        getPreviewConfig: vi.fn(),
        publishPage: vi.fn(),
        rollback: vi.fn(),
        getVersionHistory: vi.fn(),
        deleteProductTemplate: vi.fn(),
    },
}))

import { StorefrontBuilderPage } from './StorefrontBuilderPage'

describe('StorefrontBuilderPage workspace runtime', () => {
    beforeEach(() => {
        getStoreConfig.mockReset()
        getRuntimeStoreConfig.mockReset()
        getStoreConfig.mockResolvedValue({
            rowVersion: 'rv-1',
            storeName: 'Backend Store',
            storefrontTheme: 'minimal',
            pages: {
                home: { sections: [] },
            },
            productTemplates: {},
        })
        getRuntimeStoreConfig.mockResolvedValue({
            currency: 'USD',
        })
    })

    it('admits the homepage workspace without restarting the store-config request', async () => {
        render(
            <MemoryRouter initialEntries={['/storefront/builder?page=home']}>
                <StorefrontBuilderPage />
            </MemoryRouter>,
        )

        await waitFor(() => expect(screen.getByTestId('builder-canvas')).toBeInTheDocument())
        await new Promise((resolve) => setTimeout(resolve, 25))

        expect(getStoreConfig).toHaveBeenCalledTimes(1)
        expect(getRuntimeStoreConfig).toHaveBeenCalledTimes(1)
    })
})

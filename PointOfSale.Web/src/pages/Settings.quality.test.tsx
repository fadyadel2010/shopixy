import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import Settings from './Settings'
import { SettingsProvider, useSettings } from '../contexts/SettingsContext'

const mocks = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn(), success: vi.fn(), error: vi.fn() }))
vi.mock('../services/api', () => ({ default: { get: mocks.get, put: mocks.put } }))
vi.mock('../components/Toast', () => ({ useToast: () => ({ showSuccess: mocks.success, showError: mocks.error, showWarning: vi.fn() }) }))
vi.mock('../store/authStore', () => ({ useAuthStore: <T,>(selector: (state: { user: { role: string }; isAuthenticated: boolean; hasHydrated: boolean }) => T) =>
    selector({ user: { role: 'Owner' }, isAuthenticated: true, hasHydrated: true }) }))
vi.mock('../common/confirm/ConfirmDialogProvider', () => ({ useConfirmDialog: () => ({ confirm: vi.fn() }) }))
vi.mock('../auth/posPermissions', () => ({ hasPosPermission: () => true }))
vi.mock('../auth/storefrontAdminAuthority', () => ({ canManageStorefrontSettings: () => true }))
vi.mock('./StorefrontSettings', () => ({ default: () => null }))
vi.mock('../modules/storefront/informationArchitecture', () => ({ StorefrontBreadcrumbs: () => null }))
vi.mock('../hooks/useRTL', () => ({ useRTL: () => ({ dir: 'ltr', isRTL: false }) }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key: string) => key }) }))
vi.mock('../i18n/config', () => ({ default: { language: 'en' } }))

function Consumer() {
    const { settings, updateSettings } = useSettings()
    return <><output data-testid="effective-company">{settings.companyName}</output><button onClick={async () => {
        const ok = await updateSettings({ companyName: 'New company' })
        if (ok) mocks.success('context updated')
    }}>Context save</button></>
}

describe('settings persistence and effective runtime state', () => {
    let persisted: Record<string, unknown>
    beforeEach(() => {
        vi.clearAllMocks()
        persisted = { companyName: 'Original company', currencyCode: 'EGP', companyEmail: '', companyPhone: '', companyAddress: '' }
        mocks.get.mockImplementation(async (path: string) => ({ data: path === '/settings' ? { ...persisted } : { countryCode: 'EG' } }))
        mocks.put.mockImplementation(async (_: string, value: Record<string, unknown>) => { persisted = { ...value }; return { data: persisted } })
    })

    it('propagates a saved admin setting to shared runtime consumers without a full reload', async () => {
        const { container } = render(<MemoryRouter><SettingsProvider><Settings /><Consumer /></SettingsProvider></MemoryRouter>)
        await waitFor(() => expect(screen.getByTestId('effective-company')).toHaveTextContent('Original company'))
        await screen.findByRole('button', { name: 'settings.saveSettings' })
        fireEvent.change(container.querySelector('input[name="companyName"]')!, { target: { value: 'Updated company' } })
        fireEvent.click(screen.getByRole('button', { name: 'settings.saveSettings' }))
        await waitFor(() => expect(mocks.success).toHaveBeenCalledWith('settings.settingsSaved'))
        expect(screen.getByTestId('effective-company')).toHaveTextContent('Updated company')
    })

    it('does not return success when a committed context save cannot be read back', async () => {
        render(<SettingsProvider><Consumer /></SettingsProvider>)
        await waitFor(() => expect(screen.getByTestId('effective-company')).toHaveTextContent('Original company'))
        mocks.get.mockRejectedValue(new Error('Read unavailable'))
        fireEvent.click(screen.getByRole('button', { name: 'Context save' }))
        await waitFor(() => expect(mocks.put).toHaveBeenCalledTimes(1))
        await waitFor(() => expect(mocks.get).toHaveBeenCalledTimes(2))
        await new Promise(resolve => setTimeout(resolve, 0))
        expect(mocks.success).not.toHaveBeenCalled()
    })
})

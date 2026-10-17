import { fireEvent, render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
const service = vi.hoisted(() => ({ getStoreConfig: vi.fn(), activateTheme: vi.fn() }))
const toast = vi.hoisted(() => ({ showError: vi.fn(), showInfo: vi.fn(), showSuccess: vi.fn() }))
const user = { tenantId: 'tenant' }
vi.mock('../services', () => ({ storeConfigService: service }))
vi.mock('../../../components/Toast', () => ({ useToast: () => toast }))
vi.mock('../../../store/authStore', () => ({ useAuthStore: () => ({ user }) }))
vi.mock('../../../auth/commerceHubVendorIdentityAuthority', () => ({ deriveCurrentStoreIdentityPresentation: (value: unknown) => value }))
vi.mock('react-router-dom', () => ({ useNavigate: () => vi.fn() }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ i18n: { language: 'en' }, t: (key: string) => key }) }))
import { ThemeMarketplacePage } from './ThemeMarketplacePage'

it('reports failed authority as unavailable, not no active theme, and supports retry', async () => {
    service.getStoreConfig.mockRejectedValueOnce(new Error('Unavailable')).mockResolvedValueOnce({ storefrontTheme: 'modern', rowVersion: '1' })
    render(<ThemeMarketplacePage />)
    await screen.findByText('Themes unavailable')
    expect(screen.queryByText('No active Theme')).not.toBeInTheDocument()
    expect(screen.queryByText('Activate theme now')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    await screen.findByRole('heading', { name: 'Active Theme' })
    expect(service.getStoreConfig).toHaveBeenCalledTimes(2)
    expect(service.activateTheme).not.toHaveBeenCalled()
})

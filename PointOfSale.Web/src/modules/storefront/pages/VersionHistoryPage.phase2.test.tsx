import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'

const service = vi.hoisted(() => ({ getStoreConfig: vi.fn(), getVersionHistory: vi.fn(), rollback: vi.fn() }))
const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../services', () => ({ storeConfigService: service }))
vi.mock('../../../components/Toast', () => ({ useToast: () => toast }))
vi.mock('../../../store/authStore', () => ({ useAuthStore: (select: (state: unknown) => unknown) => select({ user: { tenantId: 'tenant' } }) }))
vi.mock('../../../auth/commerceHubVendorIdentityAuthority', () => ({ deriveCurrentStoreIdentityPresentation: (value: unknown) => value }))
vi.mock('react-router-dom', () => ({ useNavigate: () => vi.fn() }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ i18n: { language: 'en' }, t: (key: string) => key }) }))
import { VersionHistoryPage } from './VersionHistoryPage'

beforeEach(() => {
    vi.clearAllMocks()
    service.getStoreConfig.mockResolvedValue({ rowVersion: '10' })
    service.getVersionHistory.mockResolvedValue({ versions: [
        { version: 2, isCurrent: true, publishedAt: new Date().toISOString() },
        { version: 1, isCurrent: false, publishedAt: new Date().toISOString() },
    ] })
})

it('uses the reviewed revision and preserves a failed restore for explicit retry', async () => {
    service.rollback.mockRejectedValueOnce({ response: { status: 409 } })
    render(<VersionHistoryPage />)
    fireEvent.click(await screen.findByRole('button', { name: 'Rollback' }))
    const dialog = screen.getByRole('dialog', { name: 'Confirm Rollback' })
    fireEvent.click(within(dialog).getByRole('checkbox'))
    // Another administrator can change SQL while the confirmation stays open.
    service.getStoreConfig.mockResolvedValue({ rowVersion: '11' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Rollback Now' }))
    await waitFor(() => expect(toast.showError).toHaveBeenCalled())
    expect(service.rollback).toHaveBeenCalledWith(1, expect.objectContaining({ confirm: true }), { headers: { 'If-Match': '"10"' } })
    expect(service.getStoreConfig).toHaveBeenCalledTimes(1)
    expect(toast.showSuccess).not.toHaveBeenCalled()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
})

it('does not present malformed history as an empty or restorable list', async () => {
    service.getVersionHistory.mockResolvedValue({ versions: [{ version: 0, publishedAt: 'invalid' }] })
    render(<VersionHistoryPage />)
    await screen.findByText('StoreConfig History unavailable')
    expect(screen.queryByRole('button', { name: 'Rollback' })).not.toBeInTheDocument()
    expect(service.rollback).not.toHaveBeenCalled()
})

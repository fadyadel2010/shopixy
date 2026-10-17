import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const whatsappApi = vi.hoisted(() => ({
    getAccounts: vi.fn(),
    toggleAccount: vi.fn(),
    disconnectAccount: vi.fn(),
    deleteAccount: vi.fn(),
    getQrCode: vi.fn(),
    createAccount: vi.fn(),
    getAccountStatus: vi.fn(),
    getTemplates: vi.fn(),
    createTemplate: vi.fn(),
    updateTemplate: vi.fn(),
    deleteTemplate: vi.fn(),
    getKillSwitchStatus: vi.fn(),
    activateKillSwitch: vi.fn(),
    deactivateKillSwitch: vi.fn(),
    loading: false,
}))

const analyticsApi = vi.hoisted(() => ({
    getAnalytics: vi.fn(),
    getLogs: vi.fn(),
}))

const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))

const apiGet = vi.hoisted(() => vi.fn())

vi.mock('../hooks/useWhatsAppApi', () => ({ useWhatsAppApi: () => whatsappApi }))
vi.mock('../hooks/useWhatsAppAnalytics', () => ({ useWhatsAppAnalytics: () => analyticsApi }))
vi.mock('../../../components/Toast', () => ({ useToast: () => toast }))
vi.mock('../../../services/api', () => ({ default: { get: apiGet } }))

import WhatsAppSettings from './WhatsAppSettings'

const translations: Record<string, string> = {
    'whatsapp.tabs.accounts': 'Accounts',
    'whatsapp.tabs.templates': 'Templates',
    'whatsapp.tabs.analytics': 'Analytics',
    'whatsapp.tabs.safety': 'Safety',
    'whatsapp.tabs.logs': 'Logs',
    'whatsapp.settings.title': 'WhatsApp Settings',
    'whatsapp.settings.subtitle': 'Connect and govern messaging',
    'whatsapp.accounts.connectedAccounts': 'Connected Accounts',
    'whatsapp.accounts.orderConfirmations': 'Order Confirmations',
    'whatsapp.accounts.otpVerification': 'OTP Verification',
    'whatsapp.accounts.scanQr': 'Scan QR Code',
    'whatsapp.accounts.actions.disconnect': 'Disconnect',
    'whatsapp.accounts.confirm.enable': 'Enable this account?',
    'whatsapp.accounts.confirm.disable': 'Disable this account?',
    'whatsapp.accounts.success.enabled': 'Account enabled',
    'whatsapp.accounts.success.disabled': 'Account disabled',
    'common.refresh': 'Refresh',
    'common.enable': 'Enable',
    'common.disable': 'Disable',
    'common.delete': 'Delete',
    'common.none': 'None',
    'common.unknown': 'Unknown',
}

const translate = (key: string) => translations[key] ?? key

vi.mock('react-i18next', () => ({
    useTranslation: () => ({ t: translate, i18n: { language: 'en' } }),
}))

const account = (overrides: Record<string, unknown> = {}) => ({
    id: '11111111-1111-1111-1111-111111111111',
    displayName: 'Main Business Line',
    phoneNumber: '+201000000000',
    status: 'Connected',
    enabled: true,
    useForOrderConfirmation: true,
    useForOtpVerification: false,
    useForPaymentApproval: false,
    useForAbandonedCartRecovery: false,
    ...overrides,
})

describe('WhatsAppSettings single-account recovery authority', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        whatsappApi.loading = false
        whatsappApi.getAccounts.mockResolvedValue([account()])
        whatsappApi.getTemplates.mockResolvedValue([])
        analyticsApi.getAnalytics.mockResolvedValue(null)
        analyticsApi.getLogs.mockResolvedValue({ items: [], totalPages: 1, totalCount: 0 })
        apiGet.mockResolvedValue({ data: { countryCode: 'EG' } })
        vi.stubGlobal('confirm', vi.fn(() => true))
    })

    it.each([false, true])('does not expose a separate recovery grant when the legacy response flag is %s', async legacyFlag => {
        whatsappApi.getAccounts.mockResolvedValue([account({ useForAbandonedCartRecovery: legacyFlag })])

        render(<WhatsAppSettings />)

        expect(await screen.findByText('Main Business Line')).toBeTruthy()
        expect(screen.getByText('Order Confirmations')).toBeTruthy()
        expect(screen.getByText('OTP Verification')).toBeTruthy()
        expect(screen.queryByText('Abandoned cart recovery authority')).toBeNull()
        expect(screen.queryByRole('button', { name: /recovery/i })).toBeNull()
    })

    it('uses the account enabled state as the governing administration control', async () => {
        whatsappApi.toggleAccount.mockResolvedValue(true)
        whatsappApi.getAccounts
            .mockResolvedValueOnce([account()])
            .mockResolvedValueOnce([account({ enabled: false })])

        render(<WhatsAppSettings />)

        fireEvent.click(await screen.findByRole('button', { name: 'Disable' }))

        await waitFor(() => expect(whatsappApi.toggleAccount).toHaveBeenCalledWith(
            '11111111-1111-1111-1111-111111111111',
            false
        ))
        expect(toast.showSuccess).toHaveBeenCalledWith('Account disabled')
    })

    it('does not present a failed account read as an empty configured account list', async () => {
        whatsappApi.getAccounts.mockRejectedValue(new Error('Account authority unavailable'))
        render(<WhatsAppSettings />)
        expect(await screen.findByRole('alert')).toBeTruthy()
        expect(screen.queryByText('whatsapp.accounts.emptyTitle')).toBeNull()
        expect(screen.queryByText('whatsapp.accounts.actions.addAccount')).toBeNull()
    })

    it('allows disabling an enabled but disconnected account', async () => {
        whatsappApi.getAccounts.mockResolvedValue([account({ status: 'Disconnected' })])
        render(<WhatsAppSettings />)
        const button = await screen.findByRole('button', { name: 'Disable' })
        expect((button as HTMLButtonElement).disabled).toBe(false)
    })

    it('does not declare messaging operational when safety authority fails', async () => {
        whatsappApi.getKillSwitchStatus.mockRejectedValue(new Error('unavailable'))
        render(<WhatsAppSettings />)
        fireEvent.click(screen.getByRole('button', { name: 'Safety' }))
        expect(await screen.findByRole('alert')).toBeTruthy()
        expect(screen.queryByText(/whatsapp.safety.operational/)).toBeNull()
    })
})

import { render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const api = vi.hoisted(() => ({
    get: vi.fn(),
    put: vi.fn(),
}))

const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))

vi.mock('../services/api', () => ({ default: api }))
vi.mock('../components/Toast', () => ({ useToast: () => toast }))

const translations: Record<string, string> = {
    'AbandonedCartRecovery.header.title': 'Smart Cart Recovery',
    'AbandonedCartRecovery.header.subtitle': 'Recover abandoned carts over WhatsApp',
    'AbandonedCartRecovery.actions.refresh': 'Refresh',
    'AbandonedCartRecovery.status.title': 'Operational readiness',
    'AbandonedCartRecovery.status.timeZone': 'Sending hours are interpreted in {{timeZone}}.',
    'AbandonedCartRecovery.status.configurationSaved': 'Configuration loaded',
    'AbandonedCartRecovery.status.configurationNotSaved': 'Defaults loaded',
    'AbandonedCartRecovery.status.accountEnabled': 'Account enabled',
    'AbandonedCartRecovery.status.accountConnected': 'Account connected',
    'AbandonedCartRecovery.status.recoveryEnabled': 'Recovery enabled',
    'AbandonedCartRecovery.status.nativeRuntime': 'Native worker healthy',
    'AbandonedCartRecovery.status.operational': 'Recovery operational',
    'AbandonedCartRecovery.connection.requiredTitle': 'Recovery setup required',
    'AbandonedCartRecovery.connection.settingsLink': 'WhatsApp settings',
    'AbandonedCartRecovery.connection.enableLink': 'Enable recovery below',
    'AbandonedCartRecovery.readiness.recovery_disabled': 'Recovery is saved as disabled. No recovery workflow can start.',
    'AbandonedCartRecovery.readiness.whatsapp_disconnected': 'The WhatsApp account is disconnected.',
    'AbandonedCartRecovery.readiness.native_database_unavailable': 'The native recovery database is unavailable.',
    'AbandonedCartRecovery.readiness.native_dispatcher_unavailable': 'The audited WhatsApp dispatcher is unavailable.',
    'AbandonedCartRecovery.readiness.unavailable': 'Recovery readiness could not be verified.',
    'AbandonedCartRecovery.stats.title': 'Recovery statistics',
    'AbandonedCartRecovery.stats.abandonedCarts': 'Abandoned carts',
    'AbandonedCartRecovery.stats.messagesSent': 'Messages sent',
    'AbandonedCartRecovery.stats.recovered': 'Recovered',
    'AbandonedCartRecovery.stats.recoveryRate': 'Recovery rate',
    'AbandonedCartRecovery.settings.title': 'Recovery settings',
    'AbandonedCartRecovery.settings.subtitle': 'Configure when and how recovery messages are sent.',
    'AbandonedCartRecovery.settings.enableTitle': 'Enable smart cart recovery',
    'AbandonedCartRecovery.settings.enableDescription': 'Send recovery messages to customers who abandon their cart.',
    'AbandonedCartRecovery.settings.save': 'Save',
    'AbandonedCartRecovery.settings.saving': 'Saving',
}

vi.mock('react-i18next', () => ({
    useTranslation: () => ({ t: (key: string) => translations[key] ?? key }),
}))

import AbandonedCartRecovery from './AbandonedCartRecovery'

const SETTINGS = {
    enableAbandonedRecovery: false,
    firstMessageDelayMinutes: 60,
    secondMessageDelayMinutes: 1440,
    workingHoursFrom: '09:00',
    workingHoursTo: '21:00',
    timeZoneId: 'Africa/Cairo',
}

const eligibility = (overrides: Record<string, unknown> = {}) => ({
    isEligible: false,
    hasWhatsAppAccount: true,
    whatsAppAccountEnabled: true,
    whatsAppAccountConnected: true,
    automationEnabled: false,
    nativeSchedulerOperational: true,
    nativeDispatcherOperational: true,
    ineligibilityCode: 'recovery_disabled',
    ...overrides,
})

const renderPage = async (eligibilityPayload: Record<string, unknown>) => {
    api.get.mockImplementation((url: string) => {
        if (url.endsWith('/settings')) return Promise.resolve({ data: SETTINGS })
        if (url.endsWith('/eligibility')) return Promise.resolve({ data: eligibilityPayload })
        return Promise.resolve({ data: { totalAbandoned: 0, messagesSent: 0, recovered: 0, recoveryRate: 0 } })
    })

    render(<AbandonedCartRecovery />)
    await waitFor(() => expect(screen.queryByText('Smart Cart Recovery')).not.toBeNull())
    await waitFor(() => expect(screen.queryByRole('status')).not.toBeNull())
}

describe('AbandonedCartRecovery readiness reporting', () => {
    beforeEach(() => {
        vi.clearAllMocks()
    })

    it('honors the authoritative disabled-automation blocker before runtime diagnostics', async () => {
        await renderPage(eligibility({ nativeSchedulerOperational: false }))

        expect(screen.getByText('Recovery is saved as disabled. No recovery workflow can start.')).toBeTruthy()
        expect(screen.queryByText('WhatsApp settings')).toBeNull()

        const enableLink = screen.getByText('Enable recovery below')
        expect(enableLink.getAttribute('href')).toBe('#enable-recovery')
        expect(document.getElementById('enable-recovery')).not.toBeNull()
    })

    it('offers the WhatsApp settings link for a disconnected account', async () => {
        await renderPage(eligibility({
            whatsAppAccountConnected: false,
            ineligibilityCode: 'whatsapp_disconnected',
        }))

        expect(screen.getByText('The WhatsApp account is disconnected.')).toBeTruthy()
        expect(screen.getByText('WhatsApp settings').getAttribute('href')).toBe('/ecommerce/whatsapp')
        expect(screen.queryByText('Enable recovery below')).toBeNull()
    })

    it('reports the backend native-runtime failure once tenant prerequisites pass', async () => {
        await renderPage(eligibility({
            automationEnabled: true,
            nativeSchedulerOperational: false,
            ineligibilityCode: 'native_database_unavailable',
        }))

        expect(screen.getByText('The native recovery database is unavailable.')).toBeTruthy()
        expect(screen.queryByText('WhatsApp settings')).toBeNull()
    })

    it('falls back to a fail-closed derived blocker when the backend omits a supported code', async () => {
        await renderPage(eligibility({
            automationEnabled: true,
            nativeDispatcherOperational: false,
            ineligibilityCode: null,
        }))

        expect(screen.getByText('The audited WhatsApp dispatcher is unavailable.')).toBeTruthy()
    })
})

import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import ShippingAdministrationWorkspace from './ShippingAdministrationWorkspace'
import {
    admitCarrierAccounts,
    admitSupportedCarriers,
    createShippingAdministrationSummary,
    fetchCarrierAccounts,
    fetchSupportedCarriers,
} from './shippingAdministrationApi'
import PosRoleRouteGuard from '../../components/PosRoleRouteGuard'
import { useAuthStore } from '../../store/authStore'
import api from '../../services/api'

vi.mock('../../services/api', () => ({
    default: {
        get: vi.fn(),
        post: vi.fn(),
        put: vi.fn(),
    },
    getSafeApiErrorMessage: vi.fn((error, fallback) => error?.message || fallback),
}))

const supportedCarrierPayload = [
    {
        carrierCode: 'aramex',
        displayName: 'Aramex',
        serviceLevels: [
            {
                code: 'standard',
                name: 'Standard',
                description: 'Backend service level',
                typicalDaysMin: 1,
                typicalDaysMax: 3,
            },
        ],
    },
    {
        carrierCode: 'bosta',
        displayName: 'Bosta',
        serviceLevels: [
            {
                code: 'standard',
                name: 'Standard',
                description: 'Backend service level',
                typicalDaysMin: 1,
                typicalDaysMax: 3,
            },
        ],
    },
    {
        carrierCode: 'dhl',
        displayName: 'DHL',
        serviceLevels: [
            {
                code: 'standard',
                name: 'Standard',
                description: 'Backend service level',
                typicalDaysMin: 1,
                typicalDaysMax: 3,
            },
        ],
    },
    {
        carrierCode: 'fedex',
        displayName: 'FedEx',
        serviceLevels: [
            {
                code: 'standard',
                name: 'Standard',
                description: 'Backend service level',
                typicalDaysMin: 1,
                typicalDaysMax: 3,
            },
        ],
    },
]

const carrierAccountPayload = [
    {
        id: '11111111-1111-4111-8111-111111111111',
        carrierCode: 'fedex',
        environment: 'Sandbox',
        activeCredentialReferenceId: '22222222-2222-4222-8222-222222222222',
        state: 'Active',
        operationalState: 'Healthy',
        healthState: 'Healthy',
        healthReason: 'Backend health reason',
        lastHealthEvaluatedAtUtc: '2026-07-08T10:00:00Z',
        activatedAtUtc: '2026-07-08T09:00:00Z',
        suspendedAtUtc: null,
        disabledAtUtc: null,
        retiredAtUtc: null,
        credentialReferences: [
            {
                id: '22222222-2222-4222-8222-222222222222',
                maskedFingerprint: '***1234',
                state: 'Active',
                expiresAtUtc: '2026-12-31T00:00:00Z',
            },
        ],
        warehouseAssignments: [
            {
                warehouseId: '33333333-3333-4333-8333-333333333333',
                isEnabled: true,
                originReadinessPublished: true,
                readinessEvidenceReference: 'origin-ready',
            },
        ],
        policies: [
            {
                scope: 'International',
                mode: 'Enabled',
                customsReadinessPublished: false,
                readinessEvidenceReference: 'Customs Readiness Missing',
            },
        ],
        serviceLevels: [
            {
                code: 'standard',
                name: 'Standard',
                description: 'Backend service level',
                typicalDaysMin: 1,
                typicalDaysMax: 3,
                isEnabled: true,
            },
        ],
        lastConnectionTest: {
            id: '44444444-4444-4444-8444-444444444444',
            environment: 'Sandbox',
            status: 'Succeeded',
            failureCode: null,
            failureReason: null,
            testedAtUtc: '2026-07-08T09:30:00Z',
        },
    },
]

const auditPayload = [
    {
        id: '55555555-5555-4555-8555-555555555555',
        carrierAccountId: '11111111-1111-4111-8111-111111111111',
        action: 'Activated',
        actorId: 'pos-admin',
        correlationId: 'corr-1',
        previousState: 'ReadyForActivation',
        nextState: 'Active',
        reason: 'Backend audit reason',
        occurredAtUtc: '2026-07-08T10:05:00Z',
    },
]

function mockWorkspaceReads() {
    api.get.mockImplementation((url) => {
        if (url === '/admin/shipping/supported-carriers') {
            return Promise.resolve({ data: supportedCarrierPayload })
        }
        if (url === '/admin/shipping/carrier-accounts') {
            return Promise.resolve({ data: carrierAccountPayload })
        }
        if (url === '/admin/shipping/audit') {
            return Promise.resolve({ data: auditPayload })
        }
        return Promise.resolve({ data: [] })
    })
}

function mockSuccessfulMutations() {
    api.post.mockResolvedValue({ data: { succeeded: true } })
    api.put.mockResolvedValue({ data: { succeeded: true } })
}

describe('ShippingAdministrationWorkspace', () => {
    afterEach(() => {
        vi.clearAllMocks()
        useAuthStore.setState({
            user: null,
            isAuthenticated: false,
            hasHydrated: true,
        })
    })

    it('renders the certified workspace and switches to carrier accounts with backend-supported carriers only', async () => {
        mockWorkspaceReads()

        render(<ShippingAdministrationWorkspace />)

        expect(await screen.findByText('Shipping Administration')).toBeInTheDocument()
        expect(screen.getByText('Configured Carriers')).toBeInTheDocument()

        fireEvent.click(screen.getByRole('button', { name: 'Carrier Accounts' }))

        await waitFor(() => expect(screen.getAllByText('FedEx')).toHaveLength(2))
        expect(screen.getByRole('option', { name: 'Bosta' })).toBeInTheDocument()
        expect(screen.getByRole('option', { name: 'Aramex' })).toBeInTheDocument()
        expect(screen.getByRole('option', { name: 'DHL' })).toBeInTheDocument()
        expect(screen.getByRole('option', { name: 'FedEx' })).toBeInTheDocument()
        expect(screen.getByText('Enabled Services')).toBeInTheDocument()
        expect(screen.getByText('Standard')).toBeInTheDocument()
    })

    it('uses certified administration API endpoints for provider and account reads', async () => {
        mockWorkspaceReads()

        await fetchSupportedCarriers()
        await fetchCarrierAccounts()

        expect(api.get).toHaveBeenCalledWith('/admin/shipping/supported-carriers', expect.objectContaining({
            headers: expect.objectContaining({ 'Cache-Control': 'no-store' }),
        }))
        expect(api.get).toHaveBeenCalledWith('/admin/shipping/carrier-accounts', expect.objectContaining({
            headers: expect.objectContaining({ Pragma: 'no-cache' }),
        }))
    })

    it('allows only Owner Admin Manager presentation roles through the POS route guard', () => {
        const renderGuard = (role) => {
            useAuthStore.setState({
                user: { id: 'user', tenantId: 'tenant', role },
                isAuthenticated: true,
                hasHydrated: true,
            })

            return render(
                <MemoryRouter initialEntries={['/admin/shipping/setup']}>
                    <Routes>
                        <Route path="/" element={<div>Dashboard</div>} />
                        <Route element={<PosRoleRouteGuard roles={['Owner', 'Admin', 'Manager']} />}>
                            <Route path="/admin/shipping/setup" element={<div>Shipping Workspace</div>} />
                        </Route>
                    </Routes>
                </MemoryRouter>,
            )
        }

        const owner = renderGuard('Owner')
        expect(screen.getByText('Shipping Workspace')).toBeInTheDocument()
        owner.unmount()

        const manager = renderGuard('Manager')
        expect(screen.getByText('Shipping Workspace')).toBeInTheDocument()
        manager.unmount()

        const cashier = renderGuard('Cashier')
        expect(screen.getByText('Dashboard')).toBeInTheDocument()
        expect(screen.queryByText('Shipping Workspace')).not.toBeInTheDocument()
        cashier.unmount()
    })

    it('renders health reasons and append-only audit without editing controls', async () => {
        mockWorkspaceReads()

        render(<ShippingAdministrationWorkspace initialTab="health" />)

        expect(await screen.findByText('Backend health reason')).toBeInTheDocument()
        expect(screen.getByText('Customs Readiness Missing')).toBeInTheDocument()

        fireEvent.click(screen.getByRole('button', { name: 'Audit' }))

        expect(await screen.findByText('Backend audit reason')).toBeInTheDocument()
        expect(screen.queryByText('Delete')).not.toBeInTheDocument()
        expect(screen.queryByText('Edit')).not.toBeInTheDocument()
    })

    it('uses certified administration APIs for account management actions', async () => {
        mockWorkspaceReads()
        mockSuccessfulMutations()

        render(<ShippingAdministrationWorkspace initialTab="carrier-accounts" />)

        await waitFor(() => expect(screen.getAllByText('FedEx').length).toBeGreaterThan(0))

        fireEvent.change(screen.getByDisplayValue('Sandbox'), { target: { value: 'Production' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save' }))

        await waitFor(() => expect(api.put).toHaveBeenCalledWith(
            '/admin/shipping/carrier-accounts/11111111-1111-4111-8111-111111111111/environment',
            { environment: 'Production' },
        ))

        fireEvent.click(screen.getByRole('button', { name: 'Evaluate health' }))

        await waitFor(() => expect(api.post).toHaveBeenCalledWith(
            '/admin/shipping/carrier-accounts/11111111-1111-4111-8111-111111111111/health/evaluate',
            {},
        ))
    })

    it('keeps summary presentation derived from DTO state without activation gate duplication', () => {
        const accounts = admitCarrierAccounts(carrierAccountPayload)
        const summary = createShippingAdministrationSummary(accounts)

        expect(summary).toMatchObject({
            configuredCarriers: 1,
            activeCarriers: 1,
            blockedCarriers: 0,
            connectionWarnings: 0,
            activationBlockers: 0,
        })
    })

    it('does not hardcode provider lists or force unavailable providers into the new frontend', () => {
        const moduleRoot = resolve(process.cwd(), 'src/modules/shipping-administration')
        const workspace = readFileSync(resolve(moduleRoot, 'ShippingAdministrationWorkspace.jsx'), 'utf8')
        const apiBoundary = readFileSync(resolve(moduleRoot, 'shippingAdministrationApi.js'), 'utf8')
        const methodsTab = readFileSync(resolve(moduleRoot, 'components/ShippingMethodsTab.jsx'), 'utf8')
        const combined = `${workspace}\n${apiBoundary}\n${methodsTab}`

        expect(combined).not.toMatch(new RegExp(`fed${'ex'}`, 'i'))
        const unavailableProviders = ['bos' + 'ta', 'ara' + 'mex', 'myl' + 'erz', 'd' + 'hl']
        unavailableProviders.forEach((provider) => {
            expect(combined.toLowerCase()).not.toContain(provider)
        })
        expect(admitSupportedCarriers(supportedCarrierPayload).map((carrier) => carrier.displayName)).toEqual(['Aramex', 'Bosta', 'DHL', 'FedEx'])
    })
})

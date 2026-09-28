import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import ShippingAdministrationWorkspace from './ShippingAdministrationWorkspace'
import {
    admitCarrierAccounts,
    admitSupportedCarriers,
    admitWarehouses,
    createShippingAdministrationSummary,
    fetchCarrierAccounts,
    fetchSupportedCarriers,
    fetchWarehouses,
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
        integrationReadiness: {
            enabled: true,
            canExecute: true,
            environment: 'Sandbox',
            productionCapability: 'Partial',
            blockers: [],
            originReadinessRequired: false,
            originReadinessSatisfied: true,
            credentialAvailability: 'Available',
        },
    },
]

const warehousePayload = [
    {
        id: '33333333-3333-4333-8333-333333333333',
        name: 'Alexandria Warehouse',
        code: 'ALEX',
        address: 'Alexandria',
        phone: '+20',
        isActive: true,
        isDefault: true,
    },
    {
        id: '66666666-6666-4666-8666-666666666666',
        name: 'مخزن القاهرة',
        code: null,
        address: 'Cairo',
        phone: null,
        isActive: true,
        isDefault: false,
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
        if (url === '/warehouses') {
            return Promise.resolve({ data: warehousePayload })
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

        fireEvent.click(screen.getByRole('tab', { name: 'Carrier Accounts' }))

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
        await fetchWarehouses()

        expect(api.get).toHaveBeenCalledWith('/admin/shipping/supported-carriers', expect.objectContaining({
            headers: expect.objectContaining({ 'Cache-Control': 'no-store' }),
        }))
        expect(api.get).toHaveBeenCalledWith('/admin/shipping/carrier-accounts', expect.objectContaining({
            headers: expect.objectContaining({ Pragma: 'no-cache' }),
        }))
        expect(api.get).toHaveBeenCalledWith('/warehouses', expect.objectContaining({
            headers: expect.objectContaining({ 'Cache-Control': 'no-store' }),
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

    it('renders provider readiness and append-only audit without editing controls', async () => {
        mockWorkspaceReads()

        render(<ShippingAdministrationWorkspace initialTab="health" />)

        expect(await screen.findByText('Provider Readiness States')).toBeInTheDocument()
        expect(screen.getByText('Credential availability: Available')).toBeInTheDocument()

        fireEvent.click(screen.getByRole('tab', { name: 'Audit' }))

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

    it('does not expose carrier-account warehouse assignment as merchant-facing shipping authority', async () => {
        mockWorkspaceReads()
        mockSuccessfulMutations()

        render(<ShippingAdministrationWorkspace initialTab="carrier-accounts" />)

        await waitFor(() => expect(screen.getAllByText('FedEx').length).toBeGreaterThan(0))
        expect(screen.queryByLabelText('Warehouse')).not.toBeInTheDocument()
        expect(screen.queryByPlaceholderText('Warehouse ID')).not.toBeInTheDocument()
        expect(screen.queryByText('Warehouse Assignment')).not.toBeInTheDocument()
        expect(screen.queryByRole('option', { name: 'Alexandria Warehouse (ALEX)' })).not.toBeInTheDocument()
        expect(screen.queryByText('No active warehouses available')).not.toBeInTheDocument()
        expect(screen.queryByText('Origin readiness published')).not.toBeInTheDocument()
        expect(screen.queryByPlaceholderText('Readiness evidence reference')).not.toBeInTheDocument()
        expect(api.put).not.toHaveBeenCalledWith(
            expect.stringContaining('/warehouse-assignments/'),
            expect.anything(),
        )
    })

    it('keeps legacy warehouse assignments out of the merchant-facing workspace tabs', async () => {
        mockWorkspaceReads()

        render(<ShippingAdministrationWorkspace />)

        expect(await screen.findByText('Shipping Administration')).toBeInTheDocument()
        expect(screen.queryByRole('button', { name: 'Warehouses' })).not.toBeInTheDocument()
        expect(screen.queryByRole('button', { name: 'Policies' })).not.toBeInTheDocument()
    })

    it('hides legacy manual carrier-account rows from carrier account cards', async () => {
        api.get.mockImplementation((url) => {
            if (url === '/admin/shipping/supported-carriers') return Promise.resolve({ data: supportedCarrierPayload })
            if (url === '/admin/shipping/carrier-accounts') return Promise.resolve({
                data: [
                    ...carrierAccountPayload,
                    {
                        ...carrierAccountPayload[0],
                        id: '77777777-7777-4777-8777-777777777777',
                        carrierCode: 'manual',
                        state: 'Draft',
                        operationalState: 'NotReady',
                        healthState: 'Blocked',
                        integrationReadiness: {
                            enabled: true,
                            canExecute: true,
                            environment: null,
                            productionCapability: 'ProductionReady',
                            blockers: [],
                            originReadinessRequired: false,
                            originReadinessSatisfied: true,
                            credentialAvailability: 'NotRequired',
                        },
                    },
                ],
            })
            if (url === '/warehouses') return Promise.resolve({ data: [] })
            if (url === '/admin/shipping/audit') return Promise.resolve({ data: auditPayload })
            return Promise.resolve({ data: [] })
        })

        render(<ShippingAdministrationWorkspace initialTab="carrier-accounts" />)

        await waitFor(() => expect(screen.getAllByText('FedEx').length).toBeGreaterThan(0))
        expect(screen.queryByText('Manual Fulfillment')).not.toBeInTheDocument()
        expect(screen.queryByText('Warehouse Missing')).not.toBeInTheDocument()
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

    it('keeps Shipping Methods carrier identity backed by the supported-carrier API', () => {
        const moduleRoot = resolve(process.cwd(), 'src/modules/shipping-administration')
        const methodsTab = readFileSync(resolve(moduleRoot, 'components/ShippingMethodsTab.jsx'), 'utf8')

        expect(methodsTab).toContain('fetchSupportedCarriers')
        expect(methodsTab).toContain('carrierCode: normalizedCarrierCode')
        expect(methodsTab).toContain('serviceLevel: normalizedServiceLevel')
        expect(methodsTab).toContain('getCarrierDisplayName(supportedCarriers')
        expect(methodsTab).not.toContain('placeholder="Carrier display name"')
        expect(methodsTab).not.toContain('value={formData.carrierName}')
    })

    it('uses backend metadata to keep Manual Fulfillment out of carrier-account creation', () => {
        const moduleRoot = resolve(process.cwd(), 'src/modules/shipping-administration')
        const workspace = readFileSync(resolve(moduleRoot, 'ShippingAdministrationWorkspace.jsx'), 'utf8')
        const card = readFileSync(resolve(moduleRoot, 'components/CarrierAccountCard.jsx'), 'utf8')

        expect(workspace).toContain('accountRequired !== false')
        expect(card).toContain('accountRequired')
        expect(card).toContain("t('shippingAdministration.account.manualExplanation')")
        expect(admitSupportedCarriers([
            {
                carrierCode: 'manual',
                displayName: 'Manual Fulfillment',
                accountRequired: false,
                readinessRequired: false,
                isExternal: false,
                serviceLevels: [],
            },
        ])[0]).toMatchObject({
            carrierCode: 'manual',
            accountRequired: false,
            readinessRequired: false,
            isExternal: false,
        })
    })

    it('admits canonical warehouse API metadata for multilingual selector labels', () => {
        const warehouses = admitWarehouses(warehousePayload)

        expect(warehouses).toEqual([
            expect.objectContaining({
                id: '33333333-3333-4333-8333-333333333333',
                name: 'Alexandria Warehouse',
                code: 'ALEX',
                isActive: true,
                isDefault: true,
            }),
            expect.objectContaining({
                id: '66666666-6666-4666-8666-666666666666',
                name: 'مخزن القاهرة',
            }),
        ])
    })
})

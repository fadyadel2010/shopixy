import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import '../../i18n/config'
import ShippingAdministrationWorkspace from './ShippingAdministrationWorkspace'
import CarrierAccountCard from './components/CarrierAccountCard'
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
const api = vi.hoisted(() => ({
    get: vi.fn<(url: string, config?: unknown) => Promise<{ data: unknown }>>(),
    post: vi.fn<(url: string, data?: unknown) => Promise<{ data: unknown }>>(),
    put: vi.fn<(url: string, data?: unknown) => Promise<{ data: unknown }>>(),
}))

vi.mock('../../services/api', () => ({
    default: api,
    getSafeApiErrorMessage: vi.fn((error, fallback) => error?.message || fallback),
}))

vi.mock('../../contexts/SettingsContext', () => ({
    useSettings: () => ({
        settings: { currencySymbol: 'EGP' },
        formatCurrency: (value: number) => `${value} EGP`,
    }),
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
        const renderGuard = (role: string) => {
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

    it('saves Bosta operational settings separately from authentication and admits the canonical save contract', async () => {
        api.post.mockResolvedValue({ data: { succeeded: true, blockers: [] } })
        const definition = admitSupportedCarriers([{
            carrierCode: 'bosta',
            displayName: 'Bosta',
            isExternal: true,
            accountRequired: true,
            readinessRequired: true,
            productionCapability: 'Partial',
            environmentSupported: true,
            connectionTestSupported: true,
            connectionTestMode: 'Remote',
            requiresGeographyMapping: true,
            supportsWebhooks: true,
            webhookTrustModes: ['shared_secret'],
            credentialFields: [
                { key: 'apiKey', label: 'API key', isSecret: true, required: true, target: 'ApiKey' },
                { key: 'webhookSecret', label: 'Webhook secret', isSecret: true, required: false, target: 'WebhookSecret' },
                { key: 'codCollectionCurrency', label: 'COD currency', isSecret: false, required: false, target: 'ExtraConfig.codCollectionCurrency' },
                { key: 'businessLocationId', label: 'Business location', isSecret: false, required: false, target: 'ExtraConfig.businessLocationId' },
            ],
            serviceLevels: [{ code: 'standard', name: 'Standard', typicalDaysMin: 2, typicalDaysMax: 4 }],
        }])[0]
        const account = admitCarrierAccounts([{
            ...carrierAccountPayload[0],
            carrierCode: 'bosta',
            state: 'Draft',
            operationalState: 'NotReady',
            healthState: 'Blocked',
            lastConnectionTest: null,
            integrationReadiness: {
                enabled: false,
                canExecute: false,
                environment: 'Sandbox',
                productionCapability: 'Partial',
                blockers: ['CARRIER_CONNECTION_NOT_VERIFIED'],
                originReadinessRequired: false,
                originReadinessSatisfied: true,
                credentialAvailability: 'Missing',
            },
        }])[0]

        render(<CarrierAccountCard account={account} supportedCarriers={[definition]} onMutated={vi.fn().mockResolvedValue(undefined)} />)

        fireEvent.change(screen.getByLabelText(/API key/i), { target: { value: 'not-retained-test-key' } })
        fireEvent.change(screen.getByLabelText(/Webhook authorization value/i), { target: { value: 'not-retained-test-secret' } })
        fireEvent.change(screen.getByLabelText(/COD collection currency/i), { target: { value: 'EGP' } })
        fireEvent.change(screen.getByLabelText(/Business pickup-location ID/i), { target: { value: 'business-location-1' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save' }))

        await waitFor(() => expect(api.post).toHaveBeenCalledWith(
            '/admin/shipping/carrier-accounts/11111111-1111-4111-8111-111111111111/connection-tests',
            expect.objectContaining({
                apiKey: 'not-retained-test-key',
                webhookSecret: 'not-retained-test-secret',
                extraConfig: { codCollectionCurrency: 'EGP', businessLocationId: 'business-location-1' },
                useSandbox: true,
                saveOnly: true,
            }),
        ))
        expect(screen.queryByText(/Connection verification failed/i)).not.toBeInTheDocument()
    })

    it('saves an unavailable carrier draft without connection verification and clears secret inputs', async () => {
        api.post.mockResolvedValue({ data: { succeeded: true, blockers: ['CREDENTIALS_SAVED_NOT_CONNECTION_VERIFIED'] } })
        const definition = admitSupportedCarriers([{
            carrierCode: 'mylerz', displayName: 'Mylerz', isExternal: true, accountRequired: true, readinessRequired: true,
            productionCapability: 'Unavailable', environmentSupported: false, connectionTestSupported: false, connectionTestMode: 'Unsupported',
            credentialFields: [
                { key: 'apiKey', label: 'Username', isSecret: true, required: true, target: 'ApiKey' },
                { key: 'apiSecret', label: 'Password', isSecret: true, required: true, target: 'ApiSecret' },
            ], serviceLevels: [],
        }])[0]
        const account = admitCarrierAccounts([{
            ...carrierAccountPayload[0], carrierCode: 'mylerz', state: 'Draft', lastConnectionTest: null,
            integrationReadiness: { ...carrierAccountPayload[0].integrationReadiness, enabled: false, canExecute: false,
                productionCapability: 'Unavailable', credentialAvailability: 'Missing', blockers: ['MYLERZ_ACCOUNT_CONTRACT_QUALIFICATION_REQUIRED'] },
        }])[0]
        const onMutated = vi.fn().mockResolvedValue(undefined)
        render(<CarrierAccountCard account={account} supportedCarriers={[definition]} onMutated={onMutated} />)
        const key = screen.getByLabelText(/Mylerz username/i)
        const secret = screen.getByLabelText(/Mylerz password/i)
        expect(key).toHaveAttribute('type', 'password'); expect(secret).toHaveAttribute('type', 'password')
        fireEvent.change(key, { target: { value: 'isolated-user' } })
        fireEvent.change(secret, { target: { value: 'isolated-password' } })
        expect(screen.getByRole('button', { name: 'Test Connection' })).toBeDisabled()
        expect(screen.getByRole('button', { name: /^Activate$/i })).toBeDisabled()
        fireEvent.click(screen.getByRole('button', { name: 'Save' }))
        await waitFor(() => expect(api.post).toHaveBeenCalledTimes(1))
        expect(api.post).toHaveBeenCalledWith(expect.stringContaining('/connection-tests'), expect.objectContaining({
            apiKey: 'isolated-user', apiSecret: 'isolated-password', saveOnly: true, useSandbox: false,
        }))
        await waitFor(() => expect(onMutated).toHaveBeenCalledTimes(1))
        expect(key).toHaveValue(''); expect(secret).toHaveValue('')
        expect(screen.queryByText('Ready for checkout')).not.toBeInTheDocument()
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

    it('does not present a successful DHL local credential check as verified provider connectivity', () => {
        const definition = admitSupportedCarriers([{
            ...supportedCarrierPayload[2], connectionTestSupported: true, connectionTestMode: 'LocalOnly',
            productionCapability: 'Partial', accountRequired: true, readinessRequired: true,
        }])[0]
        const account = admitCarrierAccounts([{
            ...carrierAccountPayload[0], carrierCode: 'dhl',
            integrationReadiness: { ...carrierAccountPayload[0].integrationReadiness, canExecute: false,
                blockers: ['DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED'] },
        }])[0]

        render(<CarrierAccountCard account={account} supportedCarriers={[definition]} onMutated={vi.fn()} />)

        expect(screen.getByRole('button', { name: 'Test Connection' })).toBeDisabled()
        expect(screen.queryByText('Connection verified')).not.toBeInTheDocument()
        expect(screen.queryByText('Authenticated connection succeeded')).not.toBeInTheDocument()
    })

    it('does not turn unavailable Mylerz accountRequired=false metadata into Manual readiness', () => {
        const definition = admitSupportedCarriers([{
            carrierCode: 'mylerz', displayName: 'Mylerz', isExternal: true,
            accountRequired: false, readinessRequired: false, productionCapability: 'Unavailable',
            connectionTestSupported: false, connectionTestMode: 'Unsupported', environmentSupported: false,
            credentialFields: [], serviceLevels: [],
        }])[0]
        const account = admitCarrierAccounts([{
            ...carrierAccountPayload[0], carrierCode: 'mylerz', serviceLevels: [],
            integrationReadiness: { ...carrierAccountPayload[0].integrationReadiness,
                canExecute: false, productionCapability: 'Unavailable', credentialAvailability: 'Unverified',
                blockers: ['MYLERZ_OFFICIAL_CONTRACT_UNAVAILABLE'] },
        }])[0]
        render(<CarrierAccountCard account={account} supportedCarriers={[definition]} onMutated={vi.fn()} />)
        expect(screen.queryByText('Ready for checkout')).not.toBeInTheDocument()
        expect(screen.queryByText('Connection verified')).not.toBeInTheDocument()
        expect(screen.queryByText(/Manual Fulfillment/)).not.toBeInTheDocument()
        expect(screen.queryByRole('button', { name: /Activate|Save.*Validate/i })).not.toBeInTheDocument()
        expect(screen.getAllByText('Not configured').length).toBeGreaterThan(0)
        expect(api.post).not.toHaveBeenCalled()
    })

    it('excludes Mylerz from account creation and blocks active methods and unsupported drafts', async () => {
        api.get.mockImplementation((url: string) => {
            if (url === '/admin/shipping/supported-carriers') return Promise.resolve({ data: [{
                carrierCode: 'mylerz', displayName: 'Mylerz', isExternal: true, accountRequired: false,
                readinessRequired: false, productionCapability: 'Unavailable', connectionTestSupported: false,
                connectionTestMode: 'Unsupported', credentialFields: [], serviceLevels: [],
            }] })
            if (url === '/warehouses') return Promise.resolve({ data: warehousePayload })
            return Promise.resolve({ data: [] })
        })
        render(<ShippingAdministrationWorkspace initialTab="carrier-accounts" />)
        await screen.findByText('No carrier accounts configured')
        expect(screen.queryByRole('option', { name: 'Mylerz' })).not.toBeInTheDocument()
        fireEvent.click(screen.getByRole('tab', { name: 'Shipping Methods' }))
        fireEvent.click(await screen.findByRole('button', { name: /Add Shipping Method/i }))
        expect(screen.getByRole('option', { name: /Mylerz.*Not ready/i })).toBeDisabled()
        fireEvent.click(screen.getByRole('checkbox', { name: 'Active' }))
        fireEvent.change(screen.getByRole('combobox', { name: 'Carrier' }), { target: { value: 'mylerz' } })
        expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
        expect(api.post).not.toHaveBeenCalled()
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

    it.each([['aramex', 'Aramex', 'standard'], ['dhl', 'DHL', 'express']])(
      'blocks active %s methods but allows inactive drafts without executable backend readiness', async (carrierCode, displayName, serviceLevel) => {
        api.get.mockImplementation((url) => {
            if (url === '/admin/shipping/supported-carriers') return Promise.resolve({ data: [
                {
                    carrierCode: 'manual', displayName: 'Manual Fulfillment', isExternal: false,
                    accountRequired: false, readinessRequired: false, productionCapability: 'ProductionReady',
                    serviceLevels: [{ code: 'standard', name: 'Standard', typicalDaysMin: 0, typicalDaysMax: 0 }],
                },
                {
                    carrierCode, displayName, isExternal: true,
                    accountRequired: true, readinessRequired: true, productionCapability: 'Partial',
                    serviceLevels: [{ code: serviceLevel, name: displayName, typicalDaysMin: 0, typicalDaysMax: 0 }],
                },
            ] })
            if (url === '/admin/shipping/carrier-accounts') return Promise.resolve({ data: [] })
            if (url === '/warehouses') return Promise.resolve({ data: warehousePayload })
            if (url === '/admin/shipping/audit') return Promise.resolve({ data: [] })
            if (url === '/shippingmethods?activeOnly=false') return Promise.resolve({ data: [] })
            return Promise.resolve({ data: [] })
        })

        render(<ShippingAdministrationWorkspace initialTab="shipping-methods" />)
        fireEvent.click(await screen.findByRole('button', { name: /Add Shipping Method/i }))

        expect(screen.getByRole('option', { name: 'Manual Fulfillment' })).not.toBeDisabled()
        const blockedOption = new RegExp(`${displayName}.*Not ready`, 'i')
        expect(screen.getByRole('option', { name: blockedOption })).toBeDisabled()

        // Inactive drafts and deactivation remain available; only activation needs readiness.
        fireEvent.click(screen.getByRole('checkbox', { name: 'Active' }))
        expect(screen.getByRole('option', { name: blockedOption })).not.toBeDisabled()
        fireEvent.change(screen.getByRole('combobox', { name: 'Carrier' }), { target: { value: carrierCode } })
        expect(screen.getByRole('button', { name: 'Save' })).not.toBeDisabled()
        fireEvent.click(screen.getByRole('checkbox', { name: 'Active' }))
        expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
        expect(api.post).not.toHaveBeenCalled()
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

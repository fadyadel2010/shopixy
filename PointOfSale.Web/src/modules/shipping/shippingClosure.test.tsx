import React from 'react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { createInstance } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import en from '../../i18n/locales/en.json'
import ar from '../../i18n/locales/ar.json'
import ShipmentEvidencePanel from './components/ShipmentEvidencePanel'
import ShipmentSummaryCard from './components/ShipmentSummaryCard'
import ShipmentTimeline from './components/ShipmentTimeline'
import WarehouseAddressAdmission from './components/WarehouseAddressAdmission'
import { storedLabelLink } from './shipmentPresentation'
import { useShipmentsList } from './hooks/useShipmentsApi'
import { admitCarrierAccounts, admitSupportedCarriers } from '../shipping-administration/shippingAdministrationApi'
import CarrierAccountCard from '../shipping-administration/components/CarrierAccountCard'
import ShippingMethodsTab from '../shipping-administration/components/ShippingMethodsTab'

const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../../services/api', () => ({ default: api, getSafeApiErrorMessage: (_: unknown, fallback: string) => fallback }))
vi.mock('../../contexts/SettingsContext', () => ({ useSettings: () => ({ settings: { currencyCode: 'EGP' }, formatCurrency: (value: number) => `${value} EGP` }) }))
vi.mock('../../common/confirm/ConfirmDialogProvider', () => ({ useConfirmDialog: () => vi.fn() }))
beforeEach(() => vi.resetAllMocks())
afterEach(cleanup)
async function wrapper(language: string) {
    const i18n = createInstance()
    await i18n.init({ lng: language, fallbackLng: 'en', resources: { en: { translation: en }, ar: { translation: ar } } })
    return ({ children }: { children: React.ReactNode }) => <I18nextProvider i18n={i18n}><MemoryRouter>{children}</MemoryRouter></I18nextProvider>
}

it.each(['en', 'ar'])('renders actual details evidence and separate COD facts in %s', async language => {
    const Wrapper = await wrapper(language)
    const shipment = { status: 'PendingExternalRegistration', carrierCode: 'fedex', shipmentNumber: 'SYNTHETIC', orderId: 'order',
        serviceLevel: 'FEDEX_GROUND', labelData: 'JVBERi0x', labelFormat: 'pdf', awbNumber: 'AWB-ONLY',
        recipient: { name: 'Actual nested recipient', address: 'Recorded street', city: 'Recorded city', country: 'US' },
        createdAtUtc: '2026-10-11T10:00:00Z', externalRegistrationState: 'ReconciliationRequired', externalRegistrationError: 'UNRESOLVED',
        isCashOnDelivery: true, codAmount: 120, codCollected: true, currency: 'USD', shippingCost: 25 }
    render(<Wrapper><ShipmentSummaryCard shipment={shipment} /><ShipmentEvidencePanel shipment={shipment} /></Wrapper>)
    expect(screen.getByText('Actual nested recipient')).toBeInTheDocument()
    expect(screen.getByText('Recorded street, Recorded city, US')).toBeInTheDocument()
    expect(screen.getByText('AWB-ONLY')).toBeInTheDocument()
    expect(screen.getByRole('alert')).toHaveTextContent('UNRESOLVED')
    const resources = language === 'ar' ? ar : en
    expect(screen.getByRole('link', { name: resources.shipping.closure.storedLabel })).toHaveAttribute('href', 'data:application/pdf;base64,JVBERi0x')
    expect(screen.getByText(resources.shipping.closure.moneyBoundary)).toBeInTheDocument()
    expect(screen.getByText(resources.shipping.closure.collected)).toBeInTheDocument()
})

it.each(['en', 'ar'])('shows a backend-derived recovery warning without rewriting a zero-count registration state (%s)', async language => {
    const Wrapper = await wrapper(language)
    const shipment = { carrierCode: 'dhl', externalRegistrationState: 'PendingExternalCall', externalRegistrationAttemptCount: 0,
        externalRegistrationRecoveryRequired: true }
    render(<Wrapper><ShipmentEvidencePanel shipment={shipment} /></Wrapper>)
    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(screen.getByText((language === 'ar' ? ar : en).shipping.closure.registrationStates.PendingExternalCall)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /retry|إعادة/i })).not.toBeInTheDocument()
})

it('rejects executable stored-label URLs and unsupported inline data', () => {
    expect(storedLabelLink({ labelUrl: 'javascript:alert(1)' })).toBeNull()
    expect(storedLabelLink({ labelFormat: 'html', labelData: 'PGgxPg==' })).toBeNull()
    expect(storedLabelLink({ labelFormat: 'pdf', labelData: '<script>' })).toBeNull()
    expect(storedLabelLink({ labelUrl: 'https://example.invalid/stored.pdf' })).toEqual({ href: 'https://example.invalid/stored.pdf' })
})

it('does not invent missing historical timeline timestamps', async () => {
    const Wrapper = await wrapper('en')
    render(<Wrapper><ShipmentTimeline shipment={{ status: 'Delivered', createdAtUtc: '2026-10-11T10:00:00Z', deliveredAtUtc: '2026-10-11T11:00:00Z' }} /></Wrapper>)
    expect(screen.getAllByText(en.shipping.closure.notObserved)).toHaveLength(4)
})

it('sends list filters using current API names including false COD', async () => {
    api.get.mockResolvedValue({ data: { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 } })
    const filters = { carrierCode: 'fedex', dateFrom: '2026-10-01', dateTo: '2026-10-11', isCod: false, search: 'AWB' }
    const hook = renderHook(() => useShipmentsList(filters))
    await waitFor(() => expect(hook.result.current.loading).toBe(false))
    const query = new URLSearchParams(api.get.mock.calls[0][0].split('?')[1])
    expect(query.get('carrier')).toBe('fedex')
    expect(query.get('fromDate')).toBe('2026-10-01')
    expect(query.get('toDate')).toBe('2026-10-11')
    expect(query.get('isCod')).toBe('false')
    expect(query.get('search')).toBe('AWB')
    expect(query.has('carrierCode')).toBe(false)
})

it('fails closed when capability or connection authority is absent', () => {
    const [carrier] = admitSupportedCarriers([{ carrierCode: 'unknown', displayName: 'Unknown', serviceLevels: [] }])
    expect(carrier.productionCapability).toBe('Unavailable')
    expect(carrier.connectionTestSupported).toBe(false)
    expect(carrier.connectionTestMode).toBe('Unsupported')
})

it('a failed connection record on HTTP 200 is not action success', async () => {
    api.post.mockResolvedValue({ data: { status: 'Failed', failureCode: 'SYNTHETIC_FAILURE' } })
    const Wrapper = await wrapper('en')
    render(<Wrapper><CarrierAccountCard account={admitCarrierAccounts([{ id: 'account', carrierCode: 'bosta', state: 'Draft', operationalState: 'Disabled', healthState: 'Warning', serviceLevels: [], credentialReferences: [], warehouseAssignments: [], policies: [],
        integrationReadiness: { canExecute: false, credentialAvailability: 'Available', blockers: [] } }])[0]}
        supportedCarriers={admitSupportedCarriers([{ carrierCode: 'bosta', displayName: 'Bosta', productionCapability: 'Partial', credentialFields: [], serviceLevels: [], connectionTestSupported: true, connectionTestMode: 'Remote' }])}
        onMutated={vi.fn().mockResolvedValue(undefined)} /></Wrapper>)
    fireEvent.submit(screen.getByRole('button', { name: en.common.save }).closest('form')!)
    await screen.findByText(/SYNTHETIC_FAILURE/)
    expect(screen.queryByText(/completed successfully/i)).not.toBeInTheDocument()
})

it('unavailable carrier catalog does not invent standard or allow method submission', async () => {
    api.get.mockImplementation(url => Promise.resolve({ data: url.includes('supported-carriers') ? [
        { carrierCode: 'mylerz', displayName: 'Mylerz', productionCapability: 'Unavailable', serviceLevels: [] }
    ] : [] }))
    const Wrapper = await wrapper('en')
    render(<Wrapper><ShippingMethodsTab /></Wrapper>)
    fireEvent.click(screen.getByRole('button', { name: '+ ' + en.shipping.addMethod }))
    expect(await screen.findByRole('option', { name: /Mylerz.*Not ready/ })).toBeDisabled()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Active' }))
    fireEvent.change(screen.getByLabelText(en.shipping.closure.carrier), { target: { value: 'mylerz' } })
    await screen.findByText(en.shipping.closure.serviceUnavailable)
    expect(screen.getByRole('button', { name: en.common.save })).toBeDisabled()
    expect(api.post).not.toHaveBeenCalled()
})

it.each(['en', 'ar'])('submits genuine selected warehouse identities and preserves failed input in %s', async language => {
    const resources = language === 'ar' ? ar : en
    const labels = resources.shipping.warehouseAdmission
    const country = '11111111-1111-4111-8111-111111111111', state = '22222222-2222-4222-8222-222222222222', locality = '33333333-3333-4333-8333-333333333333'
    const evidence = '44444444-4444-4444-8444-444444444444'
    const warehouse = { id: '55555555-5555-4555-8555-555555555555', name: 'Synthetic warehouse', address: 'Saved street' }
    api.get.mockImplementation((_url, { params }) => Promise.resolve({ data: { catalogReleaseId: country, places:
        !params.countryPlaceId ? [{ placeId: country, placeKind: 'Country', canonicalCode: 'US', displayName: 'United States', postalCodePolicy: 'Required' }] :
        !params.parentPlaceId ? [{ placeId: state, placeKind: 'AdministrativeArea', canonicalCode: 'NY', displayName: 'New York' }] :
        params.parentPlaceId === state ? [{ placeId: locality, placeKind: 'Locality', canonicalCode: 'NYC', displayName: 'New York City' }] : [] } }))
    api.post.mockRejectedValueOnce({ response: { status: 400, data: { code: 'PostalCodeInvalid', message: 'Invalid postal code' } } })
        .mockResolvedValueOnce({ data: { warehouseId: warehouse.id, addressAdmissionEvidenceId: evidence, catalogReleaseId: country, addressFingerprintSha256: 'a'.repeat(64) } })
    const Wrapper = await wrapper(language)
    render(<Wrapper><WarehouseAddressAdmission warehouse={warehouse} onClose={vi.fn()} /></Wrapper>)
    await screen.findByRole('option', { name: 'US — United States' })
    fireEvent.change(screen.getByLabelText(labels.countryPlaceId), { target: { value: country } })
    await screen.findByRole('option', { name: 'NY — New York' })
    fireEvent.change(screen.getByLabelText(labels.administrativeAreaPlaceId), { target: { value: state } })
    await screen.findByRole('option', { name: 'NYC — New York City' })
    fireEvent.change(screen.getByLabelText(labels.localityPlaceId), { target: { value: locality } })
    await waitFor(() => expect(screen.queryByText(labels.loading)).not.toBeInTheDocument())
    fireEvent.change(screen.getByLabelText(labels.street), { target: { value: 'Saved street' } })
    fireEvent.change(screen.getByLabelText(labels.postal), { target: { value: '10001' } })
    fireEvent.change(screen.getByLabelText(labels.reason), { target: { value: 'Confirm genuine location' } })
    fireEvent.click(screen.getByRole('button', { name: labels.save }))
    await screen.findByRole('alert')
    expect(screen.getByLabelText(labels.postal)).toHaveValue('10001')
    expect(api.post.mock.calls[0][1]).toEqual({ countryPlaceId: country, administrativeAreaPlaceId: state, localityPlaceId: locality,
        districtPlaceId: null, zonePlaceId: null, streetLine1: 'Saved street', postalCode: '10001', reason: 'Confirm genuine location' })
    fireEvent.click(screen.getByRole('button', { name: labels.save }))
    await screen.findByText(evidence)
    expect(screen.getByRole('button', { name: labels.save })).toBeDisabled()
})

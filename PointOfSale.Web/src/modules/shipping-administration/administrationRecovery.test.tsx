import { act, cleanup, fireEvent, render, renderHook, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { createInstance } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import CarrierAccountCard from './components/CarrierAccountCard'
import { admitCarrierAccounts, admitSupportedCarriers, selectCarrierEnvironment } from './shippingAdministrationApi'
import { useShippingAdministrationApi } from './hooks/useShippingAdministrationApi'
import en from '../../i18n/locales/en.json'

const api = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn(), post: vi.fn() }))
vi.mock('../../services/api', () => ({ default: api, getSafeApiErrorMessage: (error: Error, fallback: string) => error.message || fallback }))
beforeEach(() => vi.resetAllMocks())
afterEach(cleanup)

const accountPayload = { id: 'synthetic-account', carrierCode: 'bosta', state: 'Draft', operationalState: 'NotReady', healthState: 'Blocked',
    environment: 'Sandbox', credentialReferences: [], warehouseAssignments: [], policies: [], serviceLevels: [] }
const definitions = admitSupportedCarriers([{ carrierCode: 'bosta', displayName: 'Bosta', productionCapability: 'Partial', accountRequired: true,
    connectionTestSupported: true, connectionTestMode: 'Remote', environmentSupported: true, serviceLevels: [],
    credentialFields: [{ key: 'apiKey', target: 'ApiKey', label: 'API Key', isSecret: true, required: true }] }])

it.each([false, undefined])('an HTTP200 refusal/malformed result is not an administration success (%s)', async succeeded => {
    api.put.mockResolvedValue({ data: { succeeded, failureCode: 'CARRIER_NOT_READY', blockers: ['ROUTE_NOT_QUALIFIED'] } })
    await expect(selectCarrierEnvironment('synthetic-account', 'Production')).rejects.toThrow(/CARRIER_NOT_READY/)
    expect(api.put).toHaveBeenCalledTimes(1)
})

it('changing account clears entered secrets and does not reuse the previous environment', async () => {
    const i18n = createInstance()
    await i18n.init({ lng: 'en', resources: { en: { translation: en } } })
    const first = admitCarrierAccounts([accountPayload])[0]
    const second = admitCarrierAccounts([{ ...accountPayload, id: 'other-account', environment: 'Production' }])[0]
    const onMutated = vi.fn().mockResolvedValue(undefined)
    const view = render(<I18nextProvider i18n={i18n}><CarrierAccountCard account={first} supportedCarriers={definitions} onMutated={onMutated} /></I18nextProvider>)
    fireEvent.change(screen.getByLabelText('API Key', { exact: false }), { target: { value: 'synthetic-private-key' } })
    view.rerender(<I18nextProvider i18n={i18n}><CarrierAccountCard account={second} supportedCarriers={definitions} onMutated={onMutated} /></I18nextProvider>)
    expect(screen.getByLabelText('API Key', { exact: false })).toHaveValue('')
    expect(screen.getByLabelText(en.shippingAdministration.account.environment, { selector: 'select' })).toHaveValue('Production')
    expect(api.put).not.toHaveBeenCalled()
})

it('a slower earlier administration refresh cannot replace the latest readiness snapshot', async () => {
    let finishOld!: (value: { data: unknown }) => void
    api.get.mockImplementation((url: string) => {
        if (url.endsWith('carrier-accounts')) return new Promise(resolve => { finishOld = resolve })
        return Promise.resolve({ data: [] })
    })
    const { result } = renderHook(() => useShippingAdministrationApi())
    await act(async () => {})
    api.get.mockResolvedValue({ data: [] })
    await act(async () => { await result.current.refresh() })
    expect(result.current.carrierAccounts).toEqual([])
    await act(async () => { finishOld({ data: [accountPayload] }) })
    expect(result.current.carrierAccounts).toEqual([])
})

it('a failed newest refresh removes the old positive readiness snapshot', async () => {
    api.get.mockImplementation((url: string) => Promise.resolve({ data: url.endsWith('carrier-accounts') ? [accountPayload] : [] }))
    const { result } = renderHook(() => useShippingAdministrationApi())
    await act(async () => {})
    expect(result.current.carrierAccounts).toHaveLength(1)
    api.get.mockRejectedValue(new Error('synthetic readiness read failure'))
    await act(async () => { await result.current.refresh() })
    expect(result.current.carrierAccounts).toEqual([])
    expect(result.current.error).toBe('synthetic readiness read failure')
})

it('duplicate submissions send once; previous-account completion cannot clear a new account secret', async () => {
    let finishOld!: (value: { data: { succeeded: boolean } }) => void
    api.post.mockImplementation(() => new Promise(resolve => { finishOld = resolve }))
    const i18n = createInstance()
    await i18n.init({ lng: 'en', resources: { en: { translation: en } } })
    const first = admitCarrierAccounts([accountPayload])[0]
    const second = admitCarrierAccounts([{ ...accountPayload, id: 'other-account' }])[0]
    const onMutated = vi.fn().mockResolvedValue(undefined)
    const view = render(<I18nextProvider i18n={i18n}><CarrierAccountCard account={first} supportedCarriers={definitions} onMutated={onMutated} /></I18nextProvider>)
    fireEvent.change(screen.getByLabelText('API Key', { exact: false }), { target: { value: 'synthetic-old-key' } })
    const form = screen.getByRole('button', { name: en.common.save }).closest('form')
    if (!form) throw new Error('Connection validation form must exist')
    fireEvent.submit(form)
    fireEvent.submit(form)
    expect(api.post).toHaveBeenCalledTimes(1)
    view.rerender(<I18nextProvider i18n={i18n}><CarrierAccountCard account={second} supportedCarriers={definitions} onMutated={onMutated} /></I18nextProvider>)
    fireEvent.change(screen.getByLabelText('API Key', { exact: false }), { target: { value: 'synthetic-new-key' } })
    await act(async () => { finishOld({ data: { succeeded: true } }) })
    expect(screen.getByLabelText('API Key', { exact: false })).toHaveValue('synthetic-new-key')
    expect(onMutated).not.toHaveBeenCalled()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
})

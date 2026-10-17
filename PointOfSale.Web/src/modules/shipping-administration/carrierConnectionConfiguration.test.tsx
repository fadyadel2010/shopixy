import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { createInstance } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import CarrierAccountCard from './components/CarrierAccountCard'
import { connectionFailureCategory } from './connectionDiagnostics'
import { admitCarrierAccounts, admitSupportedCarriers } from './shippingAdministrationApi'
import en from '../../i18n/locales/en.json'
import ar from '../../i18n/locales/ar.json'

const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn() }))
vi.mock('../../services/api', () => ({ default: api, getSafeApiErrorMessage: (error: Error, fallback: string) => error.message || fallback }))
beforeEach(() => vi.resetAllMocks())
afterEach(cleanup)
const required: Record<string, string[]> = { bosta: ['apiKey'], aramex: ['apiKey', 'apiSecret', 'accountId', 'accountPin', 'accountEntity', 'accountCountryCode'], dhl: ['apiKey', 'apiSecret'], mylerz: ['apiKey', 'apiSecret'] }
const target: Record<string, string> = { apiKey: 'ApiKey', apiSecret: 'ApiSecret', accountId: 'AccountId', accountPin: 'ExtraConfig.AccountPin', accountEntity: 'ExtraConfig.AccountEntity', accountCountryCode: 'ExtraConfig.AccountCountryCode' }
const payload = (carrier: string, saved = false, current = false) => ({ id: `fixture-${carrier}`, carrierCode: carrier, state: 'Draft', environment: 'Production', healthState: 'Blocked', operationalState: 'NotReady',
    serviceLevels: [], credentialReferences: [], policies: [], warehouseAssignments: [], savedCredentialFields: saved ? required[carrier] : [], connectionTestIsCurrent: current,
    lastConnectionTest: { id: 'old-test', environment: 'Production', status: 'Succeeded', testedAtUtc: '2026-10-15T00:00:00Z' } })
const definition = (carrier: string) => admitSupportedCarriers([{ carrierCode: carrier, displayName: carrier, accountRequired: true, productionCapability: carrier === 'mylerz' ? 'Unavailable' : 'Partial',
    environmentSupported: carrier !== 'mylerz', connectionTestSupported: true, connectionTestMode: 'Remote', serviceLevels: [],
    credentialFields: [...required[carrier].map(key => ({ key, label: key, target: target[key], required: true, isSecret: ['apiKey', 'apiSecret', 'accountPin'].includes(key) })),
        ...(carrier === 'bosta' ? [{ key: 'businessLocationId', target: 'ExtraConfig.businessLocationId', label: 'pickup', required: false }] : [])] }])
async function view(carrier: string, saved = false, current = false, language = 'en') {
    const i18n = createInstance()
    await i18n.init({ lng: language, resources: { en: { translation: en }, ar: { translation: ar } } })
    const onMutated = vi.fn().mockResolvedValue(undefined)
    const wrap = (account: unknown) => <I18nextProvider i18n={i18n}><CarrierAccountCard account={admitCarrierAccounts([account])[0]} supportedCarriers={definition(carrier)} onMutated={onMutated} /></I18nextProvider>
    return { ...render(wrap(payload(carrier, saved, current))), wrap, onMutated }
}

it.each(Object.keys(required))('%s saves provider-specific fields, clears secrets and does not test or enable services', async carrier => {
    api.post.mockResolvedValue({ data: { succeeded: true } })
    const rendered = await view(carrier)
    for (const key of required[carrier]) {
        const input = document.querySelector(`input[name="${target[key]}"]`) as HTMLInputElement
        expect(input.required).toBe(true)
        fireEvent.change(input, { target: { value: key === 'accountEntity' ? 'CAI' : key === 'accountCountryCode' ? 'EG' : `synthetic-${key}` } })
    }
    if (carrier === 'bosta') expect(document.querySelector('input[name="ExtraConfig.businessLocationId"]')).not.toBeRequired()
    fireEvent.click(screen.getByRole('button', { name: en.common.save }))
    await waitFor(() => expect(screen.getByText(en.shippingAdministration.connection.saved)).toBeInTheDocument())
    expect(api.post).toHaveBeenCalledWith(`/admin/shipping/carrier-accounts/fixture-${carrier}/connection-tests`, expect.objectContaining({ apiKey: 'synthetic-apiKey', saveOnly: true }))
    if (carrier === 'aramex') expect(api.post.mock.calls[0][1].extraConfig).toEqual({ AccountPin: 'synthetic-accountPin', AccountEntity: 'CAI', AccountCountryCode: 'EG' })
    expect(api.put).not.toHaveBeenCalled()
    expect(document.querySelector('input[name=ApiKey]')).toHaveValue('')
    rendered.rerender(rendered.wrap(payload(carrier, true)))
    expect(screen.getByRole('button', { name: en.shippingAdministration.connection.test })).toBeEnabled()
    expect(document.querySelector('input[name=ApiKey]')).toHaveAttribute('placeholder', en.shippingAdministration.connection.masked)
    expect(screen.queryByText(en.shippingAdministration.connection.authenticated)).not.toBeInTheDocument()
})

it.each(Object.keys(required))('%s tests only saved credentials and HTTP200 refusal removes old success immediately', async carrier => {
    let finish!: (value: unknown) => void
    api.post.mockImplementation(() => new Promise(resolve => { finish = resolve }))
    await view(carrier, true, true)
    fireEvent.click(screen.getByRole('button', { name: en.shippingAdministration.connection.test }))
    expect(screen.getAllByText(en.shippingAdministration.connection.testing).length).toBeGreaterThan(0)
    expect(screen.queryByText(en.shippingAdministration.connection.authenticated)).not.toBeInTheDocument()
    expect(api.post).toHaveBeenCalledWith(expect.stringContaining('/connection-tests'), { useSavedCredentials: true, saveOnly: false, useSandbox: false })
    await act(async () => finish({ data: { succeeded: false, failureCode: 'INVALID_CREDENTIALS' } }))
    expect(screen.getByRole('alert')).toHaveTextContent('INVALID_CREDENTIALS')
    expect(screen.getByText(en.shippingAdministration.connection.errors.authentication)).toBeInTheDocument()
    expect(screen.queryByText(en.shippingAdministration.connection.authenticated)).not.toBeInTheDocument()
})

it.each(Object.keys(required))('%s shows backend400 field refusal and keeps unsaved input for correction', async carrier => {
    api.post.mockRejectedValue({ response: { status: 400, data: { succeeded: false, failureCode: 'CREDENTIAL_APISECRET_MISSING' } } })
    await view(carrier, true)
    fireEvent.change(document.querySelector('input[name=ApiKey]')!, { target: { value: 'synthetic-retain-on-failure' } })
    fireEvent.click(screen.getByRole('button', { name: en.common.save }))
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('CREDENTIAL_APISECRET_MISSING'))
    expect(document.querySelector('input[name=ApiKey]')).toHaveValue('synthetic-retain-on-failure')
})

it.each(['data', 'details'])('reads the normalized HTTP400 refusal without losing field codes inside %s', async envelope => {
    api.post.mockRejectedValue({ response: { status: 400, data: { code: 'INVALID_REQUEST', message: 'Invalid request.', [envelope]: { Succeeded: false, FailureCode: 'CREDENTIAL_ACCOUNTPIN_MISSING', Blockers: ['CREDENTIAL_ACCOUNTPIN_MISSING'] } } } })
    await view('aramex', true)
    fireEvent.click(screen.getByRole('button', { name: en.shippingAdministration.connection.test }))
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('CREDENTIAL_ACCOUNTPIN_MISSING'))
    expect(screen.getByText(en.shippingAdministration.connection.errors.field)).toBeInTheDocument()
})

it.each(['en', 'ar'])('all four cards have localized labels and secret-only masking in %s', async language => {
    for (const carrier of Object.keys(required)) {
        await view(carrier, true, false, language)
        const strings = language === 'en' ? en : ar
        expect(screen.getByRole('button', { name: strings.shippingAdministration.connection.test })).toBeEnabled()
        expect(document.querySelector('input[name=ApiKey]')).toHaveAttribute('type', 'password')
        expect(document.querySelector('input[name=ApiKey]')).toHaveValue('')
        expect(screen.queryByText(/shippingAdministration\.connection\./)).not.toBeInTheDocument()
        cleanup()
    }
})

it('shows disabled booking independently of successful authentication without activating anything', async () => {
    const rendered = await view('bosta', true, true)
    rendered.rerender(rendered.wrap({ ...payload('bosta', true, true), state: 'Disabled',
        integrationReadiness: { enabled: false, canExecute: false, blockers: ['CARRIER_CONFIGURATION_DISABLED', 'BOSTA_PROVIDER_ACCOUNT_ELIGIBILITY_UNVERIFIED'] } }))
    expect(screen.getByText(en.shippingAdministration.connection.authenticated)).toBeVisible()
    const readiness = within(screen.getByRole('region', { name: 'Activation and booking eligibility' }))
    expect(readiness.getAllByText(en.common.no)).toHaveLength(2)
    expect(readiness.getByText(en.shippingAdministration.native.errors.CARRIER_CONFIGURATION_DISABLED)).toBeVisible()
    expect(readiness.getByText(en.shippingAdministration.native.errors.BOSTA_PROVIDER_ACCOUNT_ELIGIBILITY_UNVERIFIED)).toBeVisible()
    expect(api.post).not.toHaveBeenCalled(); expect(api.put).not.toHaveBeenCalled()
})

it.each([['CREDENTIAL_ACCOUNTPIN_MISSING', 'field'], ['INVALID_CREDENTIALS_1007', 'authentication'], ['WRONG_ENVIRONMENT_OR_ENDPOINT', 'environment'], ['NETWORK_ERROR', 'network'], ['CONNECTION_TIMEOUT', 'timeout'], ['PROVIDER_ACCESS_DENIED', 'access']])('classifies %s actionably', (code, category) => expect(connectionFailureCategory(code)).toBe(category))

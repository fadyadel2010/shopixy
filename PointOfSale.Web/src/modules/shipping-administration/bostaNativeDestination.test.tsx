import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import BostaNativeDestinationSelector from './components/BostaNativeDestinationSelector'
import en from '../../i18n/locales/en.json'
import ar from '../../i18n/locales/ar.json'
const api = vi.hoisted(() => ({ load: vi.fn(), validate: vi.fn(), save: vi.fn() }))
vi.mock('./bostaNativeApi', () => ({ loadBostaNativeGeography: api.load, validateBostaNativeDestination: api.validate, saveBostaNativeDestination: api.save }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key: string) => key }) }))
beforeEach(() => vi.resetAllMocks())
afterEach(cleanup)
const cityId = 'FceDyHXwpSYYF9zGW', districtId = 'VPRW3NEl5WQ'
const response = (districts = false, accountId = 'account-1', environment = 'Production') => ({ accountId, environment, places: [
    { type: 'city', id: cityId, name: 'Cairo', dropOffAvailable: true },
    ...(districts ? [{ type: 'district', id: districtId, name: 'Zahraa Nasr City', parentId: cityId, dropOffAvailable: true },
        { type: 'district', id: 'unserviceable-reference', name: 'Unavailable reference', parentId: cityId, dropOffAvailable: false }] : []),
] })
const button = (name: string) => screen.getByRole('button', { name: `shippingAdministration.native.${name}` })
const select = (name: string) => screen.getByRole('combobox', { name: `shippingAdministration.native.${name}` })
async function choose() {
    fireEvent.click(button('loadCities'))
    await waitFor(() => expect(select('city')).not.toBeDisabled())
    fireEvent.change(select('city'), { target: { value: cityId } })
    fireEvent.click(button('loadDistricts'))
    await waitFor(() => expect(select('district')).not.toBeDisabled())
    fireEvent.change(select('district'), { target: { value: districtId } })
}
it('requires explicit discovery and validates genuine provider IDs without saving, publishing or activation', async () => {
    api.load.mockResolvedValueOnce(response()).mockResolvedValueOnce(response(true))
    api.validate.mockResolvedValue({ accountId: 'account-1', validated: true, serviceable: true })
    render(<BostaNativeDestinationSelector accountId="account-1" environment="Production" />)
    expect(api.load).not.toHaveBeenCalled()
    await choose()
    expect(screen.getByRole('option', { name: /Zahraa Nasr City — VPRW3NEl5WQ/ })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: /Unavailable reference/ })).toBeDisabled()
    expect(api.validate).not.toHaveBeenCalled()
    fireEvent.click(button('validate'))
    expect(await screen.findByRole('status')).toHaveTextContent('shippingAdministration.native.validated')
    expect(api.validate).toHaveBeenCalledExactlyOnceWith('account-1', cityId, districtId)
    expect(api.save).not.toHaveBeenCalled()
})
it.each([['foreign', 'Production'], ['account-1', 'Sandbox']])('rejects foreign-account/environment reference results', async (accountId, environment) => {
    api.load.mockResolvedValue(response(false, accountId, environment))
    render(<BostaNativeDestinationSelector accountId="account-1" environment="Production" />)
    fireEvent.click(button('loadCities')); await screen.findByRole('alert')
    expect(select('city')).toHaveValue(''); expect(button('validate')).toBeDisabled()
})
it('discards an in-flight result after credentials or account scope changes', async () => {
    let finish!: (value: ReturnType<typeof response>) => void
    api.load.mockImplementation(() => new Promise(resolve => { finish = resolve }))
    const view = render(<BostaNativeDestinationSelector accountId="account-1" environment="Production" configurationVersion="v1" />)
    fireEvent.click(button('loadCities'))
    view.rerender(<BostaNativeDestinationSelector accountId="account-1" environment="Production" configurationVersion="v2" />)
    await act(async () => finish(response()))
    expect(select('city')).toBeDisabled(); expect(button('validate')).toBeDisabled()
})
it('only saves the selected shipment snapshot after an explicit action and locks the retained selection', async () => {
    api.load.mockResolvedValueOnce(response()).mockResolvedValueOnce(response(true))
    api.save.mockResolvedValue({})
    render(<BostaNativeDestinationSelector accountId="account-1" environment="Production" shipmentId="shipment-1" />)
    await choose(); expect(api.save).not.toHaveBeenCalled()
    fireEvent.click(button('saveSnapshot'))
    expect(await screen.findByRole('status')).toHaveTextContent('shippingAdministration.native.snapshotSaved')
    expect(api.save).toHaveBeenCalledExactlyOnceWith('account-1', 'shipment-1', cityId, districtId)
    expect(button('saveSnapshot')).toBeDisabled()
})
it('provides matching Arabic/English destination states without promising booking readiness', () => {
    expect(Object.keys(ar.shippingAdministration.native).sort()).toEqual(Object.keys(en.shippingAdministration.native).sort())
    expect(en.shippingAdministration.native.validated).toContain('still required')
    expect(ar.shippingAdministration.native.validated).toContain('مطلوبة')
})

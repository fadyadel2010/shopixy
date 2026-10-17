import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import BostaPickupSelector from './components/BostaPickupSelector'
const api = vi.hoisted(() => ({ get: vi.fn() }))
vi.mock('../../services/api', () => ({ default: api }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key: string) => key }) }))
beforeEach(() => vi.resetAllMocks())
afterEach(cleanup)
const response = (id = 'account-1', environment = 'Production') => ({ data: { accountId: id, environment, locations: [{ id: 'PICKUP-1', cityName: 'Cairo', districtName: 'District' }] } })
const load = () => screen.getByRole('button', { name: 'shippingAdministration.bostaPickup.load' })

it('loads only on request, never auto-selects default and selection is not a save or booking', async () => {
    api.get.mockResolvedValue(response())
    const onSelected = vi.fn()
    render(<BostaPickupSelector accountId="account-1" environment="Production" onSelected={onSelected} />)
    expect(api.get).not.toHaveBeenCalled()
    fireEvent.click(load())
    const select = await screen.findByRole('combobox')
    expect(onSelected).not.toHaveBeenCalled(); expect(select).toHaveValue('')
    fireEvent.change(select, { target: { value: 'PICKUP-1' } })
    expect(onSelected).toHaveBeenCalledExactlyOnceWith('PICKUP-1')
    expect(api.get).toHaveBeenCalledTimes(1)
})

it.each([response('foreign'), response('account-1', 'Sandbox')])('rejects an account/environment-mismatched response', async data => {
    api.get.mockResolvedValue(data)
    render(<BostaPickupSelector accountId="account-1" environment="Production" onSelected={vi.fn()} />)
    fireEvent.click(load()); await screen.findByRole('alert')
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
})

it('cannot display a stale pickup list after the selected account changes', async () => {
    let finish!: (value: ReturnType<typeof response>) => void
    api.get.mockImplementation(() => new Promise(resolve => { finish = resolve }))
    const view = render(<BostaPickupSelector accountId="account-1" environment="Production" onSelected={vi.fn()} />)
    fireEvent.click(load())
    view.rerender(<BostaPickupSelector accountId="account-2" environment="Production" onSelected={vi.fn()} />)
    await act(async () => finish(response()))
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})

it('invalidates selectable locations when saved credentials change within the same account and environment', async () => {
    api.get.mockResolvedValue(response())
    const view = render(<BostaPickupSelector accountId="account-1" environment="Production" configurationVersion="original" onSelected={vi.fn()} />)
    fireEvent.click(load()); await screen.findByRole('combobox')
    view.rerender(<BostaPickupSelector accountId="account-1" environment="Production" configurationVersion="rotated" onSelected={vi.fn()} />)
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
})

it('reports a verified provider default without selecting, saving or activating it', async () => {
    api.get.mockResolvedValue({ data: { ...response().data, pickupSelection: { mode: 'ProviderDefault', resolvedLocationId: 'PICKUP-1', verifiedAtUtc: '2026-10-16T00:00:00Z' } } })
    const onSelected = vi.fn()
    render(<BostaPickupSelector accountId="account-1" environment="Production" onSelected={onSelected} />)
    fireEvent.click(load())
    expect(await screen.findByText('shippingAdministration.bostaPickup.defaultVerified')).toHaveAttribute('role', 'status')
    expect(screen.getByRole('combobox')).toHaveValue('')
    expect(onSelected).not.toHaveBeenCalled()
    expect(api.get).toHaveBeenCalledTimes(1)
})

it('shows an ambiguous default as a blocker, not verified default authority', async () => {
    api.get.mockResolvedValue({ data: { ...response().data, pickupSelection: { mode: 'ProviderDefault', resolvedLocationId: null, errorCode: 'BOSTA_DEFAULT_PICKUP_AMBIGUOUS' } } })
    render(<BostaPickupSelector accountId="account-1" environment="Production" onSelected={vi.fn()} />)
    fireEvent.click(load())
    expect(await screen.findByRole('alert')).toHaveTextContent('BOSTA_DEFAULT_PICKUP_AMBIGUOUS')
    expect(screen.queryByText('shippingAdministration.bostaPickup.defaultVerified')).not.toBeInTheDocument()
})

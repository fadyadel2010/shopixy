import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import BostaGeographySetup from './components/BostaGeographySetup'

const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../../services/api', () => ({ default: api }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ i18n: { language: 'en' } }) }))
beforeEach(() => vi.resetAllMocks())
afterEach(cleanup)
const release = '11111111-1111-4111-8111-111111111111'
const reference = { accountId: 'account-1', environment: 'Production', catalogReleaseId: 'synthetic-catalog',
    places: [{ type: 'city', id: 'reference-city', name: 'Reference city' }], canonicalPlaces: [
        { id: 'canonical-country', canonicalCode: 'EG', placeKind: 'Country' },
        { id: 'canonical-city', canonicalCode: 'TEST-ONLY-CITY', placeKind: 'AdministrativeArea' },
        { id: 'canonical-district', canonicalCode: 'TEST-ONLY-DISTRICT', placeKind: 'District' }] }
const run = { id: 'synthetic-run', sourceHashSha256: 'source-hash', candidates: [
    { id: 'candidate-country', providerPlaceType: 'country', providerPlaceId: 'reference-country', providerDisplayName: 'Egypt', providerParentPlaceId: null, serviceable: true },
    { id: 'candidate-city', providerPlaceType: 'city', providerPlaceId: 'reference-city', providerDisplayName: 'Reference city', providerParentPlaceId: 'reference-country', serviceable: true },
    { id: 'candidate-district', providerPlaceType: 'district', providerPlaceId: 'reference-district', providerDisplayName: 'Reference district', providerParentPlaceId: 'reference-city', serviceable: true }] }
const renderSetup = () => render(<BostaGeographySetup accountId="account-1" environment="Production" configurationVersion="v1" disabled={false} />)
const load = () => fireEvent.click(screen.getByRole('button', { name: 'Load current geography reference' }))

it('requires explicit sync and canonical review, then reads persisted identities without booking or activation', async () => {
    api.get.mockImplementation(async (url: string) => ({ data: url.endsWith('/validation') ? { succeeded: true, contentChecksumSha256: 'content-hash' } :
        url.includes('/releases/') ? { id: release, status: 'Draft', mappings: [] } : reference }))
    api.post.mockImplementation(async (url: string) => ({ data: url.endsWith('/sync') ? run : { succeeded: true, resourceId: release } }))
    renderSetup()
    expect(api.get).not.toHaveBeenCalled(); expect(api.post).not.toHaveBeenCalled()
    load(); fireEvent.change(await screen.findByRole('combobox', { name: 'Provider city' }), { target: { value: 'reference-city' } })
    fireEvent.click(screen.getByRole('button', { name: 'Synchronize city districts for review' }))
    fireEvent.change(await screen.findByRole('combobox', { name: 'Destination district' }), { target: { value: 'reference-district' } })
    const save = screen.getByRole('button', { name: 'Save reviewed mapping draft' })
    expect(save).toBeDisabled()
    for (const type of ['country', 'city', 'district']) fireEvent.change(screen.getByRole('combobox', { name: `Canonical ${type}` }), { target: { value: `canonical-${type}` } })
    fireEvent.click(save)
    await screen.findByText(new RegExp(release))
    expect(api.post).toHaveBeenCalledTimes(2)
    expect(api.post.mock.calls[1][1]).toMatchObject({ expectedSourceHashSha256: 'source-hash', catalogReleaseId: 'synthetic-catalog',
        choices: [{ candidateId: 'candidate-country', canonicalPlaceId: 'canonical-country' }, { candidateId: 'candidate-city', canonicalPlaceId: 'canonical-city' },
            { candidateId: 'candidate-district', canonicalPlaceId: 'canonical-district' }] })
    expect(api.get).toHaveBeenCalledWith(expect.stringContaining(`/releases/${release}`))
    expect(api.post.mock.calls.some(([url]) => /create-shipment|activate|pickup-request/.test(url))).toBe(false)
})

it('an independent reviewer can reload the exact saved revision and submit its checksum', async () => {
    api.get.mockImplementation(async (url: string) => ({ data: url.endsWith('/validation') ? { succeeded: true, contentChecksumSha256: 'reviewed-hash' } : { id: release, status: 'Draft', mappings: [] } }))
    api.post.mockResolvedValue({ data: { succeeded: true } })
    renderSetup()
    fireEvent.change(screen.getByRole('textbox', { name: 'Release ID for independent review' }), { target: { value: release } })
    fireEvent.click(screen.getByRole('button', { name: 'Read saved release' }))
    const validate = await screen.findByRole('button', { name: 'Validate independent review' })
    await waitFor(() => expect(validate).toBeEnabled())
    fireEvent.click(validate)
    await waitFor(() => expect(api.post).toHaveBeenCalledWith(expect.stringContaining(`/${release}/validate`), expect.objectContaining({ expectedContentChecksumSha256: 'reviewed-hash' })))
})

it('invalidates a pending reference when credentials change', async () => {
    let finish!: (value: { data: typeof reference }) => void
    api.get.mockImplementation(() => new Promise(resolve => { finish = resolve }))
    const view = renderSetup(); load()
    view.rerender(<BostaGeographySetup accountId="account-1" environment="Production" configurationVersion="rotated" disabled={false} />)
    await act(async () => finish({ data: reference }))
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
    expect(api.post).not.toHaveBeenCalled()
})

it('rejects a foreign account response before showing references', async () => {
    api.get.mockResolvedValue({ data: { ...reference, accountId: 'foreign' } })
    renderSetup(); load(); expect(await screen.findByRole('alert')).toHaveTextContent('BOSTA_REFERENCE_TARGET_MISMATCH')
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
})

it('registers missing geography metadata only after explicit approval and real reference selection', async () => {
    api.get.mockResolvedValue({ data: { ...reference, catalogReleaseId: null, canonicalPlaces: [] } })
    api.post.mockResolvedValue({ data: run })
    renderSetup(); load()
    fireEvent.change(await screen.findByRole('combobox', { name: 'Provider city' }), { target: { value: 'reference-city' } })
    const bootstrap = screen.getByRole('button', { name: 'Bootstrap verified geography provider and draft' })
    expect(bootstrap).toBeDisabled()
    fireEvent.change(screen.getByRole('textbox', { name: 'Geography provider bootstrap reason' }), { target: { value: 'Explicit synthetic provider review' } })
    fireEvent.click(screen.getByRole('checkbox', { name: /Register missing Bosta geography metadata/ }))
    expect(api.post).not.toHaveBeenCalled()
    fireEvent.click(bootstrap)
    await screen.findByRole('combobox', { name: 'Destination district' })
    expect(api.post).toHaveBeenCalledWith(expect.stringContaining('/bootstrap'), { cityId: 'reference-city', reason: 'Explicit synthetic provider review' })
    expect(api.post).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button', { name: 'Save reviewed mapping draft' })).toBeDisabled()
})

it('withholds incompatible-parent districts and cannot save when a selected city is unserviceable', async () => {
    api.get.mockResolvedValue({ data: reference })
    api.post.mockResolvedValue({ data: { ...run, candidates: [...run.candidates.map(c => c.providerPlaceType === 'city' ? { ...c, serviceable: false } : c),
        { ...run.candidates[2], id: 'foreign-candidate', providerPlaceId: 'foreign-district', providerDisplayName: 'Foreign district', providerParentPlaceId: 'foreign-city' }] } })
    renderSetup(); load()
    fireEvent.change(await screen.findByRole('combobox', { name: 'Provider city' }), { target: { value: 'reference-city' } })
    fireEvent.click(screen.getByRole('button', { name: 'Synchronize city districts for review' }))
    fireEvent.change(await screen.findByRole('combobox', { name: 'Destination district' }), { target: { value: 'reference-district' } })
    expect(screen.queryByRole('option', { name: /Foreign district/ })).not.toBeInTheDocument()
    for (const type of ['country', 'city', 'district']) fireEvent.change(screen.getByRole('combobox', { name: `Canonical ${type}` }), { target: { value: `canonical-${type}` } })
    expect(screen.getByRole('button', { name: 'Save reviewed mapping draft' })).toBeDisabled()
    expect(api.post).toHaveBeenCalledTimes(1)
})

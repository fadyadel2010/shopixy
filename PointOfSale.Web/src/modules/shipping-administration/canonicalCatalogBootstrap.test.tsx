import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import CanonicalCatalogBootstrap from './components/CanonicalCatalogBootstrap'

const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../../services/api', () => ({ default: api }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ i18n: { language: 'en' } }) }))
beforeEach(() => vi.resetAllMocks())
afterEach(cleanup)
const id = '11111111-1111-4111-8111-111111111111'
const checksum = 'synthetic-revision-hash'
const saved = { release: { id, releaseKey: 'SYNTHETIC-ONLY', status: 'Draft', createdByUserId: 'synthetic-editor' },
    sourceSummary: 'Synthetic test data, not a production import', sourceEvidenceUri: 'https://synthetic.example.invalid/catalog', sourceEvidenceSha256: 'synthetic-source-hash',
    places: [{ id: 'synthetic-place-id', stableKey: 'TEST-ONLY-PLACE', placeKind: 'Locality', canonicalCode: 'TEST-CITY', parentPlaceId: 'synthetic-parent', countryPlaceId: 'synthetic-country', names: [{ languageTag: 'en', name: 'Synthetic city' }] }],
    countryProfiles: [{ iso2: 'US', requiredLevelCodesForDelivery: ['AREA', 'CITY'] }], validation: { succeeded: true, contentChecksumSha256: checksum } }
const setup = () => render(<CanonicalCatalogBootstrap accountId="account" environment="Production" configurationVersion="v1" />)
const read = async () => {
    fireEvent.change(screen.getByRole('textbox', { name: 'Catalog release ID' }), { target: { value: id } })
    fireEvent.click(screen.getByRole('button', { name: 'Read saved canonical catalog' }))
    await screen.findByText('TEST-ONLY-PLACE')
}

it('requires preview and an explicit reason to stage an exact manifest; never publishes automatically', async () => {
    const manifest = { releaseKey: 'SYNTHETIC-ONLY', places: [] }
    api.post.mockImplementation(async (url: string) => ({ data: url.endsWith('/preview') ? { succeeded: true, manifestChecksumSha256: 'manifest-hash', placeCount: 3, edgeCount: 2, issues: [] } : { succeeded: true, resourceId: id } }))
    api.get.mockResolvedValue({ data: saved })
    setup(); expect(api.get).not.toHaveBeenCalled(); expect(api.post).not.toHaveBeenCalled()
    fireEvent.change(screen.getByRole('textbox', { name: 'Canonical catalog JSON manifest' }), { target: { value: JSON.stringify(manifest) } })
    fireEvent.click(screen.getByRole('button', { name: 'Preview import without saving' }))
    await screen.findByText(/manifest-hash/)
    const stage = screen.getByRole('button', { name: 'Stage canonical import draft only' })
    expect(stage).toBeDisabled()
    fireEvent.change(screen.getByRole('textbox', { name: 'Catalog action reason' }), { target: { value: 'Explicit synthetic draft import' } })
    fireEvent.click(stage); fireEvent.click(stage)
    await screen.findByText('TEST-ONLY-PLACE')
    expect(api.post).toHaveBeenCalledTimes(2)
    expect(api.post.mock.calls[1]).toEqual(['/admin/geographic/catalog/import/drafts', { manifest, expectedManifestChecksumSha256: 'manifest-hash', reason: 'Explicit synthetic draft import' }])
    expect(screen.getByRole('button', { name: 'Publish approved canonical catalog' })).toBeDisabled()
    expect(screen.getByText('synthetic-parent')).toBeVisible()
})

it('allows independent source review and sends the saved source and content revision, with separate explicit publish approval', async () => {
    api.get.mockResolvedValueOnce({ data: saved }).mockResolvedValueOnce({ data: { ...saved, release: { ...saved.release, status: 'Validated' } } }).mockResolvedValue({ data: { ...saved, release: { ...saved.release, status: 'Published' } } })
    api.post.mockResolvedValue({ data: { succeeded: true } })
    setup(); await read()
    fireEvent.change(screen.getByRole('textbox', { name: 'Catalog action reason' }), { target: { value: 'Independent synthetic review' } })
    const validate = screen.getByRole('button', { name: 'Validate independently reviewed catalog' })
    expect(validate).toBeDisabled()
    fireEvent.click(screen.getByRole('checkbox', { name: /I independently reviewed/ }))
    fireEvent.click(validate)
    await waitFor(() => expect(api.post).toHaveBeenCalledWith(`/admin/geographic/catalog/releases/${id}/validate`, {
        expectedContentChecksumSha256: checksum, sourceEvidenceUri: saved.sourceEvidenceUri, sourceEvidenceSha256: saved.sourceEvidenceSha256,
        confirmOfficialSourceReviewed: true, reason: 'Independent synthetic review' }))
    const publish = screen.getByRole('button', { name: 'Publish approved canonical catalog' })
    expect(publish).toBeDisabled()
    await waitFor(() => expect(validate).toBeDisabled())
    fireEvent.click(screen.getByRole('checkbox', { name: /I explicitly approve/ }))
    await waitFor(() => expect(publish).toBeEnabled())
    fireEvent.click(publish)
    await waitFor(() => expect(api.post).toHaveBeenCalledWith(`/admin/geographic/catalog/releases/${id}/publish`, { expectedContentChecksumSha256: checksum, reason: 'Independent synthetic review' }))
    expect(api.post.mock.calls.some(([url]) => /activate|shipment|pickup/.test(url))).toBe(false)
})

it('shows import validation failures and invalidates preview when the proposed manifest changes', async () => {
    api.post.mockResolvedValue({ data: { succeeded: false, issues: [{ code: 'CatalogHierarchyInvalid', stableKey: 'SYNTHETIC' }], placeCount: 3, edgeCount: 1, manifestChecksumSha256: 'invalid-hash' } })
    setup(); const input = screen.getByRole('textbox', { name: 'Canonical catalog JSON manifest' })
    fireEvent.change(input, { target: { value: '{}' } }); fireEvent.click(screen.getByRole('button', { name: 'Preview import without saving' }))
    await screen.findByText(/CatalogHierarchyInvalid/)
    expect(screen.getByRole('button', { name: 'Stage canonical import draft only' })).toBeDisabled()
    fireEvent.change(input, { target: { value: '{"changed":true}' } })
    expect(screen.queryByText(/invalid-hash/)).not.toBeInTheDocument(); expect(api.post).toHaveBeenCalledTimes(1)
})

it('discards pending review responses after credential/environment scope changes', async () => {
    let finish!: (value: { data: typeof saved }) => void
    api.get.mockImplementation(() => new Promise(resolve => { finish = resolve }))
    const view = setup()
    fireEvent.change(screen.getByRole('textbox', { name: 'Catalog release ID' }), { target: { value: id } })
    fireEvent.click(screen.getByRole('button', { name: 'Read saved canonical catalog' }))
    view.rerender(<CanonicalCatalogBootstrap accountId="account" environment="Sandbox" configurationVersion="v2" />)
    await act(async () => finish({ data: saved }))
    expect(screen.queryByText('TEST-ONLY-PLACE')).not.toBeInTheDocument(); expect(api.post).not.toHaveBeenCalled()
})

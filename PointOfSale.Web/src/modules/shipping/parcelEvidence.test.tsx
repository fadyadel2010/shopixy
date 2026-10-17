import { beforeEach, afterEach, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor, cleanup } from '@testing-library/react'
import { createInstance } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import ParcelEvidenceForm from './components/ParcelEvidenceForm'
import en from '../../i18n/locales/en.json'
import ar from '../../i18n/locales/ar.json'

const api = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn(), post: vi.fn() }))
vi.mock('../../services/api', () => ({ default: api }))
beforeEach(() => { vi.resetAllMocks(); api.get.mockImplementation((url: string) => Promise.resolve({ data: {
    shipmentId: url.split('/')[3], canMeasure: true, canQuote: true, quoteBlockers: [], contents: [{ orderItemId: 'line', productId: 'product', quantity: 2 }],
} })) })
afterEach(cleanup)
it.each(['BOSTA_REMOTE_QUOTES_UNSUPPORTED', 'CARRIER_NOT_CERTIFIED', 'PARCEL_QUOTE_LOCKED', 'CARRIER_CONFIGURATION_DISABLED'])(
    'saved measurements cannot enable estimates while backend quote readiness is blocked (%s)', async blocker => {
        api.get.mockResolvedValue({ data: {
            canMeasure: false, canQuote: false, quoteBlockers: [blocker],
            evidence: { measurement: { length: 10, width: 11, height: 12, dimensionUnit: 'cm', grossWeight: 1, weightUnit: 'kg', pieceReference: 'BOX' } },
        } })
        const { label } = await setup('en')
        await screen.findByRole('status')
        const estimate = screen.getByRole('button', { name: label('estimate') })
        expect(estimate).toBeDisabled()
        expect(screen.getByText(new RegExp(blocker))).toBeInTheDocument()
        fireEvent.click(estimate)
        expect(api.post).not.toHaveBeenCalled()
    },
)

it('an older runtime without quote-readiness evidence does not enable an external estimate', async () => {
    api.get.mockResolvedValue({ data: { canMeasure: false, evidence: { measurement: {} } } })
    const { label } = await setup('en')
    await screen.findByRole('status')
    expect(screen.getByRole('button', { name: label('estimate') })).toBeDisabled()
    expect(api.post).not.toHaveBeenCalled()
})

it('measurement save refreshes eligibility and cannot carry a previously ready quote across a backend refusal', async () => {
    api.put.mockResolvedValue({ data: {} })
    const { label } = await setup('en')
    api.get.mockResolvedValue({ data: { shipmentId: 'synthetic-shipment', canMeasure: false, canQuote: false, quoteBlockers: ['CARRIER_CONFIGURATION_DISABLED'] } })
    fill(label)
    fireEvent.click(screen.getByRole('button', { name: label('save') }))
    await screen.findByRole('status')
    await screen.findByText(/CARRIER_CONFIGURATION_DISABLED/)
    expect(api.get).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('button', { name: label('estimate') })).toBeDisabled()
    expect(api.post).not.toHaveBeenCalled()
})

it('a failed eligibility refresh retains the actual successful measurement save but does not enable quotes', async () => {
    api.put.mockResolvedValue({ data: {} })
    const { label } = await setup('en')
    api.get.mockRejectedValue(new Error('synthetic readiness read failure'))
    fill(label)
    fireEvent.click(screen.getByRole('button', { name: label('save') }))
    expect(await screen.findByRole('alert')).toHaveTextContent(label('loadError'))
    expect(screen.getByRole('status')).toHaveTextContent(label('saved'))
    expect(screen.getByRole('button', { name: label('estimate') })).toBeDisabled()
    expect(api.get).toHaveBeenCalledTimes(2)
    expect(api.put).toHaveBeenCalledTimes(1)
    expect(api.post).not.toHaveBeenCalled()
})

it('a foreign-shipment evidence response is rejected before enabling measurements or estimates', async () => {
    api.get.mockResolvedValue({ data: { shipmentId: 'foreign-shipment', canMeasure: true, canQuote: true, quoteBlockers: [] } })
    const { label } = await setup('en')
    expect(await screen.findByRole('alert')).toHaveTextContent('PARCEL_RESPONSE_TARGET_MISMATCH')
    expect(screen.getByRole('button', { name: label('save') })).toBeDisabled()
    expect(screen.getByRole('button', { name: label('estimate') })).toBeDisabled()
    expect(api.put).not.toHaveBeenCalled(); expect(api.post).not.toHaveBeenCalled()
})

async function setup(language: string) {
    const i18n = createInstance()
    await i18n.init({ lng: language, fallbackLng: 'en', resources: { en: { translation: en }, ar: { translation: ar } } })
    const result = render(<I18nextProvider i18n={i18n}><ParcelEvidenceForm shipmentId="synthetic-shipment" /></I18nextProvider>)
    await waitFor(() => expect(api.get).toHaveBeenCalled())
    return { ...result, i18n, label: (key: string) => i18n.t(`shipping.parcel.${key}`) }
}
function fill(label: (key: string) => string) {
    for (const [key, value] of Object.entries({ length: '23.1', width: '14.5', height: '8.9', grossWeight: '1750', pieceReference: 'BOX-1' }))
        fireEvent.change(screen.getByLabelText(label(key)), { target: { value } })
    fireEvent.change(screen.getByLabelText(label('weightUnit')), { target: { value: 'g' } })
}

it('a completed save for the previous shipment cannot lock the new shipment or enable its quote', async () => {
    let resolveSave!: (value: { data: object }) => void
    api.put.mockImplementation(() => new Promise(resolve => { resolveSave = resolve }))
    const { rerender, i18n, label } = await setup('en')
    fill(label)
    fireEvent.click(screen.getByRole('button', { name: label('save') }))
    expect(api.put).toHaveBeenCalledTimes(1)
    rerender(<I18nextProvider i18n={i18n}><ParcelEvidenceForm shipmentId="new-synthetic-shipment" /></I18nextProvider>)
    await waitFor(() => expect(api.get).toHaveBeenCalledWith('/admin/shipments/new-synthetic-shipment/parcel-evidence'))
    await act(async () => { resolveSave({ data: {} }) })
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: label('estimate') })).toBeDisabled()
    expect(screen.getByRole('spinbutton', { name: label('length') })).toHaveValue(null)
    expect(api.post).not.toHaveBeenCalled()
})

it('a late quote for the previous shipment is not displayed as the new shipment price', async () => {
    let resolveQuote!: (value: { data: { quotes: Array<{ serviceName: string, cost: number, currency: string }> } }) => void
    api.post.mockImplementation(() => new Promise(resolve => { resolveQuote = resolve }))
    api.put.mockResolvedValue({ data: {} })
    const { rerender, i18n, label } = await setup('en')
    fill(label)
    fireEvent.click(screen.getByRole('button', { name: label('save') }))
    await screen.findByRole('status')
    fireEvent.click(screen.getByRole('button', { name: label('estimate') }))
    expect(api.post).toHaveBeenCalledTimes(1)
    rerender(<I18nextProvider i18n={i18n}><ParcelEvidenceForm shipmentId="new-synthetic-shipment" /></I18nextProvider>)
    await waitFor(() => expect(api.get).toHaveBeenCalledWith('/admin/shipments/new-synthetic-shipment/parcel-evidence'))
    await act(async () => { resolveQuote({ data: { quotes: [{ serviceName: 'OLD-TARGET-QUOTE', cost: 99, currency: 'USD' }] } }) })
    expect(screen.queryByText(/OLD-TARGET-QUOTE/)).not.toBeInTheDocument()
    expect(api.post).toHaveBeenCalledTimes(1)
})

it('repeated form submission stages only one measurement save without retrying', async () => {
    let resolveSave!: (value: { data: object }) => void
    api.put.mockImplementation(() => new Promise(resolve => { resolveSave = resolve }))
    const { container, label } = await setup('en')
    fill(label)
    const form = container.querySelector('form')
    if (!form) throw new Error('Parcel measurement form must exist')
    fireEvent.submit(form)
    fireEvent.submit(form)
    expect(api.put).toHaveBeenCalledTimes(1)
    await act(async () => { resolveSave({ data: {} }) })
    expect(screen.getByRole('status')).toHaveTextContent(label('saved'))
})

it('a previous-target save failure does not set an error on the newly selected shipment', async () => {
    let rejectSave!: (reason: { response: { data: { code: string } } }) => void
    api.put.mockImplementation(() => new Promise((_, reject) => { rejectSave = reject }))
    const { rerender, i18n, label } = await setup('en')
    fill(label)
    fireEvent.click(screen.getByRole('button', { name: label('save') }))
    expect(api.put).toHaveBeenCalledTimes(1)
    rerender(<I18nextProvider i18n={i18n}><ParcelEvidenceForm shipmentId="new-synthetic-shipment" /></I18nextProvider>)
    await waitFor(() => expect(api.get).toHaveBeenCalledWith('/admin/shipments/new-synthetic-shipment/parcel-evidence'))
    await act(async () => { rejectSave({ response: { data: { code: 'OLD_TARGET_FAILURE' } } }) })
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByRole('spinbutton', { name: label('length') })).toBeEnabled()
    expect(api.put).toHaveBeenCalledTimes(1)
})

for (const language of ['en', 'ar']) {
    it(`${language}: missing/negative measurements cannot submit and unit-labelled packed measurements are explicit`, async () => {
        const { label } = await setup(language)
        expect(screen.getByRole('button', { name: label('save') })).toBeDisabled()
        expect(screen.getByText(label('explanation'))).toBeInTheDocument()
        fill(label)
        await waitFor(() => expect(screen.getByRole('button', { name: label('save') })).toBeEnabled())
        fireEvent.change(screen.getByLabelText(label('grossWeight')), { target: { value: '-1' } })
        expect(screen.getByRole('button', { name: label('save') })).toBeDisabled()
        expect(api.put).not.toHaveBeenCalled()
    })
    it(`${language}: validation error retains original entered measurements and successful save locks evidence`, async () => {
        api.put.mockRejectedValueOnce({ response: { data: { code: 'PARCEL_MEASUREMENTS_OUT_OF_RANGE' } } }).mockResolvedValueOnce({ data: {} })
        const { label } = await setup(language); fill(label)
        await waitFor(() => expect(screen.getByRole('button', { name: label('save') })).toBeEnabled())
        fireEvent.click(screen.getByRole('button', { name: label('save') }))
        await screen.findByRole('alert')
        expect(screen.getByLabelText(label('grossWeight'))).toHaveValue(1750)
        expect(screen.getByLabelText(label('weightUnit'))).toHaveValue('g')
        fireEvent.click(screen.getByRole('button', { name: label('save') }))
        await screen.findByRole('status')
        expect(api.put.mock.calls[0][1]).toEqual(api.put.mock.calls[1][1])
        expect(api.put.mock.calls[1][1]).toEqual({ length: 23.1, width: 14.5, height: 8.9, dimensionUnit: 'cm', grossWeight: 1750, weightUnit: 'g', pieceReference: 'BOX-1', packageCount: 1 })
        expect(screen.getByLabelText(label('length'))).toBeDisabled()
    })
    it(`${language}: missing authority locks inputs and international customs stays explicitly blocked`, async () => {
        api.get.mockResolvedValue({ data: { canMeasure: false, isInternational: true, registrationState: 'ReconciliationRequired' } })
        const { label, container } = await setup(language)
        await waitFor(() => expect(screen.getByText(label('customsBlocked'))).toBeInTheDocument())
        expect(screen.getByLabelText(label('length'))).toBeDisabled()
        expect(screen.getByRole('button', { name: label('estimate') })).toBeDisabled()
        expect(container.querySelector('section')).toHaveAttribute('dir', language === 'ar' ? 'rtl' : 'ltr')
        expect(screen.getByText(/ReconciliationRequired/)).toBeInTheDocument()
    })
    it(`${language}: loaded evidence requests only an estimate and preserves readiness failures`, async () => {
        api.get.mockResolvedValue({ data: { shipmentId: 'synthetic-shipment', canQuote: true, quoteBlockers: [], canMeasure: false, evidence: { measurement: { length: 10, width: 11, height: 12, dimensionUnit: 'cm', grossWeight: 1, weightUnit: 'kg', pieceReference: 'BOX' } } } })
        api.post.mockRejectedValueOnce({ response: { data: { errorCode: 'DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED' } } })
            .mockResolvedValueOnce({ data: { quotes: [{ serviceName: 'DHL estimate', cost: 123.45, currency: 'EGP' }] } })
        const { label } = await setup(language); await screen.findByRole('status')
        fireEvent.click(screen.getByRole('button', { name: label('estimate') }))
        expect(await screen.findByRole('alert')).toHaveTextContent('DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED')
        fireEvent.click(screen.getByRole('button', { name: label('estimate') }))
        await screen.findByText('DHL estimate: 123.45 EGP')
        expect(screen.getByText(label('estimateOnly'))).toBeInTheDocument()
        expect(api.post).toHaveBeenCalledWith('/admin/shipments/synthetic-shipment/parcel-quote')
        expect(api.put).not.toHaveBeenCalled()
    })
}

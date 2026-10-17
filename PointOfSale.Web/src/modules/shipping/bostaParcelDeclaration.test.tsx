import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { createInstance } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import ParcelEvidenceForm from './components/ParcelEvidenceForm'
import en from '../../i18n/locales/en.json'
import ar from '../../i18n/locales/ar.json'

const api = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn(), post: vi.fn() }))
vi.mock('../../services/api', () => ({ default: api }))
const view = (id: string, bosta = true) => ({ shipmentId: id, canMeasure: true, canQuote: false,
    requiresGoodsDeclaration: bosta, carrierCode: bosta ? 'bosta' : 'dhl', quoteBlockers: ['BOSTA_REMOTE_QUOTES_UNSUPPORTED'], contents: [] })
beforeEach(() => { vi.resetAllMocks(); api.get.mockImplementation((url: string) => Promise.resolve({ data: view(url.split('/')[3]) })); api.put.mockResolvedValue({ data: {} }) })
afterEach(cleanup)

async function setup(language: string) {
    const i18n = createInstance()
    await i18n.init({ lng: language, fallbackLng: 'en', resources: { en: { translation: en }, ar: { translation: ar } } })
    const result = render(<I18nextProvider i18n={i18n}><ParcelEvidenceForm shipmentId="synthetic-bosta" /></I18nextProvider>)
    const label = (key: string) => i18n.t(`shipping.parcel.${key}`)
    await screen.findByLabelText(label('goodsValue'))
    for (const [key, value] of Object.entries({ length: '20', width: '15', height: '10', grossWeight: '1.25', pieceReference: 'SYNTHETIC-BOX' }))
        fireEvent.change(screen.getByLabelText(label(key)), { target: { value } })
    return { ...result, i18n, label }
}

it.each(['en', 'ar'])('requires a separate accurate goods declaration and submits it without defaulting value (%s)', async language => {
    const { label } = await setup(language)
    expect(screen.getByLabelText(label('goodsValue'))).toHaveValue(null)
    expect(screen.getByRole('button', { name: label('save') })).toBeDisabled()
    fireEvent.change(screen.getByLabelText(label('goodsDescription')), { target: { value: 'Synthetic packed goods' } })
    fireEvent.change(screen.getByLabelText(label('goodsValue')), { target: { value: '75.25' } })
    fireEvent.click(screen.getByRole('button', { name: label('save') }))
    await screen.findByRole('status')
    expect(api.put).toHaveBeenCalledExactlyOnceWith('/admin/shipments/synthetic-bosta/parcel-evidence', {
        length: 20, width: 15, height: 10, grossWeight: 1.25, dimensionUnit: 'cm', weightUnit: 'kg', pieceReference: 'SYNTHETIC-BOX', packageCount: 1,
        goods: { description: 'Synthetic packed goods', value: 75.25, currency: 'EGP' },
    })
    expect(api.post).not.toHaveBeenCalled()
})

it('retains a failed declaration for correction but clears it when the shipment changes', async () => {
    api.put.mockRejectedValue({ response: { data: { code: 'BOSTA_GOODS_VALUE_INVALID' } } })
    const { label, rerender, i18n } = await setup('en')
    fireEvent.change(screen.getByLabelText(label('goodsDescription')), { target: { value: 'Synthetic packed goods' } })
    fireEvent.change(screen.getByLabelText(label('goodsValue')), { target: { value: '75.25' } })
    fireEvent.click(screen.getByRole('button', { name: label('save') }))
    await screen.findByRole('alert')
    expect(screen.getByLabelText(label('goodsValue'))).toHaveValue(75.25)
    await act(async () => rerender(<I18nextProvider i18n={i18n}><ParcelEvidenceForm shipmentId="other-synthetic-bosta" /></I18nextProvider>))
    await waitFor(() => expect(screen.getByLabelText(label('goodsValue'))).toHaveValue(null))
    expect(screen.getByLabelText(label('goodsDescription'))).toHaveValue('')
})

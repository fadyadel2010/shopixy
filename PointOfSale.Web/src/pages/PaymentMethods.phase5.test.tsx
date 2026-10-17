import { act } from 'react'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import PaymentMethods, { mergePaymentMethodCatalog } from './PaymentMethods'

const api = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn(), post: vi.fn() }))
vi.mock('../services/api', () => ({ default: api }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key: string, fallback?: string) => fallback || key, i18n: { language: 'en' } }) }))
vi.mock('../contexts/SettingsContext', () => ({ useSettings: () => ({ formatCurrency: (n: number) => `${n}`, settings: {} }) }))
const catalog = [{ code: 'COD', displayName: 'Cash on Delivery', sortOrder: 0, requiresConfiguration: false }]
beforeEach(() => {
    vi.resetAllMocks()
    api.get.mockImplementation((path: string) => Promise.resolve({ data: path.endsWith('catalog') ? catalog : [] }))
})
describe('Phase 5 payment editor safety', () => {
    it('retains zero display order ahead of positive order', () => {
        expect(mergePaymentMethodCatalog([...catalog, { code: 'STRIPE', sortOrder: 2 }], []).map((m: { code: string }) => m.code)).toEqual(['COD', 'STRIPE'])
    })
    it('excludes same-tick duplicate configuration mutations', async () => {
        let resolve: ((value: unknown) => void) | undefined
        api.put.mockImplementation(() => new Promise(r => { resolve = r }))
        render(<PaymentMethods />)
        const row = (await screen.findByText('Cash on Delivery')).closest('tr')!
        const button = within(row).getByRole('button', { name: 'Configure' })
        act(() => { fireEvent.click(button); fireEvent.click(button) })
        expect(api.put).toHaveBeenCalledTimes(1)
        await act(async () => { resolve?.({ data: {} }) })
    })
    it('does not announce complete success when accepted configuration cannot be read back', async () => {
        api.put.mockResolvedValue({ data: {} })
        render(<PaymentMethods />)
        const row = (await screen.findByText('Cash on Delivery')).closest('tr')!
        api.get.mockRejectedValue(new Error('read unavailable'))
        fireEvent.click(within(row).getByRole('button', { name: 'Configure' }))
        await waitFor(() => expect(screen.queryByText('common.updatedSuccessfully')).not.toBeInTheDocument())
        expect(await screen.findByText(/saved.*reload/i)).toBeInTheDocument()
    })
})

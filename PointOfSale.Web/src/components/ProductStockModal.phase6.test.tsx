import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import ProductStockModal from './ProductStockModal'

const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), invalidate: vi.fn() }))
const t = (key: string) => key
vi.mock('../services/api', () => ({ default: api }))
vi.mock('../services/posRuntimeCache', () => ({ invalidatePosRuntimeCache: api.invalidate }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t }) }))
type StockResponse = { data: { warehouseId: string; warehouseName: string; quantity: number; reservedQuantity: number; availableQuantity: number }[] }
const response = (name: string): StockResponse => ({ data: [{ warehouseId: name, warehouseName: name, quantity: 3, reservedQuantity: 1, availableQuantity: 2 }] })

beforeEach(() => vi.clearAllMocks())
afterEach(() => vi.restoreAllMocks())
describe('product stock request lifecycle', () => {
    it('never lets an older product response replace the currently selected product stock', async () => {
        let finishOld!: (value: StockResponse) => void
        api.get.mockImplementationOnce(() => new Promise<StockResponse>(resolve => { finishOld = resolve }))
            .mockResolvedValueOnce(response('current warehouse'))
        const view = render(<ProductStockModal productId="old" productName="Old" onClose={vi.fn()} />)
        view.rerender(<ProductStockModal productId="current" productName="Current" onClose={vi.fn()} />)
        await screen.findByText('current warehouse')
        await act(async () => finishOld(response('old warehouse')))
        expect(screen.queryByText('old warehouse')).not.toBeInTheDocument()
        expect(screen.getByText('current warehouse')).toBeInTheDocument()
        expect(api.get).toHaveBeenCalledTimes(2)
        expect(api.post).not.toHaveBeenCalled()
    })

    it('shows a bounded load failure without logging request headers or customer payloads', async () => {
        const log = vi.spyOn(console, 'error').mockImplementation(() => {})
        api.get.mockRejectedValue({ response: { status: 503, data: { secret: 'private-payload' } }, config: { headers: { Authorization: 'private-token' } } })
        render(<ProductStockModal productId="current" productName="Current" onClose={vi.fn()} />)
        await screen.findByText('inventoryManagement.failedToLoadStock')
        expect(log).toHaveBeenCalledExactlyOnceWith('Failed to load product stock:', { status: 503 })
        expect(api.get).toHaveBeenCalledTimes(1)
        await waitFor(() => expect(api.post).not.toHaveBeenCalled())
    })

    it('single-flights adjustment and does not let old success reset the next product form', async () => {
        let finishAdjustment!: (value: object) => void
        api.get.mockResolvedValueOnce(response('old warehouse')).mockResolvedValueOnce(response('current warehouse'))
        api.post.mockImplementation(() => new Promise(resolve => { finishAdjustment = resolve }))
        const view = render(<ProductStockModal productId="old" productName="Old" canAdjust onClose={vi.fn()} />)
        await screen.findByText('old warehouse')
        fireEvent.click(screen.getByRole('button', { name: /inventoryManagement.adjustStock/ }))
        const oldForm = view.container.querySelector('form')!
        fireEvent.change(oldForm.querySelector('select')!, { target: { value: 'old warehouse' } })
        fireEvent.change(oldForm.querySelector('input[type=number]')!, { target: { value: '1' } })
        fireEvent.change(oldForm.querySelector('input[type=text]')!, { target: { value: 'Old reason' } })
        act(() => { fireEvent.submit(oldForm); fireEvent.submit(oldForm) })
        expect(api.post).toHaveBeenCalledExactlyOnceWith('/products/old/adjust-stock', {
            warehouseId: 'old warehouse', quantityChange: 1, reason: 'Old reason',
        })
        view.rerender(<ProductStockModal productId="current" productName="Current" canAdjust onClose={vi.fn()} />)
        await screen.findByText('current warehouse')
        fireEvent.click(screen.getByRole('button', { name: /inventoryManagement.adjustStock/ }))
        const currentInput = view.container.querySelector('form input[type=text]')!
        fireEvent.change(currentInput, { target: { value: 'Current draft' } })
        await act(async () => finishAdjustment({}))
        expect(currentInput).toHaveValue('Current draft')
        expect(api.get).toHaveBeenCalledTimes(2)
        expect(api.invalidate).toHaveBeenCalledExactlyOnceWith({ reason: 'stock-adjusted' })
    })

    it('ignores failure from an old product without logging it against the current product', async () => {
        let rejectOld!: (value: unknown) => void
        const log = vi.spyOn(console, 'error').mockImplementation(() => {})
        api.get.mockImplementationOnce(() => new Promise<StockResponse>((_, reject) => { rejectOld = reject }))
            .mockResolvedValueOnce(response('current warehouse'))
        const view = render(<ProductStockModal productId="old" productName="Old" onClose={vi.fn()} />)
        view.rerender(<ProductStockModal productId="current" productName="Current" onClose={vi.fn()} />)
        await screen.findByText('current warehouse')
        await act(async () => rejectOld(new Error('private old request')))
        expect(screen.queryByText('inventoryManagement.failedToLoadStock')).not.toBeInTheDocument()
        expect(log).not.toHaveBeenCalled()
        expect(screen.getByText('current warehouse')).toBeInTheDocument()
    })

    it('invalidates reads on unmount and does not retry or log an unmounted failure', async () => {
        let rejectRead!: (value: unknown) => void
        const log = vi.spyOn(console, 'error').mockImplementation(() => {})
        api.get.mockImplementationOnce(() => new Promise<StockResponse>((_, reject) => { rejectRead = reject }))
        const view = render(<ProductStockModal productId="current" productName="Current" onClose={vi.fn()} />)
        view.unmount()
        await act(async () => rejectRead(new Error('private unmounted request')))
        expect(log).not.toHaveBeenCalled()
        expect(api.get).toHaveBeenCalledTimes(1)
        expect(api.post).not.toHaveBeenCalled()
    })
})

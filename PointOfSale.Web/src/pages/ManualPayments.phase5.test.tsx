import { act } from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ManualPayments from './ManualPayments'

const mocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), confirm: vi.fn(), showError: vi.fn(), showSuccess: vi.fn() }))
vi.mock('../services/api', () => ({ default: mocks, isRequestCanceled: () => false }))
vi.mock('../common/confirm/ConfirmDialogProvider', () => ({ useConfirmDialog: () => ({ confirm: mocks.confirm }) }))
vi.mock('../components/Toast', () => ({ useToast: () => ({ showError: mocks.showError, showSuccess: mocks.showSuccess }) }))
vi.mock('react-i18next', () => {
    const t = (key: string, fallback?: string) => fallback || key
    return { useTranslation: () => ({ t }) }
})
vi.mock('../hooks/useRTL', () => ({ useRTL: () => ({ dir: 'ltr', isRTL: false, locale: 'en' }) }))
const payment = { paymentIntentId: '11111111-1111-4111-8111-111111111111', orderId: '22222222-2222-4222-8222-222222222222', orderNumber: 'P5QA', currency: 'EGP', amount: 17.23, createdAtUtc: new Date().toISOString() }
beforeEach(() => {
    vi.resetAllMocks()
    mocks.get.mockResolvedValue({ data: { items: [payment], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 } })
    mocks.post.mockResolvedValue({ data: { message: 'confirmed' } })
})
describe('Phase 5 Manual payment review lifetime', () => {
    it('makes one confirmation and never sends approval after the workspace unmounts', async () => {
        let resolve: ((value: boolean) => void) | undefined
        mocks.confirm.mockImplementation(() => new Promise<boolean>(r => { resolve = r }))
        const view = render(<MemoryRouter><ManualPayments /></MemoryRouter>)
        const approve = await screen.findByRole('button', { name: 'ManualPayments.actions.approve' })
        act(() => { fireEvent.click(approve); fireEvent.click(approve) })
        expect(mocks.confirm).toHaveBeenCalledTimes(1)
        view.unmount()
        await act(async () => { resolve?.(true) })
        expect(mocks.post).not.toHaveBeenCalled()
    })
    it('does not announce a fully refreshed success after accepted approval and failed pending read', async () => {
        mocks.confirm.mockResolvedValue(true)
        render(<MemoryRouter><ManualPayments /></MemoryRouter>)
        const approve = await screen.findByRole('button', { name: 'ManualPayments.actions.approve' })
        mocks.get.mockRejectedValue(new Error('read unavailable'))
        fireEvent.click(approve)
        await waitFor(() => expect(mocks.showError).toHaveBeenCalledWith(expect.stringMatching(/saved.*reload/i)))
        expect(mocks.post).toHaveBeenCalledTimes(1)
        expect(mocks.showSuccess).not.toHaveBeenCalled()
    })
})

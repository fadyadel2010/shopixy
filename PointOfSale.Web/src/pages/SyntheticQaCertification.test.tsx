import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import SyntheticQaCertification from './SyntheticQaCertification'

const mocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../services/api', () => ({ default: mocks }))

describe('Synthetic QA panel', () => {
    beforeEach(() => vi.clearAllMocks())
    it('offers no actions when the server refuses QA admission', async () => {
        mocks.get.mockRejectedValue({ response: { status: 404 } })
        render(<SyntheticQaCertification />)
        await waitFor(() => expect(mocks.get).toHaveBeenCalled())
        expect(screen.getByText('QA certification unavailable')).toBeTruthy()
        expect(screen.queryByRole('button')).toBeNull()
    })
    it('labels simulations explicitly and uses server-issued evidence without money inputs', async () => {
        mocks.get.mockResolvedValue({ data: { enabled: true } })
        mocks.post.mockResolvedValue({ data: { id: 'server-evidence', kind: 'Collection', amount: 35, currency: 'EGP' } })
        render(<SyntheticQaCertification />)
        await screen.findByText('SYNTHETIC QA — NO REAL DELIVERY OR MONEY')
        for (const name of ['runId', 'orderId', 'shipmentId']) fireEvent.change(screen.getByLabelText(name), { target: { value: name } })
        fireEvent.click(screen.getByRole('button', { name: 'Issue collection evidence' }))
        await waitFor(() => expect(mocks.post).toHaveBeenCalledWith('/qa/affiliate-certification/evidence', {
            runId: 'runId', orderId: 'orderId', shipmentId: 'shipmentId', kind: 'Collection',
        }))
        await waitFor(() => expect((screen.getByLabelText('evidenceId') as HTMLInputElement).value).toBe('server-evidence'))
        fireEvent.click(screen.getByRole('button', { name: 'Simulate collection — NO MONEY' }))
        await waitFor(() => expect(mocks.post).toHaveBeenLastCalledWith('/qa/affiliate-certification/collect', {
            orderId: 'orderId', evidenceId: 'server-evidence',
        }))
    })
})

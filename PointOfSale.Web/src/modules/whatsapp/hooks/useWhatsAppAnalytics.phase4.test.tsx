import { renderHook, act } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
const api = vi.hoisted(() => ({ get: vi.fn() }))
vi.mock('../../../services/api', () => ({ default: api }))
import { useWhatsAppAnalytics } from './useWhatsAppAnalytics'
beforeEach(() => vi.clearAllMocks())
it('maps the actual log DTO, computes pagination and sends the supported type filter', async () => {
    api.get.mockResolvedValue({ data: { items: [{ id: 'qa', targetPhone: 'synthetic', messageType: 'OTP', status: 'SENT', failureReason: 'QA reason' }], totalCount: 41, page: 1, pageSize: 20 } })
    const { result } = renderHook(() => useWhatsAppAnalytics())
    let data: Record<string, unknown> = {}
    await act(async () => { data = await result.current.getLogs({ type: 'Otp' }, 1) })
    expect(api.get.mock.calls[0][0]).toContain('messageType=OTP')
    expect(data.totalPages).toBe(3)
    expect((data.items as Record<string, unknown>[])[0]).toMatchObject({ phoneNumber: 'synthetic', type: 'Otp', status: 'Sent', errorMessage: 'QA reason' })
})
it('normalizes order-confirmation labels without inventing unknown transport states', async () => {
    api.get.mockResolvedValue({ data: { items: [{ messageType: 'ORDER_CONFIRMATION', status: 'DELIVERED' }, { messageType: 'FUTURE_TYPE', status: 'FUTURE_STATUS' }], totalCount: 2, pageSize: 20 } })
    const { result } = renderHook(() => useWhatsAppAnalytics())
    let data: Record<string, unknown> = {}
    await act(async () => { data = await result.current.getLogs() })
    expect(data.items).toEqual(expect.arrayContaining([
        expect.objectContaining({ type: 'OrderConfirmation', status: 'Delivered' }),
        expect.objectContaining({ type: 'FUTURE_TYPE', status: 'FUTURE_STATUS' }),
    ]))
})
it('maps actual analytics totals and does not invent unsupported delivery funnel counts', async () => {
    api.get.mockResolvedValue({ data: { totalMessagesSent: 7, totalMessagesFailed: 2, successRate: 77.8, messagesByType: {} } })
    const { result } = renderHook(() => useWhatsAppAnalytics())
    let data: Record<string, unknown> = {}
    await act(async () => { data = await result.current.getAnalytics() })
    expect(data).toMatchObject({ sentMessages: 7, failedMessages: 2, successRate: 77.8 })
    expect(data.totalMessages).toBeUndefined()
    expect(data.funnel).toBeUndefined()
})

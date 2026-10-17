import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import i18n from '../i18n/config'
import { useAuthStore } from '../store/authStore'
import BlockedPhones from './BlockedPhones'
import { OrderDetailsPanel } from './Orders'
import type { AdminOrderDetails } from '../services/adminOrdersApi'
import type { ReactElement } from 'react'

const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
const confirm = vi.hoisted(() => vi.fn())
vi.mock('../services/api', () => ({ default: api }))
vi.mock('../common/confirm/ConfirmDialogProvider', () => ({ useConfirmDialog: () => ({ confirm }) }))

const fixture = { id: 'trust-phone', phoneNumber: '+201000000099', blockedAtUtc: '2026-10-16T12:30:00Z', blockedReason: 'QA', blockedByUser: 'QA' }
const order: AdminOrderDetails = { orderId: 'trust-order', orderNumber: 'TRUST-QA', tenantId: 'tenant', status: 'Pending', paymentStatus: 'Pending', fulfillmentStatus: 'Created', source: 'Ecommerce', rowVersion: '1', items: [], payments: [] }
const trust = { orderId: order.orderId, hasPhone: true, phoneNumber: fixture.phoneNumber, previousOrdersCount: 1, cancelledOrdersCount: 0, riskLevel: 'SAFE', riskSignals: [], phoneRisk: { isBlocked: true, spamReports: 0, globalSpamReports: 0 } }
function mount(ui: ReactElement, client = new QueryClient({ defaultOptions: { queries: { retry: false } } }), route = '/') {
    return { client, ...render(<MemoryRouter initialEntries={[route]}><QueryClientProvider client={client}>{ui}</QueryClientProvider></MemoryRouter>) }
}
beforeEach(async () => {
    vi.clearAllMocks()
    useAuthStore.setState({ user: null })
    await i18n.changeLanguage('en')
    confirm.mockResolvedValue(true)
    api.get.mockResolvedValue({ data: { items: [fixture], totalCount: 1, totalPages: 1 } })
    api.post.mockResolvedValue({ data: { isBlocked: false } })
})
afterEach(async () => { await i18n.changeLanguage('en') })

it('renders the populated blocked-phone workflow in Arabic without English fallbacks', async () => {
    await i18n.changeLanguage('ar')
    mount(<BlockedPhones />)
    expect(await screen.findByRole('heading', { name: 'أرقام الهواتف المحظورة' })).toBeTruthy()
    expect(await screen.findByRole('columnheader', { name: 'رقم الهاتف' })).toBeTruthy()
    expect(screen.getByRole('textbox', { name: 'البحث برقم الهاتف أو سبب الحظر' })).toBeTruthy()
    expect(screen.getByText(new Date(fixture.blockedAtUtc).toLocaleString('ar-EG'))).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'إلغاء الحظر' }))
    await waitFor(() => expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ confirmLabel: 'إلغاء الحظر', description: expect.stringContaining('طلبات') })))
    expect(screen.queryByText('Blocked Phones')).toBeNull()
})
it('does not label a manually blocked phone Safe even if its heuristic risk score is SAFE', () => {
    mount(<OrderDetailsPanel order={order} onClose={() => {}} customerTrust={trust} />)
    expect(screen.queryByText('Safe', { exact: true })).toBeNull()
    expect(screen.getByText('This phone number is currently blocked.', { exact: true })).toBeTruthy()
})
it('does not show Safe when no usable phone identity exists', () => {
    mount(<OrderDetailsPanel order={order} onClose={() => {}} customerTrust={{ ...trust, hasPhone: false, phoneNumber: null, phoneRisk: null }} />)
    expect(screen.queryByText('Safe', { exact: true })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Block Phone' })).toBeNull()
})
it('links trust evidence to the corresponding blocked-phone search', () => {
    mount(<OrderDetailsPanel order={order} onClose={() => {}} customerTrust={trust} />)
    expect(screen.getByRole('link', { name: 'Manage blocked phones' })).toHaveAttribute('href', '/ecommerce/blocked-phones?search=%2B201000000099')
})
it('invalidates all current-workspace trust records after unblock, without invalidating another tenant', async () => {
    const client = new QueryClient()
    const keys = [
        ['pos-admin-orders', 'anonymous', 'anonymous', 'customer-trust', 'order-one'],
        ['pos-admin-orders', 'anonymous', 'anonymous', 'customer-trust', 'order-two'],
        ['pos-admin-orders', 'another-tenant', 'another-user', 'customer-trust', 'order-three'],
    ]
    keys.forEach(key => client.setQueryData(key, { phoneRisk: { isBlocked: true } }))
    mount(<BlockedPhones />, client)
    fireEvent.click(await screen.findByRole('button', { name: 'Unblock' }))
    await waitFor(() => expect(api.post).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(client.getQueryState(keys[0])?.isInvalidated).toBe(true))
    expect(client.getQueryState(keys[1])?.isInvalidated).toBe(true)
    expect(client.getQueryState(keys[2])?.isInvalidated).toBe(false)
})
it('honors the trust-card deep link without requiring the operator to retype the identity', async () => {
    mount(<BlockedPhones />, undefined, '/ecommerce/blocked-phones?search=%2B201000000099')
    await waitFor(() => expect(api.get).toHaveBeenCalled())
    expect(api.get.mock.calls[0][0]).toContain('search=%2B201000000099')
    expect(screen.getByRole('textbox')).toHaveAttribute('dir', 'ltr')
})
it('excludes duplicate unblock submissions while confirmation is pending', async () => {
    let resolve: (value: boolean) => void = () => {}
    confirm.mockImplementation(() => new Promise<boolean>(r => { resolve = r }))
    mount(<BlockedPhones />)
    const button = await screen.findByRole('button', { name: 'Unblock' })
    fireEvent.click(button); fireEvent.click(button)
    expect(confirm).toHaveBeenCalledTimes(1)
    await act(async () => { resolve(true) })
    expect(api.post).toHaveBeenCalledTimes(1)
})
it('cancels unblock without changing enforcement or invalidating trusted data', async () => {
    confirm.mockResolvedValue(false)
    const client = new QueryClient()
    const key = ['pos-admin-orders', 'anonymous', 'anonymous', 'customer-trust', 'order-one']
    client.setQueryData(key, trust)
    mount(<BlockedPhones />, client)
    fireEvent.click(await screen.findByRole('button', { name: 'Unblock' }))
    await waitFor(() => expect(confirm).toHaveBeenCalledTimes(1))
    expect(api.post).not.toHaveBeenCalled()
    expect(client.getQueryState(key)?.isInvalidated).toBe(false)
})
it('distinguishes committed unblock with failed readback from a failed mutation', async () => {
    mount(<BlockedPhones />)
    const button = await screen.findByRole('button', { name: 'Unblock' })
    api.get.mockRejectedValue(new Error('offline'))
    fireEvent.click(button)
    expect(await screen.findByRole('alert')).toHaveTextContent('The phone was unblocked, but the list could not be refreshed.')
    expect(api.post).toHaveBeenCalledTimes(1)
})
it('returns to the last valid page after its only blocked row is removed', async () => {
    let removed = false
    api.post.mockImplementation(async () => { removed = true; return { data: { isBlocked: false } } })
    api.get.mockImplementation(async (path: string) => ({ data: { items: path.includes('page=2') && removed ? [] : [fixture], totalCount: removed ? 20 : 21, totalPages: removed ? 1 : 2 } }))
    mount(<BlockedPhones />)
    fireEvent.click(await screen.findByRole('button', { name: 'Next' }))
    await waitFor(() => expect(api.get.mock.calls.some(([path]) => path.includes('page=2'))).toBe(true))
    fireEvent.click(await screen.findByRole('button', { name: 'Unblock' }))
    await waitFor(() => expect(api.post).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(api.get.mock.calls[api.get.mock.calls.length - 1]?.[0]).toContain('page=1'))
    expect(screen.queryByRole('button', { name: 'Next' })).toBeNull()
})

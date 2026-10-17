import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
const customers = vi.hoisted(() => ({ getOnlineCustomers: vi.fn() }))
const confirm = vi.hoisted(() => vi.fn())
const toast = vi.hoisted(() => ({ showError: vi.fn() }))
const translate = (key: string) => key
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: translate, i18n: { language: 'en', dir: () => 'ltr' } }) }))
vi.mock('../services/api', () => ({ default: api }))
vi.mock('../components/Toast', () => ({ useToast: () => toast }))
vi.mock('../common/confirm/ConfirmDialogProvider', () => ({ useConfirmDialog: () => ({ confirm }) }))
vi.mock('../modules/onlineCustomers/services', () => ({ onlineCustomersApi: customers }))
vi.mock('../modules/onlineCustomers/components', () => ({ OnlineCustomerDetails: () => null }))
import BlockedPhones from './BlockedPhones'
import OnlineCustomersPage from '../modules/onlineCustomers/pages/OnlineCustomersPage'
beforeEach(() => vi.clearAllMocks())
it('shows an unavailable block list rather than a successful empty list after failure', async () => {
    api.get.mockRejectedValue(new Error('offline'))
    render(<MemoryRouter><QueryClientProvider client={new QueryClient()}><BlockedPhones /></QueryClientProvider></MemoryRouter>)
    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.queryByText('blockedPhones.empty')).toBeNull()
})
it('rejects a late earlier block-list search response', async () => {
    let old: (value: unknown) => void = () => {}
    api.get.mockImplementationOnce(() => new Promise(resolve => { old = resolve }))
        .mockResolvedValue({ data: { items: [], totalCount: 0, totalPages: 0 } })
    render(<MemoryRouter><QueryClientProvider client={new QueryClient()}><BlockedPhones /></QueryClientProvider></MemoryRouter>)
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'new search' } })
    await waitFor(() => expect(api.get).toHaveBeenCalledTimes(2))
    await act(async () => { old({ data: { items: [{ id: 'old', phoneNumber: 'old result' }], totalCount: 1, totalPages: 1 } }) })
    expect(screen.queryByText('old result')).toBeNull()
})
it('shows registered-customer read failure, not a fabricated empty account directory', async () => {
    customers.getOnlineCustomers.mockRejectedValue(new Error('offline'))
    render(<OnlineCustomersPage />)
    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.queryByText('common.noData')).toBeNull()
})

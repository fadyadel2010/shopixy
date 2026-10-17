import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import Customers from './Customers'
import Suppliers from './Suppliers'

const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn() }))
const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../services/api', () => ({ default: api }))
vi.mock('../components/Toast', () => ({ useToast: () => toast }))
vi.mock('../contexts/SettingsContext', () => ({ useSettings: () => ({ formatCurrency: (v: number) => String(v) }) }))
vi.mock('../hooks/useRTL', () => ({ useRTL: () => ({ dir: 'ltr', isRTL: false }) }))
vi.mock('../components/ValidationDeleteModal', () => ({ default: () => null }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key: string) => key }) }))

describe('party editor request ownership', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        api.get.mockResolvedValue({ data: { items: [], totalPages: 1 } })
    })

    it.each([['customers', Customers, 'customers.addCustomer'], ['suppliers', Suppliers, 'suppliers.addSupplier']] as const)(
        '%s sends one creation while pending, retains input on failure, then allows retry', async (kind, Page, addLabel) => {
            let refuse!: (error: unknown) => void
            api.post.mockImplementationOnce(() => new Promise((_, reject) => { refuse = reject }))
            api.post.mockResolvedValueOnce({ data: {} })
            const { container } = render(<MemoryRouter><Page /></MemoryRouter>)
            await waitFor(() => expect(api.get).toHaveBeenCalled())
            fireEvent.click(screen.getByRole('button', { name: addLabel }))
            const form = container.querySelectorAll('form')[1]
            const name = form.querySelector('input')!
            fireEvent.change(name, { target: { value: 'SYNTHETIC fixture only' } })
            act(() => { fireEvent.submit(form); fireEvent.submit(form) })
            expect(api.post).toHaveBeenCalledExactlyOnceWith('/' + kind, expect.objectContaining({ name: 'SYNTHETIC fixture only' }))
            expect(screen.getByRole('button', { name: 'common.save' })).toBeDisabled()
            expect(screen.getByRole('button', { name: 'common.cancel' })).toBeDisabled()
            await act(async () => refuse(new Error('Synthetic failure')))
            expect(name).toHaveValue('SYNTHETIC fixture only')
            expect(toast.showSuccess).not.toHaveBeenCalled()
            fireEvent.submit(form)
            await waitFor(() => expect(api.post).toHaveBeenCalledTimes(2))
            await waitFor(() => expect(container.querySelectorAll('form')).toHaveLength(1))
        })

    it('does not replace a newer supplier search with a slow older response', async () => {
        let old!: (value: unknown) => void
        api.get.mockImplementation((_path, options) => options.params.search === 'older'
            ? new Promise(resolve => { old = resolve })
            : Promise.resolve({ data: { items: options.params.search === 'newer' ? [{ id: 'new', name: 'Newest supplier' }] : [], totalPages: 1 } }))
        render(<MemoryRouter><Suppliers /></MemoryRouter>)
        const input = screen.getByPlaceholderText('suppliers.searchPlaceholder')
        fireEvent.change(input, { target: { value: 'older' } })
        fireEvent.change(input, { target: { value: 'newer' } })
        await screen.findByText('Newest supplier')
        await act(async () => old({ data: { items: [{ id: 'old', name: 'Obsolete supplier' }], totalPages: 1 } }))
        expect(screen.getByText('Newest supplier')).toBeInTheDocument()
        expect(screen.queryByText('Obsolete supplier')).not.toBeInTheDocument()
    })
})

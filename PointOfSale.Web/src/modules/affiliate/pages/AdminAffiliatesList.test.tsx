import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import AdminAffiliatesList from './AdminAffiliatesList'

const list = vi.hoisted(() => vi.fn())
vi.mock('../../../services/adminAffiliateApi', () => ({ default: { listAffiliates: list } }))
vi.mock('../../../contexts/SettingsContext', () => ({ useSettings: () => ({ formatCurrency: String }) }))
vi.mock('../../../hooks/useAffiliateReadiness', () => ({ useAffiliateReadiness: () => ({ loading: false, isReady: true }) }))

describe('Merchant affiliate visibility', () => {
    beforeEach(() => list.mockReset())
    it('explains the merchant earnings scope instead of claiming no global enrollments', async () => {
        list.mockResolvedValue({ affiliates: [], totalCount: 0, totalPages: 0 })
        render(<MemoryRouter><AdminAffiliatesList /></MemoryRouter>)
        expect(await screen.findByText('No Affiliate Earnings for This Store Yet')).toBeInTheDocument()
        expect(screen.getByText(/Enrollment and promotions in other stores do not add/)).toBeInTheDocument()
        expect(screen.queryByText('No Affiliates Yet')).not.toBeInTheDocument()
    })
    it('shows failed requests as retryable errors, never an empty registry', async () => {
        list.mockRejectedValueOnce(new Error('unavailable'))
        render(<MemoryRouter><AdminAffiliatesList /></MemoryRouter>)
        expect(await screen.findByRole('alert')).toHaveTextContent('Unable to load')
        expect(screen.queryByText('No Affiliate Earnings for This Store Yet')).not.toBeInTheDocument()
        list.mockResolvedValue({ affiliates: [], totalCount: 0, totalPages: 0 })
        fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
        expect(await screen.findByText('No Affiliate Earnings for This Store Yet')).toBeInTheDocument()
    })
})

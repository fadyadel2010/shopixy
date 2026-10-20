import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import SmartOffersPage from './SmartOffersPage'

const mocks = vi.hoisted(() => ({
    listAdminSmartOffers: vi.fn(), getAdminSmartOffer: vi.fn(), getAdminSmartOfferStats: vi.fn(),
    createAdminSmartOffer: vi.fn(), updateAdminSmartOffer: vi.fn(), archiveAdminSmartOffer: vi.fn(),
    user: { tenantId: 'fixture-tenant', id: 'fixture-owner' },
}))
vi.mock('../services/adminSmartOffersApi', () => mocks)
vi.mock('../store/authStore', () => ({ useAuthStore: (selector: (state: { user: typeof mocks.user }) => unknown) => selector({ user: mocks.user }) }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key: string) => key }) }))
vi.mock('../hooks/useRTL', () => ({ useRTL: () => ({ dir: 'ltr' }) }))
vi.mock('../common/confirm/ConfirmDialogProvider', () => ({ useConfirmDialog: () => ({ confirm: vi.fn() }) }))

const deferred = <T,>() => { let resolve!: (value: T) => void; const promise = new Promise<T>(r => { resolve = r }); return { promise, resolve } }
const field = (key: string) => screen.getByLabelText(`SmartOfferEditor.fields.${key}`)
const record = { id: 'fixture-offer', name: 'Saved offer', status: 'Inactive', priority: 0, triggerType: 'TimeDelay', pagesScope: 'Home',
    delaySeconds: 12, scrollPercent: null, maxDisplaysPerUser: 3, showOncePerSession: true, popupTitle: 'Saved title', buttonText: 'Continue', rowVersion: '1' }
beforeEach(() => {
    vi.resetAllMocks()
    mocks.listAdminSmartOffers.mockResolvedValue([])
    mocks.getAdminSmartOfferStats.mockResolvedValue({ totalOffers: 0, activeOffers: 0, exitIntentOffers: 0 })
})
afterEach(cleanup)

describe('Smart Offers asynchronous workflows', () => {
    it('late initial list/stat completion leaves an open creation draft and its preview intact', async () => {
        const list = deferred<unknown[]>()
        const stats = deferred<unknown>()
        mocks.listAdminSmartOffers.mockReturnValueOnce(list.promise)
        mocks.getAdminSmartOfferStats.mockReturnValueOnce(stats.promise)
        render(<SmartOffersPage />)
        fireEvent.click(screen.getByRole('button', { name: 'SmartOffersPage.actions.createOffer' }))
        const name = field('name')
        fireEvent.change(name, { target: { value: 'My draft' } })
        fireEvent.change(field('title'), { target: { value: 'Live draft' } })
        await act(async () => { list.resolve([record]); stats.resolve({ totalOffers: 1, activeOffers: 0, exitIntentOffers: 0 }) })
        expect(field('name')).toBe(name)
        expect(name).toHaveValue('My draft')
        expect(screen.getByRole('heading', { name: 'Live draft' })).toBeVisible()
    })

    it('saves, reopens, updates and restores all persisted editable values using the admitted row version', async () => {
        let saved = record
        mocks.listAdminSmartOffers.mockImplementation(async () => [saved])
        mocks.updateAdminSmartOffer.mockImplementation(async (_old, payload) => { saved = { ...saved, ...payload, rowVersion: '2' }; return saved })
        render(<SmartOffersPage />)
        fireEvent.click(await screen.findByRole('button', { name: 'SmartOffersPage.actions.edit' }))
        expect(field('delaySeconds')).toHaveValue(12)
        expect(field('priority')).toHaveValue(0)
        fireEvent.change(field('priority'), { target: { value: '9' } })
        fireEvent.change(field('pagesScope'), { target: { value: 'Cart' } })
        fireEvent.submit(document.querySelector('form')!)
        await screen.findByText('SmartOffersPage.messages.updateSuccess')
        expect(mocks.updateAdminSmartOffer).toHaveBeenCalledWith(record, expect.objectContaining({ priority: 9, delaySeconds: 12, pagesScope: 'Cart' }), mocks.user)
        fireEvent.click(screen.getByRole('button', { name: 'SmartOffersPage.actions.edit' }))
        expect(field('priority')).toHaveValue(9)
        expect(field('pagesScope')).toHaveValue('Cart')
        expect(field('delaySeconds')).toHaveValue(12)
    })

    it('failed save retains unrelated values and can be retried without duplicate submissions', async () => {
        mocks.createAdminSmartOffer.mockRejectedValueOnce({ response: { data: { message: 'Safe simulated failure' } } }).mockResolvedValue(record)
        render(<SmartOffersPage />)
        fireEvent.click(screen.getByRole('button', { name: 'SmartOffersPage.actions.createOffer' }))
        fireEvent.change(field('name'), { target: { value: 'Keep me' } })
        fireEvent.submit(document.querySelector('form')!)
        expect(await screen.findByRole('alert')).toHaveTextContent('Safe simulated failure')
        expect(field('name')).toHaveValue('Keep me')
        act(() => { fireEvent.submit(document.querySelector('form')!); fireEvent.submit(document.querySelector('form')!) })
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
        expect(mocks.createAdminSmartOffer).toHaveBeenCalledTimes(2)
    })

    it('does not confuse failed reads or unavailable statistics with the empty state or zero', async () => {
        mocks.listAdminSmartOffers.mockRejectedValueOnce(new Error('Unavailable')).mockResolvedValue([])
        mocks.getAdminSmartOfferStats.mockRejectedValueOnce(new Error('Unavailable')).mockResolvedValue({ totalOffers: 0 })
        render(<SmartOffersPage />)
        await screen.findByRole('alert')
        expect(screen.queryByText('SmartOffersPage.emptyState.title')).toBeNull()
        const total = screen.getByText('SmartOffersPage.stats.totalOffers').parentElement!
        expect(within(total).getByText('—')).toBeVisible()
        expect(screen.getByText('SmartOffersPage.messages.statsError')).toBeVisible()
        fireEvent.click(screen.getByRole('button', { name: 'common.retry' }))
        await screen.findByText('SmartOffersPage.emptyState.title')
        expect(within(total).getByText('0')).toBeVisible()
    })

    it('a failed detail read does not replace the successfully loaded offer table', async () => {
        mocks.listAdminSmartOffers.mockResolvedValue([record])
        mocks.getAdminSmartOffer.mockRejectedValue(new Error('Details unavailable'))
        render(<SmartOffersPage />)
        fireEvent.click(await screen.findByRole('button', { name: 'SmartOffersPage.actions.details' }))
        await screen.findByRole('alert')
        expect(screen.getByRole('button', { name: 'SmartOffersPage.actions.edit' })).toBeVisible()
        expect(screen.getByText(record.name)).toBeVisible()
    })
})

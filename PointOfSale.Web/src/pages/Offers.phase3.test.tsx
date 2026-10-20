import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import Coupons from './Coupons'
import Promotions from './Promotions'
import SmartOffersPage from './SmartOffersPage'
import SmartOfferEditor from './SmartOfferEditor'

const mocks = vi.hoisted(() => ({
    coupons: { listAdminCoupons: vi.fn(), getAdminCoupon: vi.fn(), createAdminCoupon: vi.fn(), updateAdminCoupon: vi.fn(), toggleAdminCoupon: vi.fn(), archiveAdminCoupon: vi.fn() },
    promotions: { listAdminPromotions: vi.fn(), getAdminPromotion: vi.fn(), getAdminPromotionStats: vi.fn(), createAdminPromotion: vi.fn(), updateAdminPromotion: vi.fn(), toggleAdminPromotion: vi.fn(), archiveAdminPromotion: vi.fn() },
    smart: { listAdminSmartOffers: vi.fn(), getAdminSmartOffer: vi.fn(), getAdminSmartOfferStats: vi.fn(), createAdminSmartOffer: vi.fn(), updateAdminSmartOffer: vi.fn(), archiveAdminSmartOffer: vi.fn() },
    user: { tenantId: 'tenant', id: 'owner' },
}))
vi.mock('../services/adminCouponsApi', () => mocks.coupons)
vi.mock('../services/adminPromotionsApi', () => mocks.promotions)
vi.mock('../services/adminSmartOffersApi', () => mocks.smart)
vi.mock('../services/api', () => ({ default: { get: vi.fn().mockResolvedValue({ data: [] }) } }))
vi.mock('../store/authStore', () => ({ useAuthStore: (selector: (state: { user: typeof mocks.user }) => unknown) => selector({ user: mocks.user }) }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key: string, options?: { defaultValue?: string }) => options?.defaultValue || key, i18n: { dir: () => 'ltr' } }) }))
vi.mock('../common/confirm/ConfirmDialogProvider', () => ({ useConfirmDialog: () => ({ confirm: vi.fn().mockResolvedValue(true) }) }))
vi.mock('../contexts/SettingsContext', () => ({ useSettings: () => ({ formatCurrency: (n: number) => `${n} EGP`, settings: { currencySymbol: 'EGP' } }) }))
vi.mock('../hooks/useRTL', () => ({ useRTL: () => ({ dir: 'ltr', isRTL: false, locale: 'en' }) }))

const coupon = { id: 'coupon', code: 'SCOPED', discountType: 'Percentage', discountValue: 10, isActive: true, status: 'Active',
    startDate: '2026-10-16T12:34:56.123Z', endDate: '2027-10-16T21:43:22.456Z',
    applicableProductIds: '["product"]', applicableCategoryIds: '["category"]', excludedProductIds: '["excluded"]' }
const promotion = { id: 'promotion', name: 'Scoped promotion', discountType: 'Percentage', discountValue: 10, scope: 'Cart', priority: 0, isActive: false,
    startDate: coupon.startDate, endDate: coupon.endDate }
const deferred = <T,>() => { let resolve!: (value: T) => void; const promise = new Promise<T>(r => { resolve = r }); return { promise, resolve } }

beforeEach(() => {
    vi.resetAllMocks()
    mocks.coupons.listAdminCoupons.mockResolvedValue([coupon])
    mocks.promotions.listAdminPromotions.mockResolvedValue([promotion])
    mocks.promotions.getAdminPromotionStats.mockResolvedValue({})
    mocks.smart.listAdminSmartOffers.mockResolvedValue([])
    mocks.smart.getAdminSmartOfferStats.mockResolvedValue({})
})
afterEach(cleanup)

describe('Phase 3 offer editors', () => {
    it('coupon editing preserves persisted eligibility and exact unchanged schedule', async () => {
        const { container } = render(<Coupons />)
        fireEvent.click(await screen.findByRole('button', { name: 'common.edit' }))
        fireEvent.submit(container.querySelector('form')!)
        await waitFor(() => expect(mocks.coupons.updateAdminCoupon).toHaveBeenCalledOnce())
        expect(mocks.coupons.updateAdminCoupon.mock.calls[0][1]).toMatchObject({ applicableProductIds: coupon.applicableProductIds,
            applicableCategoryIds: coupon.applicableCategoryIds, excludedProductIds: coupon.excludedProductIds, startDate: coupon.startDate, endDate: coupon.endDate })
    })

    it('promotion editing does not truncate the persisted schedule', async () => {
        const { container } = render(<Promotions />)
        fireEvent.click(await screen.findByTitle('common.edit'))
        fireEvent.submit(container.querySelector('form')!)
        await waitFor(() => expect(mocks.promotions.updateAdminPromotion).toHaveBeenCalledOnce())
        expect(mocks.promotions.updateAdminPromotion.mock.calls[0][1]).toMatchObject({ startDate: promotion.startDate, endDate: promotion.endDate })
    })

    it.each(['coupon', 'promotion', 'smart'])('%s suppresses same-tick duplicate saves', async kind => {
        const pending = deferred<unknown>()
        const create = kind === 'coupon' ? mocks.coupons.createAdminCoupon : kind === 'promotion' ? mocks.promotions.createAdminPromotion : mocks.smart.createAdminSmartOffer
        create.mockReturnValue(pending.promise)
        const { container } = render(kind === 'coupon' ? <Coupons /> : kind === 'promotion' ? <Promotions /> : <SmartOffersPage />)
        fireEvent.click(await screen.findByRole('button', { name: kind === 'coupon' ? /coupons.addCoupon/ : kind === 'promotion' ? /promotions.addPromotion/ : /SmartOffersPage.actions.createOffer/ }))
        if (kind === 'promotion') {
            const dates = container.querySelectorAll('input[type="date"],input[type="datetime-local"]')
            fireEvent.change(dates[0], { target: { value: (dates[0] as HTMLInputElement).type === 'date' ? '2026-10-16' : '2026-10-16T00:00:00' } })
            fireEvent.change(dates[1], { target: { value: (dates[1] as HTMLInputElement).type === 'date' ? '2027-10-16' : '2027-10-16T00:00:00' } })
        }
        const editorForm = kind === 'smart' ? document.querySelector('form')! : container.querySelector('form')!
        act(() => { fireEvent.submit(editorForm); fireEvent.submit(editorForm) })
        expect(create).toHaveBeenCalledOnce()
        await act(async () => pending.resolve({}))
    })

    it('coupon old search completion cannot replace the newer search result', async () => {
        const old = deferred<unknown[]>()
        mocks.coupons.listAdminCoupons.mockReturnValueOnce(old.promise).mockResolvedValue([{ ...coupon, code: 'NEWEST' }])
        render(<Coupons />)
        fireEvent.change(screen.getByPlaceholderText('coupons.searchPlaceholder'), { target: { value: 'new' } })
        await screen.findByRole('button', { name: 'NEWEST' })
        await act(async () => old.resolve([{ ...coupon, code: 'STALE' }]))
        expect(screen.queryByRole('button', { name: 'STALE' })).toBeNull()
        expect(screen.getByRole('button', { name: 'NEWEST' })).toBeTruthy()
    })

    it('smart editor clears disabled coupon settings when the code is removed', async () => {
        const save = vi.fn()
        render(<SmartOfferEditor open offer={{ name: 'Synthetic', couponCode: 'OLD', couponDiscountType: 'Percentage', couponDiscountValue: 20, couponMinOrderAmount: 10, couponMaxDiscountAmount: 5 }} saving={false} error="" onClose={vi.fn()} onSave={save} />)
        fireEvent.change(screen.getByDisplayValue('OLD'), { target: { value: '' } })
        fireEvent.submit(document.querySelector('form')!)
        expect(save.mock.calls[0][0]).toMatchObject({ couponCode: null, couponDiscountType: null, couponDiscountValue: null, couponMinOrderAmount: null, couponMaxDiscountAmount: null })
    })

    it.each(['coupon', 'promotion', 'smart'])('%s committed save plus failed readback is not announced as complete success', async kind => {
        const list = kind === 'coupon' ? mocks.coupons.listAdminCoupons : kind === 'promotion' ? mocks.promotions.listAdminPromotions : mocks.smart.listAdminSmartOffers
        const create = kind === 'coupon' ? mocks.coupons.createAdminCoupon : kind === 'promotion' ? mocks.promotions.createAdminPromotion : mocks.smart.createAdminSmartOffer
        list.mockResolvedValueOnce([]).mockRejectedValue(new Error('readback unavailable'))
        create.mockResolvedValue({})
        const { container } = render(kind === 'coupon' ? <Coupons /> : kind === 'promotion' ? <Promotions /> : <SmartOffersPage />)
        await waitFor(() => expect(list).toHaveBeenCalledOnce())
        fireEvent.click(screen.getByRole('button', { name: kind === 'coupon' ? /coupons.addCoupon/ : kind === 'promotion' ? /promotions.addPromotion/ : /SmartOffersPage.actions.createOffer/ }))
        if (kind === 'promotion') {
            const dates = container.querySelectorAll('input[type="datetime-local"]')
            fireEvent.change(dates[0], { target: { value: '2026-10-16T00:00:00' } })
            fireEvent.change(dates[1], { target: { value: '2027-10-16T00:00:00' } })
        }
        fireEvent.submit(kind === 'smart' ? document.querySelector('form')! : container.querySelector('form')!)
        await screen.findByText('offers.savedReloadFailed')
        expect(document.querySelector('form')).toBeNull()
        expect(create).toHaveBeenCalledOnce()
        expect(screen.queryByText(kind === 'coupon' ? 'coupons.created' : kind === 'promotion' ? 'promotions.created' : 'SmartOffersPage.messages.createSuccess')).toBeNull()
    })

    it.each(['promotion', 'smart'])('%s older search cannot replace newer results', async kind => {
        const old = deferred<unknown[]>()
        const list = kind === 'promotion' ? mocks.promotions.listAdminPromotions : mocks.smart.listAdminSmartOffers
        const record = kind === 'promotion' ? promotion : { id: 'smart', name: 'Newest', status: 'Inactive', triggerType: 'ExitIntent', pagesScope: 'All' }
        list.mockReturnValueOnce(old.promise).mockResolvedValue([{ ...record, name: 'Newest' }])
        render(kind === 'promotion' ? <Promotions /> : <SmartOffersPage />)
        fireEvent.change(screen.getByPlaceholderText(kind === 'promotion' ? 'common.search' : 'SmartOffersPage.filters.searchPlaceholder'), { target: { value: 'new' } })
        await screen.findByText('Newest')
        await act(async () => old.resolve([{ ...record, name: 'Stale' }]))
        expect(screen.queryByText('Stale')).toBeNull()
        expect(screen.getByText('Newest')).toBeTruthy()
    })
})

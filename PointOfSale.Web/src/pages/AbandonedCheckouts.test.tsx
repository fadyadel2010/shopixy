import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import en from '../i18n/locales/en.json'
import ar from '../i18n/locales/ar.json'

const api = vi.hoisted(() => ({ get: vi.fn() }))
let language = 'en'

vi.mock('../services/api', () => ({
    default: api,
    isRequestCanceled: () => false,
}))

const resolveKey = (resource: Record<string, unknown>, key: string): unknown => (
    key.split('.').reduce<unknown>((value, part) => (
        value && typeof value === 'object' ? (value as Record<string, unknown>)[part] : undefined
    ), resource)
)

const translate = (key: string, options: Record<string, unknown> = {}) => {
    const resource = language === 'ar' ? ar : en
    const value = resolveKey(resource, key)
    if (typeof value !== 'string') return key
    return Object.entries(options).reduce(
        (text, [name, replacement]) => text.replaceAll(`{{${name}}}`, String(replacement)),
        value,
    )
}

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: translate,
        i18n: { language, resolvedLanguage: language },
    }),
}))

vi.mock('../hooks/useFormatting', () => ({
    default: () => ({
        dir: language === 'ar' ? 'rtl' : 'ltr',
        locale: language === 'ar' ? 'ar-EG' : 'en-US',
        formatCurrency: (value: number, currency = 'EGP') => `${currency} ${Number(value).toFixed(2)}`,
        formatDateTime: () => language === 'ar' ? 'تاريخ منسق' : 'Formatted date',
        formatInteger: (value: number) => String(value),
        formatNumber: (value: number) => Number(value).toFixed(1),
    }),
}))

import AbandonedCheckouts from './AbandonedCheckouts'

const stats = {
    totalSessions: 4,
    activeSessions: 1,
    abandonedSessions: 2,
    convertedSessions: 1,
    abandonmentRate: 50,
    conversionRate: 25,
    totalAbandonedRevenue: 150.25,
    abandonedValueAvailable: true,
    abandonedValueCurrency: 'EGP',
}

const checkout = {
    checkoutSessionId: '10000000-0000-0000-0000-000000000001',
    customerName: 'Safe Fixture',
    phone: '+200000000000',
    email: 'fixture@example.invalid',
    cartTotal: 150.25,
    currency: 'EGP',
    status: 'Abandoned',
    deviceType: 'Mobile',
    source: 'Direct',
    timeSpentSeconds: 125,
    createdAtUtc: '2026-09-30T10:00:00Z',
    lastActivityAtUtc: '2026-09-30T10:02:05Z',
}

const listResponse = (items = [checkout]) => ({
    data: { items, totalCount: items.length, page: 1, pageSize: 20, totalPages: items.length ? 1 : 0, hasMore: false },
})

const configureSuccessfulApi = () => {
    api.get.mockImplementation((url: string) => {
        if (url.endsWith('/stats')) return Promise.resolve({ data: stats })
        if (url.includes(checkout.checkoutSessionId)) {
            return Promise.resolve({
                data: {
                    ...checkout,
                    city: 'Cairo',
                    paymentMethodCode: 'COD',
                    recoveryUrl: 'http://fixture.invalid/recover-checkout/safe-token',
                    items: [],
                },
            })
        }
        return Promise.resolve(listResponse())
    })
}

describe('AbandonedCheckouts', () => {
    beforeEach(() => {
        language = 'en'
        api.get.mockReset()
        configureSuccessfulApi()
    })

    afterEach(() => cleanup())

    it('renders complete English copy and backend-authoritative percentages', async () => {
        render(<AbandonedCheckouts />)

        expect(await screen.findByRole('heading', { name: 'Abandoned Checkouts' })).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'All' })).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Active' })).toBeInTheDocument()
        expect(screen.getByPlaceholderText('Search by name, phone, or email...')).toBeInTheDocument()
        await waitFor(() => expect(screen.getByText('2 (50.0%)')).toBeInTheDocument())
        expect(screen.getByText('1 (25.0%)')).toBeInTheDocument()
        expect(screen.getAllByText('EGP 150.25')).toHaveLength(2)
        expect(screen.queryByText(/abandonedCheckouts\./)).not.toBeInTheDocument()
    })

    it('renders Arabic copy in RTL without unresolved keys', async () => {
        language = 'ar'
        render(<AbandonedCheckouts />)

        const heading = await screen.findByRole('heading', { name: 'عمليات الدفع المتروكة' })
        expect(heading.closest('[dir="rtl"]')).not.toBeNull()
        expect(screen.getByRole('button', { name: 'الكل' })).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'نشطة' })).toBeInTheDocument()
        expect(screen.getByPlaceholderText('ابحث بالاسم أو الهاتف أو البريد الإلكتروني...')).toBeInTheDocument()
        expect(screen.queryByText('Abandoned Checkouts')).not.toBeInTheDocument()
        expect(screen.queryByText(/abandonedCheckouts\./)).not.toBeInTheDocument()
    })

    it('keeps failed requests distinct from a genuine empty result and offers retry', async () => {
        api.get.mockImplementation((url: string) => (
            url.endsWith('/stats') ? Promise.resolve({ data: stats }) : Promise.reject({ response: { status: 503 } })
        ))

        render(<AbandonedCheckouts />)

        expect(await screen.findByText('Checkout sessions could not be loaded. This is not an empty result.')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
        expect(screen.queryByText('No sessions match this search or filter')).not.toBeInTheDocument()
        expect(screen.queryByText('No checkout sessions found')).not.toBeInTheDocument()
    })

    it.each([
        [403, 'You do not have permission to view abandoned checkouts.'],
        [401, 'Your session expired. Sign in again to continue.'],
    ])('explains authorization failure %s without presenting it as empty data', async (status, message) => {
        api.get.mockImplementation((url: string) => (
            url.endsWith('/stats') ? Promise.resolve({ data: stats }) : Promise.reject({ response: { status } })
        ))

        render(<AbandonedCheckouts />)

        expect(await screen.findByText(message)).toBeInTheDocument()
        expect(screen.queryByText('No checkout sessions found')).not.toBeInTheDocument()
    })

    it('retains previously loaded rows and labels a failed refresh as stale data', async () => {
        render(<AbandonedCheckouts />)
        await screen.findByText('Safe Fixture')

        api.get.mockImplementation((url: string) => (
            url.endsWith('/stats') ? Promise.resolve({ data: stats }) : Promise.reject({ response: { status: 503 } })
        ))
        fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))

        expect(await screen.findByText('Refresh failed. The previously loaded results are still shown.')).toBeInTheDocument()
        expect(screen.getByText('Safe Fixture')).toBeInTheDocument()
    })

    it('sends every status filter and restores All without leaking the filter parameter', async () => {
        render(<AbandonedCheckouts />)
        await screen.findByText('Safe Fixture')

        fireEvent.click(screen.getByRole('button', { name: 'Active' }))
        await waitFor(() => expect(api.get).toHaveBeenCalledWith(expect.stringContaining('status=Active')))

        fireEvent.click(screen.getByRole('button', { name: 'Converted' }))
        await waitFor(() => expect(api.get).toHaveBeenCalledWith(expect.stringContaining('status=ConvertedToOrder')))

        fireEvent.click(screen.getByRole('button', { name: 'All' }))
        await waitFor(() => {
            const latestListUrl = [...api.get.mock.calls]
                .map(([url]) => String(url))
                .filter((url) => url.includes('/admin/abandoned-checkouts?'))
                .at(-1)
            expect(latestListUrl).not.toContain('status=')
        })
    })

    it('opens authoritative details and explains that checkout recovery is not Smart Cart Recovery eligibility', async () => {
        render(<AbandonedCheckouts />)
        fireEvent.click(await screen.findByText('Safe Fixture'))

        expect(await screen.findByRole('heading', { name: 'Checkout details' })).toBeInTheDocument()
        expect(screen.getByText('This restores the checkout only. It does not make the session eligible for Smart Cart Recovery or send a WhatsApp message.')).toBeInTheDocument()
        expect(screen.getByText('No cart item snapshot is available for this session.')).toBeInTheDocument()
        expect(screen.queryByText(checkout.checkoutSessionId)).not.toBeInTheDocument()
    })

    it('defines the same audited translation keys in English and Arabic', () => {
        const requiredKeys = [
            'title', 'subtitle', 'totalSessions', 'abandoned', 'converted', 'lostRevenue',
            'searchPlaceholder', 'empty', 'noMatches', 'details', 'checkoutInfo', 'recoveryLinkHint',
            'statuses.all', 'statuses.abandoned', 'statuses.active', 'statuses.converted',
            'sources.direct', 'sources.storefront', 'sources.instagram', 'sources.affiliate', 'sources.googleads',
            'errors.list', 'errors.stats', 'errors.detail', 'errors.permission',
        ]

        for (const key of requiredKeys) {
            expect(resolveKey(en.abandonedCheckouts, key)).toEqual(expect.any(String))
            expect(resolveKey(ar.abandonedCheckouts, key)).toEqual(expect.any(String))
        }
    })
})

import { Building2, Ticket } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type {
    SuperAdminPromoAnalyticsResult,
    SuperAdminPromoAnalyticsSortField,
    SuperAdminSortDirection,
    SuperAdminVendorPerformanceResult,
    SuperAdminVendorPerformanceSortField,
} from './financeService'

interface FinancePerformanceTablesProps {
    promos?: SuperAdminPromoAnalyticsResult
    vendors?: SuperAdminVendorPerformanceResult
    promosLoading?: boolean
    vendorsLoading?: boolean
    promosError?: string | null
    vendorsError?: string | null
    promoSortBy: SuperAdminPromoAnalyticsSortField
    promoSortDirection: SuperAdminSortDirection
    vendorSortBy: SuperAdminVendorPerformanceSortField
    vendorSortDirection: SuperAdminSortDirection
    onPromoSortByChange: (value: SuperAdminPromoAnalyticsSortField) => void
    onPromoSortDirectionChange: (value: SuperAdminSortDirection) => void
    onVendorSortByChange: (value: SuperAdminVendorPerformanceSortField) => void
    onVendorSortDirectionChange: (value: SuperAdminSortDirection) => void
    onPromoPreviousPage: () => void
    onPromoNextPage: () => void
    onVendorPreviousPage: () => void
    onVendorNextPage: () => void
}

const COMMERCEHUB_AUTHORITY_CURRENCY = 'EGP'
const UNSUPPORTED_COMMERCEHUB_CURRENCY = 'CommerceHub currency blocked'

function formatCurrency(value: number, currency = 'EGP', locale = 'en'): string {
    if (String(currency || '').trim().toUpperCase() !== COMMERCEHUB_AUTHORITY_CURRENCY) {
        return UNSUPPORTED_COMMERCEHUB_CURRENCY
    }

    return new Intl.NumberFormat(locale, {
        style: 'currency',
        currency: COMMERCEHUB_AUTHORITY_CURRENCY,
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(Number(value ?? 0))
}

function formatPercent(value: number, locale = 'en'): string {
    return new Intl.NumberFormat(locale, {
        style: 'percent',
        minimumFractionDigits: 1,
        maximumFractionDigits: 1,
    }).format(Number(value ?? 0))
}

function formatDateTime(value: string | null | undefined, locale: string, fallback: string): string {
    if (!value) {
        return fallback
    }

    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
        return fallback
    }

    return date.toLocaleString(locale)
}

function Pagination({
    page,
    totalPages,
    totalCount,
    onPrevious,
    onNext,
}: {
    page: number
    totalPages: number
    totalCount: number
    onPrevious: () => void
    onNext: () => void
}) {
    const { t } = useTranslation()

    return (
        <div className="flex flex-col gap-4 border-t border-slate-100 px-6 py-4 text-sm text-slate-500 sm:flex-row sm:items-center sm:justify-between">
            <p>
                {t('superAdmin.financePerformanceTables.pagination.summary', {
                    page,
                    totalPages: Math.max(totalPages, 1),
                    count: totalCount,
                })}
            </p>
            <div className="flex gap-3">
                <button type="button" className="rounded-2xl border border-slate-200 bg-white px-4 py-2 font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60" disabled={page <= 1} onClick={onPrevious}>{t('superAdmin.common.actions.previous')}</button>
                <button type="button" className="rounded-2xl border border-slate-200 bg-white px-4 py-2 font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60" disabled={page >= totalPages} onClick={onNext}>{t('superAdmin.common.actions.next')}</button>
            </div>
        </div>
    )
}

export default function FinancePerformanceTables({
    promos,
    vendors,
    promosLoading,
    vendorsLoading,
    promosError,
    vendorsError,
    promoSortBy,
    promoSortDirection,
    vendorSortBy,
    vendorSortDirection,
    onPromoSortByChange,
    onPromoSortDirectionChange,
    onVendorSortByChange,
    onVendorSortDirectionChange,
    onPromoPreviousPage,
    onPromoNextPage,
    onVendorPreviousPage,
    onVendorNextPage,
}: FinancePerformanceTablesProps) {
    const { t, i18n } = useTranslation()
    const promoMeta = promos?.meta
    const vendorMeta = vendors?.meta
    const locale = i18n.resolvedLanguage || i18n.language || 'en'

    return (
        <section className="grid gap-6 xl:grid-cols-2">
            <article className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
                <div className="border-b border-slate-100 px-6 py-5">
                    <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
                        <div>
                            <div className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-4 py-2 text-sm font-semibold text-slate-700">
                                <Ticket className="h-4 w-4" />
                                {t('superAdmin.financePerformanceTables.promo.badge')}
                            </div>
                            <h3 className="mt-4 text-xl font-semibold text-slate-950">{t('superAdmin.financePerformanceTables.promo.title')}</h3>
                            <p className="mt-2 text-sm text-slate-500">{t('superAdmin.financePerformanceTables.promo.description')}</p>
                        </div>

                        <div className="grid gap-3 sm:grid-cols-2">
                            <select value={promoSortBy} onChange={(event) => onPromoSortByChange(event.target.value as SuperAdminPromoAnalyticsSortField)} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100">
                                <option value="NetImpact">{t('superAdmin.financePerformanceTables.promo.sort.netImpact')}</option>
                                <option value="RevenueGenerated">{t('superAdmin.financePerformanceTables.promo.sort.revenueGenerated')}</option>
                                <option value="PromoCost">{t('superAdmin.financePerformanceTables.promo.sort.promoCost')}</option>
                                <option value="UsageCount">{t('superAdmin.financePerformanceTables.promo.sort.usageCount')}</option>
                                <option value="PromoCode">{t('superAdmin.financePerformanceTables.promo.sort.promoCode')}</option>
                                <option value="LastUsedAt">{t('superAdmin.financePerformanceTables.promo.sort.lastUsedAt')}</option>
                            </select>
                            <select value={promoSortDirection} onChange={(event) => onPromoSortDirectionChange(event.target.value as SuperAdminSortDirection)} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100">
                                <option value="Desc">{t('superAdmin.financePerformanceTables.sort.descending')}</option>
                                <option value="Asc">{t('superAdmin.financePerformanceTables.sort.ascending')}</option>
                            </select>
                        </div>
                    </div>
                </div>

                {promosError ? (
                    <div className="px-6 py-8 text-sm text-rose-700">{promosError}</div>
                ) : (
                    <>
                        <div className="overflow-x-auto">
                            <table className="min-w-full divide-y divide-slate-200 text-sm">
                                <thead className="bg-slate-50 text-left text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                    <tr>
                                        <th className="px-6 py-4">{t('superAdmin.financePerformanceTables.promo.table.promo')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.financePerformanceTables.promo.table.uses')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.financePerformanceTables.promo.table.revenue')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.financePerformanceTables.promo.table.promoCost')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.financePerformanceTables.promo.table.netImpact')}</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-slate-100 bg-white">
                                    {(promos?.items ?? []).map((item) => (
                                        <tr key={`${item.promoCodeId ?? item.promoCode}-${item.currency}`} className="align-top">
                                            <td className="px-6 py-4">
                                                <div className="font-semibold text-slate-950">{item.promoCode}</div>
                                                <div className="mt-1 text-xs text-slate-500">
                                                    {item.isActive
                                                        ? t('superAdmin.financePerformanceTables.common.active')
                                                        : t('superAdmin.financePerformanceTables.common.inactive')}
                                                    {' | '}
                                                    {formatDateTime(item.lastUsedAtUtc, locale, t('superAdmin.financePerformanceTables.common.noActivity'))}
                                                </div>
                                            </td>
                                            <td className="px-6 py-4 text-slate-700">
                                                <div className="font-semibold text-slate-950">{item.totalUsageCount}</div>
                                                <div className="mt-1 text-xs text-slate-500">{t('superAdmin.financePerformanceTables.promo.table.averageDiscount', { amount: formatCurrency(item.averageDiscountPerUse, item.currency, locale) })}</div>
                                            </td>
                                            <td className="px-6 py-4 text-emerald-700">{formatCurrency(item.revenueGenerated, item.currency, locale)}</td>
                                            <td className="px-6 py-4 text-amber-700">{formatCurrency(item.totalPromoCost, item.currency, locale)}</td>
                                            <td className={`px-6 py-4 font-semibold ${item.netImpact >= 0 ? 'text-slate-950' : 'text-rose-700'}`}>{formatCurrency(item.netImpact, item.currency, locale)}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>

                        {promosLoading ? (
                            <div className="px-6 py-8 text-center text-sm text-slate-500">{t('superAdmin.financePerformanceTables.promo.loading')}</div>
                        ) : null}

                        {promos && promos.items.length === 0 && !promosLoading ? (
                            <div className="px-6 py-8 text-center text-sm text-slate-500">{t('superAdmin.financePerformanceTables.promo.empty')}</div>
                        ) : null}

                        <Pagination
                            page={promoMeta?.page ?? 1}
                            totalPages={promoMeta?.totalPages ?? 0}
                            totalCount={promoMeta?.totalCount ?? promos?.items.length ?? 0}
                            onPrevious={onPromoPreviousPage}
                            onNext={onPromoNextPage}
                        />
                    </>
                )}
            </article>

            <article className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
                <div className="border-b border-slate-100 px-6 py-5">
                    <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
                        <div>
                            <div className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-4 py-2 text-sm font-semibold text-slate-700">
                                <Building2 className="h-4 w-4" />
                                {t('superAdmin.financePerformanceTables.vendor.badge')}
                            </div>
                            <h3 className="mt-4 text-xl font-semibold text-slate-950">{t('superAdmin.financePerformanceTables.vendor.title')}</h3>
                            <p className="mt-2 text-sm text-slate-500">{t('superAdmin.financePerformanceTables.vendor.description')}</p>
                        </div>

                        <div className="grid gap-3 sm:grid-cols-2">
                            <select value={vendorSortBy} onChange={(event) => onVendorSortByChange(event.target.value as SuperAdminVendorPerformanceSortField)} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100">
                                <option value="NetContribution">{t('superAdmin.financePerformanceTables.vendor.sort.netContribution')}</option>
                                <option value="GrossCommission">{t('superAdmin.financePerformanceTables.vendor.sort.grossCommission')}</option>
                                <option value="TotalSales">{t('superAdmin.financePerformanceTables.vendor.sort.totalSales')}</option>
                                <option value="PromoCost">{t('superAdmin.financePerformanceTables.vendor.sort.promoCost')}</option>
                                <option value="RefundRate">{t('superAdmin.financePerformanceTables.vendor.sort.refundRate')}</option>
                                <option value="TotalRefundAmount">{t('superAdmin.financePerformanceTables.vendor.sort.totalRefundAmount')}</option>
                                <option value="OrdersCount">{t('superAdmin.financePerformanceTables.vendor.sort.ordersCount')}</option>
                                <option value="VendorName">{t('superAdmin.financePerformanceTables.vendor.sort.vendorName')}</option>
                            </select>
                            <select value={vendorSortDirection} onChange={(event) => onVendorSortDirectionChange(event.target.value as SuperAdminSortDirection)} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100">
                                <option value="Desc">{t('superAdmin.financePerformanceTables.sort.descending')}</option>
                                <option value="Asc">{t('superAdmin.financePerformanceTables.sort.ascending')}</option>
                            </select>
                        </div>
                    </div>
                </div>

                {vendorsError ? (
                    <div className="px-6 py-8 text-sm text-rose-700">{vendorsError}</div>
                ) : (
                    <>
                        <div className="overflow-x-auto">
                            <table className="min-w-full divide-y divide-slate-200 text-sm">
                                <thead className="bg-slate-50 text-left text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                    <tr>
                                        <th className="px-6 py-4">{t('superAdmin.financePerformanceTables.vendor.table.vendor')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.financePerformanceTables.vendor.table.orders')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.financePerformanceTables.vendor.table.gross')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.financePerformanceTables.vendor.table.promo')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.financePerformanceTables.vendor.table.refundRate')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.financePerformanceTables.vendor.table.net')}</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-slate-100 bg-white">
                                    {(vendors?.items ?? []).map((item) => (
                                        <tr key={`${item.vendorId}-${item.currency}`} className="align-top">
                                            <td className="px-6 py-4">
                                                <div className="font-semibold text-slate-950">{item.vendorName}</div>
                                                <div className="mt-1 text-xs text-slate-500">{item.contactEmail || item.vendorSlug || item.vendorId}</div>
                                                <div className="mt-1 text-xs text-slate-500">{t('superAdmin.financePerformanceTables.vendor.table.lastOrder', { timestamp: formatDateTime(item.lastOrderAtUtc, locale, t('superAdmin.financePerformanceTables.common.noActivity')) })}</div>
                                            </td>
                                            <td className="px-6 py-4 text-slate-700">
                                                <div className="font-semibold text-slate-950">{item.ordersCount}</div>
                                                <div className="mt-1 text-xs text-slate-500">{t('superAdmin.financePerformanceTables.vendor.table.sales', { amount: formatCurrency(item.totalSales, item.currency, locale) })}</div>
                                            </td>
                                            <td className="px-6 py-4 text-sky-700">{formatCurrency(item.grossCommission, item.currency, locale)}</td>
                                            <td className="px-6 py-4 text-amber-700">
                                                <div>{formatCurrency(item.promoCost, item.currency, locale)}</div>
                                                <div className="mt-1 text-xs text-slate-500">{t('superAdmin.financePerformanceTables.vendor.table.refunds', { amount: formatCurrency(item.totalRefundAmount, item.currency, locale) })}</div>
                                            </td>
                                            <td className="px-6 py-4 text-slate-700">
                                                <div>{formatPercent(item.refundRate, locale)}</div>
                                                <div className="mt-1 text-xs text-slate-500">{t('superAdmin.financePerformanceTables.vendor.table.refundedOrders', { count: item.refundedOrdersCount })}</div>
                                            </td>
                                            <td className={`px-6 py-4 font-semibold ${item.netContribution >= 0 ? 'text-emerald-700' : 'text-rose-700'}`}>
                                                {formatCurrency(item.netContribution, item.currency, locale)}
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>

                        {vendorsLoading ? (
                            <div className="px-6 py-8 text-center text-sm text-slate-500">{t('superAdmin.financePerformanceTables.vendor.loading')}</div>
                        ) : null}

                        {vendors && vendors.items.length === 0 && !vendorsLoading ? (
                            <div className="px-6 py-8 text-center text-sm text-slate-500">{t('superAdmin.financePerformanceTables.vendor.empty')}</div>
                        ) : null}

                        <Pagination
                            page={vendorMeta?.page ?? 1}
                            totalPages={vendorMeta?.totalPages ?? 0}
                            totalCount={vendorMeta?.totalCount ?? vendors?.items.length ?? 0}
                            onPrevious={onVendorPreviousPage}
                            onNext={onVendorNextPage}
                        />
                    </>
                )}
            </article>
        </section>
    )
}

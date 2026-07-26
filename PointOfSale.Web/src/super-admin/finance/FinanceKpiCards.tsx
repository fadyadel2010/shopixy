import type { LucideIcon } from 'lucide-react'
import {
    BadgeDollarSign,
    Coins,
    RotateCcw,
    ShoppingBag,
    TicketPercent,
    WalletCards,
} from 'lucide-react'
import type { SuperAdminFinancialOverview } from './financeService'

interface FinanceKpiCardsProps {
    data?: SuperAdminFinancialOverview
    isLoading?: boolean
}

interface MetricCard {
    key: keyof Pick<
        SuperAdminFinancialOverview,
        | 'totalGrossCommission'
        | 'totalPromoCost'
        | 'totalNetCommission'
        | 'netRevenue'
        | 'totalRefundAmount'
        | 'averageOrderValue'
    >
    label: string
    description: string
    icon: LucideIcon
    accent: string
    surface: string
}

const cards: MetricCard[] = [
    {
        key: 'totalGrossCommission',
        label: 'Gross Commission',
        description: 'Platform commission before promo funding and refund reversals.',
        icon: BadgeDollarSign,
        accent: 'text-sky-700',
        surface: 'from-sky-50 to-white border-sky-100',
    },
    {
        key: 'totalPromoCost',
        label: 'Promo Cost',
        description: 'Platform-funded commission discounts net of promo reversals.',
        icon: TicketPercent,
        accent: 'text-amber-700',
        surface: 'from-amber-50 to-white border-amber-100',
    },
    {
        key: 'totalNetCommission',
        label: 'Net Commission',
        description: 'Gross commission after promo funding.',
        icon: Coins,
        accent: 'text-emerald-700',
        surface: 'from-emerald-50 to-white border-emerald-100',
    },
    {
        key: 'netRevenue',
        label: 'Net Revenue',
        description: 'Net commission after refund-driven commission reversals.',
        icon: WalletCards,
        accent: 'text-slate-900',
        surface: 'from-slate-100 to-white border-slate-200',
    },
    {
        key: 'totalRefundAmount',
        label: 'Refund Value',
        description: 'Completed refund amounts in the selected reporting scope.',
        icon: RotateCcw,
        accent: 'text-rose-700',
        surface: 'from-rose-50 to-white border-rose-100',
    },
    {
        key: 'averageOrderValue',
        label: 'AOV',
        description: 'Average financially recognized sub-order value.',
        icon: ShoppingBag,
        accent: 'text-violet-700',
        surface: 'from-violet-50 to-white border-violet-100',
    },
]

function formatCurrency(value: number, currency = 'EGP'): string {
    if ((currency || '').trim().toUpperCase() !== 'EGP') {
        return 'CommerceHub currency blocked'
    }

    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: 'EGP',
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(Number(value ?? 0))
}

function formatPercent(value: number): string {
    return new Intl.NumberFormat('en-US', {
        style: 'percent',
        minimumFractionDigits: 1,
        maximumFractionDigits: 1,
    }).format(Number(value ?? 0))
}

function KpiSkeleton() {
    return (
        <div className="rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm">
            <div className="flex items-start justify-between gap-4">
                <div className="flex-1">
                    <div className="h-3 w-28 animate-pulse rounded-full bg-slate-200" />
                    <div className="mt-4 h-9 w-32 animate-pulse rounded-2xl bg-slate-200" />
                    <div className="mt-3 h-3 w-48 animate-pulse rounded-full bg-slate-100" />
                </div>
                <div className="h-12 w-12 animate-pulse rounded-2xl bg-slate-100" />
            </div>
        </div>
    )
}

export default function FinanceKpiCards({ data, isLoading }: FinanceKpiCardsProps) {
    return (
        <section className="space-y-6">
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
                {isLoading && !data
                    ? cards.map((card) => <KpiSkeleton key={card.key} />)
                    : cards.map((card) => {
                          const Icon = card.icon
                          const value = Number(data?.[card.key] ?? 0)

                          return (
                              <article
                                  key={card.key}
                                  className={`rounded-[28px] border bg-gradient-to-br p-5 shadow-[0_20px_60px_-40px_rgba(15,23,42,0.45)] ${card.surface}`}
                              >
                                  <div className="flex items-start justify-between gap-4">
                                      <div className="min-w-0">
                                          <p className="text-sm font-medium text-slate-500">{card.label}</p>
                                          <p className="mt-4 text-3xl font-semibold tracking-tight text-slate-950">
                                              {formatCurrency(value, data?.currency)}
                                          </p>
                                          <p className="mt-3 text-sm text-slate-500">{card.description}</p>
                                      </div>
                                      <div className={`flex h-12 w-12 items-center justify-center rounded-2xl bg-white shadow-sm ${card.accent}`}>
                                          <Icon className="h-5 w-5" />
                                      </div>
                                  </div>
                              </article>
                          )
                      })}
            </div>

            <div className="grid gap-4 lg:grid-cols-3">
                <div className="rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm">
                    <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                        Orders In Scope
                    </p>
                    <p className="mt-3 text-2xl font-semibold text-slate-950">
                        {Number(data?.totalOrdersCount ?? 0).toLocaleString('en-US')}
                    </p>
                    <p className="mt-2 text-sm text-slate-500">
                        Counted at recognized sub-order commission rows.
                    </p>
                </div>

                <div className="rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm">
                    <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                        Refund Rate
                    </p>
                    <p className="mt-3 text-2xl font-semibold text-slate-950">
                        {formatPercent(data?.refundRate ?? 0)}
                    </p>
                    <p className="mt-2 text-sm text-slate-500">
                        {Number(data?.refundedOrdersCount ?? 0).toLocaleString('en-US')} refunded order{(data?.refundedOrdersCount ?? 0) === 1 ? '' : 's'} within scope.
                    </p>
                </div>

                <div className="rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm">
                    <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                        Revenue Drag
                    </p>
                    <p className="mt-3 text-2xl font-semibold text-slate-950">
                        {formatCurrency(data?.refundImpactOnRevenue ?? 0, data?.currency)}
                    </p>
                    <p className="mt-2 text-sm text-slate-500">
                        Commission revenue reversed because of completed refunds.
                    </p>
                </div>
            </div>
        </section>
    )
}

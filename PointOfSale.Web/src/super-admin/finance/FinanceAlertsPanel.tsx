import { AlertTriangle, Landmark, ShieldAlert, Wallet } from 'lucide-react'
import { useQuery } from '@tanstack/react-query'
import {
    getSuperAdminVendorFinanceAlerts,
    type SuperAdminVendorFinanceAlertItem,
    type SuperAdminFinanceOverview,
} from './financeService'

interface FinanceAlertsPanelProps {
    overview?: SuperAdminFinanceOverview
}

const COMMERCEHUB_AUTHORITY_CURRENCY = 'EGP'
const UNSUPPORTED_COMMERCEHUB_CURRENCY = 'CommerceHub currency blocked'

function formatCurrency(amount: number, currency = 'EGP'): string {
    if ((currency || '').trim().toUpperCase() !== COMMERCEHUB_AUTHORITY_CURRENCY) {
        return UNSUPPORTED_COMMERCEHUB_CURRENCY
    }

    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: COMMERCEHUB_AUTHORITY_CURRENCY,
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(Number(amount ?? 0))
}

function formatPercent(value: number): string {
    return `${new Intl.NumberFormat('en-US', {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2,
    }).format((value ?? 0) * 100)}%`
}

function formatDateTime(value?: string | null): string {
    if (!value) {
        return 'Pending'
    }

    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
        return 'Pending'
    }

    return date.toLocaleString()
}

function getAlertTone(severity: 'WARNING' | 'CRITICAL'): string {
    return severity === 'CRITICAL'
        ? 'border-rose-200 bg-rose-50 text-rose-900'
        : 'border-amber-200 bg-amber-50 text-amber-900'
}

function buildVendorAlertDetail(alert: SuperAdminVendorFinanceAlertItem): string {
    if (alert.alertType === 'LARGE_PAYABLE_DELAY') {
        return `Payable delay remains unresolved. Vendor debt is ${formatCurrency(alert.amountOwedByVendor, alert.currency)} and the refund ratio is ${formatPercent(alert.refundRatio)}.`
    }

    if (alert.isGracePeriodActive && alert.graceDeadlineUtc) {
        return `Grace deadline ${formatDateTime(alert.graceDeadlineUtc)}. Current balance ${formatCurrency(alert.currentBalance, alert.currency)}.`
    }

    return `Current balance ${formatCurrency(alert.currentBalance, alert.currency)}. Refund ratio ${formatPercent(alert.refundRatio)}.`
}

export default function FinanceAlertsPanel({ overview }: FinanceAlertsPanelProps) {
    const alertsQuery = useQuery({
        queryKey: ['super-admin', 'finance', 'alerts', 'default'],
        queryFn: () =>
            getSuperAdminVendorFinanceAlerts({
                page: 1,
                pageSize: 6,
                highRefundRatioThreshold: 0.25,
                inactiveDays: 30,
            }),
        staleTime: 30_000,
        refetchInterval: 30_000,
        refetchOnWindowFocus: true,
    })

    const platformShortfall = Math.max(0, Number(overview?.payablesBalance ?? 0) - Number(overview?.cashBalance ?? 0))
    const lowPlatformCashAlert = platformShortfall > 0
        ? {
            id: 'platform-cash-coverage',
            severity: 'CRITICAL' as const,
            title: 'Low platform cash',
            message: `Platform cash is short by ${formatCurrency(platformShortfall, overview?.currency)} against current vendor payables.`,
            detail: `Cash balance ${formatCurrency(overview?.cashBalance ?? 0, overview?.currency)} versus payables ${formatCurrency(overview?.payablesBalance ?? 0, overview?.currency)}.`,
            icon: Wallet,
        }
        : null

    const vendorAlerts = alertsQuery.data?.alerts ?? []

    return (
        <section className="rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
            <div className="border-b border-slate-100 px-6 py-5">
                <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
                    <div>
                        <div className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-4 py-2 text-sm font-semibold text-slate-700">
                            <ShieldAlert className="h-4 w-4" />
                            Alerts
                        </div>
                        <h2 className="mt-4 text-2xl font-semibold text-slate-950">
                            High vendor debt, payable delay, and platform cash warnings
                        </h2>
                        <p className="mt-3 max-w-3xl text-sm leading-7 text-slate-500">
                            These warnings surface CommerceHub risks that need finance operations attention before they affect vendor trust or payout continuity.
                        </p>
                    </div>
                </div>
            </div>

            <div className="space-y-4 px-6 py-6">
                {lowPlatformCashAlert ? (() => {
                    const Icon = lowPlatformCashAlert.icon

                    return (
                        <div className={`rounded-[28px] border px-5 py-4 ${getAlertTone(lowPlatformCashAlert.severity)}`}>
                            <div className="flex items-start gap-3">
                                <Icon className="mt-0.5 h-5 w-5 shrink-0" />
                                <div>
                                    <p className="font-semibold">{lowPlatformCashAlert.title}</p>
                                    <p className="mt-1 text-sm">{lowPlatformCashAlert.message}</p>
                                    <p className="mt-2 text-sm opacity-80">{lowPlatformCashAlert.detail}</p>
                                </div>
                            </div>
                        </div>
                    )
                })() : null}

                {alertsQuery.isLoading && vendorAlerts.length === 0 ? (
                    <div className="rounded-[28px] border border-slate-200 bg-slate-50 px-5 py-8 text-sm text-slate-500">
                        Loading CommerceHub finance alerts...
                    </div>
                ) : null}

                {alertsQuery.isError ? (
                    <div className="rounded-[28px] border border-rose-200 bg-rose-50 px-5 py-6 text-sm text-rose-800">
                        Unable to load vendor alerts right now.
                    </div>
                ) : null}

                {vendorAlerts.map((alert) => {
                    const Icon = alert.alertType === 'LARGE_PAYABLE_DELAY' ? Landmark : AlertTriangle

                    return (
                        <div
                            key={`${alert.vendorId}:${alert.alertType}`}
                            className={`rounded-[28px] border px-5 py-4 ${getAlertTone(alert.severity)}`}
                        >
                            <div className="flex items-start gap-3">
                                <Icon className="mt-0.5 h-5 w-5 shrink-0" />
                                <div>
                                    <p className="font-semibold">{alert.message}</p>
                                    <p className="mt-1 text-sm opacity-85">{buildVendorAlertDetail(alert)}</p>
                                </div>
                            </div>
                        </div>
                    )
                })}

                {!lowPlatformCashAlert && vendorAlerts.length === 0 && !alertsQuery.isLoading ? (
                    <div className="rounded-[28px] border border-emerald-200 bg-emerald-50 px-5 py-8 text-sm text-emerald-900">
                        No active CommerceHub finance warnings are currently open.
                    </div>
                ) : null}
            </div>
        </section>
    )
}

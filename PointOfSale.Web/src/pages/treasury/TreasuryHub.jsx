/**
 * Treasury Hub
 *
 * Dynamic tenant catalog presentation. Rows come exclusively from
 * GET /api/treasury/accounts and `accountType` is the only role authority:
 * exact Cashbox/Bank = available now, exact Clearing = money in transit.
 * Account codes are identity only and never determine role, totals, or style.
 */

import { useState, useEffect, useCallback, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../../contexts/SettingsContext'
import { useToast } from '../../components/Toast'
import ReportTruthBadge from '../../components/ReportTruthBadge'
import * as treasuryApi from '../../services/treasuryApi'
import {
    getLocalizedTreasuryAccountName,
    isClearingAccount,
    partitionTreasuryCatalog,
    sumTreasuryBalances,
} from '../../services/treasuryAccountCatalog'
import { isRequestCanceled } from '../../services/api'
import { useRTL } from '../../hooks/useRTL'
import { formatTime } from '../../utils/dateFormat'

const TYPE_STYLE = {
    Cashbox: {
        icon: '💵',
        gradient: 'from-emerald-50 to-emerald-100',
        border: 'border-emerald-200',
        text: 'text-emerald-600',
        textBold: 'text-emerald-900',
    },
    Bank: {
        icon: '🏦',
        gradient: 'from-blue-50 to-blue-100',
        border: 'border-blue-200',
        text: 'text-blue-600',
        textBold: 'text-blue-900',
    },
    Clearing: {
        icon: '⏳',
        gradient: 'from-amber-50 to-amber-100',
        border: 'border-amber-200',
        text: 'text-amber-600',
        textBold: 'text-amber-900',
    },
}

const FALLBACK_STYLE = {
    icon: '📊',
    gradient: 'from-gray-50 to-gray-100',
    border: 'border-gray-200',
    text: 'text-gray-600',
    textBold: 'text-gray-900',
}

const styleForType = (accountType) => TYPE_STYLE[accountType] || FALLBACK_STYLE

const roleLabelKeyForType = (accountType) => {
    if (accountType === 'Cashbox') return 'treasury.cashbox'
    if (accountType === 'Bank') return 'treasury.bank'
    return 'treasury.clearingAccount'
}

const roleExplanationKeyForType = (accountType) => {
    if (accountType === 'Cashbox') return 'treasury.cashboxRole'
    if (accountType === 'Bank') return 'treasury.bankRole'
    return 'treasury.clearingRole'
}

export default function TreasuryHub() {
    const { t, i18n } = useTranslation()
    const navigate = useNavigate()
    const { formatCurrency } = useSettings()
    const { dir, locale } = useRTL()
    const { showError } = useToast()

    const [loading, setLoading] = useState(true)
    const [refreshing, setRefreshing] = useState(false)
    const [catalog, setCatalog] = useState([])
    const [catalogError, setCatalogError] = useState('')
    const [dashboard, setDashboard] = useState(null)
    const activeRequestControllerRef = useRef(null)

    const loadData = useCallback(async (silent = false, signal) => {
        try {
            if (!silent) setLoading(true)
            else setRefreshing(true)
            setCatalogError('')

            const today = new Date()
            const from = new Date(today.getFullYear(), today.getMonth(), 1)

            const [catalogResult, dashboardResult] = await Promise.allSettled([
                treasuryApi.getTreasuryAccounts({ signal }),
                treasuryApi.getTreasuryDashboard(from, today, { signal }),
            ])

            if (signal?.aborted) return

            if (catalogResult.status === 'fulfilled') {
                setCatalog(Array.isArray(catalogResult.value) ? catalogResult.value : [])
                setCatalogError('')
            } else if (!isRequestCanceled(catalogResult.reason)) {
                const message = t('treasury.catalogLoadFailed', 'The Treasury catalog could not be loaded. Balances are hidden until you retry.')
                setCatalog([])
                setCatalogError(message)
                if (!silent) showError(message)
            }

            if (dashboardResult.status === 'fulfilled') {
                setDashboard(dashboardResult.value)
            } else if (!isRequestCanceled(dashboardResult.reason)) {
                console.error('Failed to load Treasury dashboard data:', dashboardResult.reason)
                if (!silent && catalogResult.status === 'fulfilled') {
                    showError(t('treasury.loadError', 'Failed to load treasury data'))
                }
            }
        } catch (error) {
            if (isRequestCanceled(error)) return
            console.error('Failed to load treasury data:', error)
            if (!silent) showError(t('treasury.loadError', 'Failed to load treasury data'))
        } finally {
            if (!signal?.aborted) {
                setLoading(false)
                setRefreshing(false)
            }
        }
    }, [showError, t])

    const runLoadData = useCallback((silent = false) => {
        if (silent && activeRequestControllerRef.current) return
        activeRequestControllerRef.current?.abort()
        const controller = new AbortController()
        activeRequestControllerRef.current = controller
        void loadData(silent, controller.signal).finally(() => {
            if (activeRequestControllerRef.current === controller) {
                activeRequestControllerRef.current = null
            }
        })
    }, [loadData])

    useEffect(() => {
        runLoadData()
        const interval = setInterval(() => runLoadData(true), 60000)
        return () => {
            clearInterval(interval)
            activeRequestControllerRef.current?.abort()
            activeRequestControllerRef.current = null
        }
    }, [runLoadData])

    const { available, clearing } = partitionTreasuryCatalog(catalog)
    const totalCash = sumTreasuryBalances(available)
    const totalClearing = sumTreasuryBalances(clearing)
    const language = i18n?.language

    if (loading) {
        return (
            <div className="flex items-center justify-center h-96">
                <div className="text-center">
                    <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary-600 mx-auto" aria-hidden="true"></div>
                    <p className="mt-4 text-gray-600" role="status">{t('common.loading')}</p>
                </div>
            </div>
        )
    }

    const renderAccountCard = (account) => {
        const style = styleForType(account.accountType)
        const displayName = getLocalizedTreasuryAccountName(account, language)
        const balance = Number(account.currentBalance) || 0
        const clearingRow = isClearingAccount(account)
        return (
            <button
                key={account.accountCode}
                type="button"
                onClick={() => navigate(`/treasury/account/${account.accountCode}`)}
                aria-label={`${displayName} (${account.accountCode}): ${formatCurrency(balance)}`}
                className={`bg-gradient-to-br ${style.gradient} border ${style.border} rounded-xl p-6 text-start cursor-pointer hover:shadow-lg transition-all duration-200 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary-600 focus-visible:ring-offset-2`}
            >
                <div className="flex justify-between items-start gap-3">
                    <div className="min-w-0">
                        <p className={`text-sm font-medium ${style.text}`}>
                            <span aria-hidden="true">{style.icon}</span>{' '}
                            {t(roleLabelKeyForType(account.accountType), account.accountType)}
                        </p>
                        <p className="text-lg font-bold text-gray-900 mt-1 break-words">{displayName}</p>
                        <p className={`text-3xl font-bold ${style.textBold} mt-2`}>
                            {formatCurrency(balance)}
                        </p>
                        <p className="text-xs text-gray-500 mt-1">
                            {t('treasury.accountCode', 'Code')}: <bdi>{account.accountCode}</bdi>
                        </p>
                        <p className="text-xs text-gray-600 mt-1">
                            {t(roleExplanationKeyForType(account.accountType), '')}
                        </p>
                    </div>
                    <div className="text-4xl opacity-30" aria-hidden="true">
                        {style.icon}
                    </div>
                </div>

                {clearingRow && balance !== 0 && (
                    <div className="mt-4 pt-4 border-t border-white/50">
                        <span className="inline-flex items-center px-2 py-1 rounded-full text-xs font-medium bg-amber-200 text-amber-800">
                            ⏳ {t('treasury.pendingSettlement', 'Pending Settlement')}
                        </span>
                    </div>
                )}

                <div className="mt-4 text-end">
                    <span className={`text-sm ${style.text} hover:underline`}>
                        {t('treasury.viewMovements', 'View Movements')} <span aria-hidden="true" className="inline-block rtl:rotate-180">→</span>
                    </span>
                </div>
            </button>
        )
    }

    return (
        <div dir={dir} className="space-y-6">
            <ReportTruthBadge type="financial" />

            <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
                <div>
                    <h2 className="text-3xl font-bold text-gray-900">
                        <span aria-hidden="true">🏦 </span>{t('treasury.hub', 'Treasury Hub')}
                    </h2>
                    <p className="text-gray-600 mt-1">
                        {t('treasury.hubSubtitle', 'Available cash and money in transit from the Treasury catalog')}
                    </p>
                </div>
                <div className="flex w-full gap-3 sm:w-auto">
                    <button
                        onClick={() => navigate('/treasury')}
                        className="btn-secondary flex-1 sm:flex-none"
                    >
                        📊 {t('treasury.dashboard', 'Dashboard')}
                    </button>
                    <button
                        onClick={() => runLoadData(true)}
                        disabled={refreshing}
                        className="btn-secondary flex flex-1 items-center justify-center gap-2 sm:flex-none"
                    >
                        <svg className={`w-4 h-4 ${refreshing ? 'animate-spin' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                        </svg>
                        {refreshing ? t('common.refreshing') : t('common.refresh')}
                    </button>
                </div>
            </div>

            {catalogError && (
                <div className="bg-red-50 border border-red-200 rounded-lg p-4 flex flex-wrap items-center justify-between gap-3" role="alert">
                    <p className="text-sm text-red-800">{catalogError}</p>
                    <button type="button" onClick={() => runLoadData()} className="btn-secondary text-sm">
                        {t('treasury.retry', 'Retry')}
                    </button>
                </div>
            )}

            <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
                <div className="bg-gradient-to-br from-green-50 to-green-100 border border-green-200 rounded-xl p-6">
                    <p className="text-sm font-medium text-green-600">
                        💰 {t('treasury.totalCashAndBank', 'Total Cash & Bank')}
                    </p>
                    <p className="mt-2 break-words text-3xl font-bold text-green-900 sm:text-4xl">
                        {catalogError ? t('common.unavailable', 'Unavailable') : formatCurrency(totalCash)}
                    </p>
                    <p className="text-xs text-green-700 mt-2">
                        {t('treasury.availableCashTotal', 'Available Cashbox + Bank')}
                    </p>
                </div>
                <div className="bg-gradient-to-br from-amber-50 to-amber-100 border border-amber-200 rounded-xl p-6">
                    <p className="text-sm font-medium text-amber-600">
                        ⏳ {t('treasury.pendingClearing', 'Pending Clearing')}
                    </p>
                    <p className="mt-2 break-words text-3xl font-bold text-amber-900 sm:text-4xl">
                        {catalogError ? t('common.unavailable', 'Unavailable') : formatCurrency(totalClearing)}
                    </p>
                    <p className="text-xs text-amber-700 mt-2">
                        {t('treasury.pendingClearingTotal', 'Pending Clearing total')}
                    </p>
                </div>
                <div className={`bg-gradient-to-br rounded-xl p-6 ${(dashboard?.netProfit || 0) >= 0
                    ? 'from-emerald-50 to-emerald-100 border border-emerald-200'
                    : 'from-red-50 to-red-100 border border-red-200'
                    }`}>
                    <p className={`text-sm font-medium ${(dashboard?.netProfit || 0) >= 0 ? 'text-emerald-600' : 'text-red-600'}`}>
                        📈 {t('treasury.netProfitMTD', 'Net Profit (MTD)')}
                    </p>
                    <p className={`mt-2 break-words text-3xl font-bold sm:text-4xl ${(dashboard?.netProfit || 0) >= 0 ? 'text-emerald-900' : 'text-red-900'}`}>
                        {formatCurrency(dashboard?.netProfit || 0)}
                    </p>
                </div>
            </div>

            {!catalogError && (catalog.length === 0 ? (
                <div className="bg-white rounded-lg shadow p-10 text-center" role="status">
                    <p className="text-4xl" aria-hidden="true">🏦</p>
                    <p className="mt-3 font-semibold text-gray-900">
                        {t('treasury.catalogEmpty', 'No Treasury accounts are mapped for this tenant.')}
                    </p>
                    <p className="mt-1 text-sm text-gray-600">
                        {t('treasury.catalogEmptyHint', 'Ask an Owner to map an active Cashbox or Bank account before using Treasury.')}
                    </p>
                </div>
            ) : (
                <>
                    <section aria-labelledby="treasury-available-heading" className="space-y-4">
                        <div>
                            <h3 id="treasury-available-heading" className="text-xl font-bold text-gray-900">
                                💰 {t('treasury.availableNow', 'Available now')} ({formatCurrency(totalCash)})
                            </h3>
                            <p className="text-sm text-gray-600 mt-1">
                                {t('treasury.availableNowExplanation', 'Mapped Cashbox and Bank accounts only. This is money available now.')}
                            </p>
                        </div>
                        {available.length === 0 ? (
                            <p className="text-sm text-gray-600" role="status">
                                {t('treasury.catalogEmpty', 'No Treasury accounts are mapped for this tenant.')}
                            </p>
                        ) : (
                            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                                {available.map(renderAccountCard)}
                            </div>
                        )}
                    </section>

                    <section aria-labelledby="treasury-transit-heading" className="space-y-4">
                        <div>
                            <h3 id="treasury-transit-heading" className="text-xl font-bold text-gray-900">
                                ⏳ {t('treasury.inTransit', 'In transit')} ({formatCurrency(totalClearing)})
                            </h3>
                             <p className="text-sm text-gray-600 mt-1">
                                 {t('treasury.inTransitExplanation', 'Mapped Clearing accounts hold money in transit. Settlement requires provider or carrier evidence and cannot be moved with a Treasury transfer.')}
                             </p>
                             <p className="text-sm text-gray-600 mt-1" role="note">
                                 {t('treasury.clearingSettlementUnavailable', 'COD and gateway clearing settlement is currently unavailable because authenticated carrier remittance or provider payout evidence is not integrated. Clearing balances cannot be moved through Treasury Transfers.')}
                             </p>
                        </div>
                        {clearing.length === 0 ? (
                            <p className="text-sm text-gray-600" role="status">
                                {t('treasury.noPendingClearing', 'No pending balances in clearing accounts')}
                            </p>
                        ) : (
                            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                                {clearing.map(renderAccountCard)}
                            </div>
                        )}
                    </section>
                </>
            ))}

            <div className="bg-white rounded-lg shadow p-6">
                <h3 className="text-lg font-semibold text-gray-900 mb-2">
                    🔗 {t('treasury.quickLinks', 'Quick Links')}
                </h3>
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
                    <button
                        onClick={() => navigate('/treasury/trial-balance')}
                        className="p-4 bg-gray-50 hover:bg-gray-100 rounded-lg transition-colors text-start"
                    >
                        <span className="text-2xl" aria-hidden="true">⚖️</span>
                        <p className="font-medium text-gray-900 mt-2">
                            {t('treasury.trialBalance', 'Trial Balance')}
                        </p>
                    </button>
                    <button
                        onClick={() => navigate('/treasury/profit-loss')}
                        className="p-4 bg-gray-50 hover:bg-gray-100 rounded-lg transition-colors text-start"
                    >
                        <span className="text-2xl" aria-hidden="true">📈</span>
                        <p className="font-medium text-gray-900 mt-2">
                            {t('treasury.profitLoss', 'Profit & Loss')}
                        </p>
                    </button>
                    <button
                        onClick={() => navigate('/treasury/balance-sheet')}
                        className="p-4 bg-gray-50 hover:bg-gray-100 rounded-lg transition-colors text-start"
                    >
                        <span className="text-2xl" aria-hidden="true">📋</span>
                        <p className="font-medium text-gray-900 mt-2">
                            {t('treasury.balanceSheet', 'Balance Sheet')}
                        </p>
                    </button>
                    <button
                        onClick={() => navigate('/treasury/general-ledger')}
                        className="p-4 bg-gray-50 hover:bg-gray-100 rounded-lg transition-colors text-start"
                    >
                        <span className="text-2xl" aria-hidden="true">📖</span>
                        <p className="font-medium text-gray-900 mt-2">
                            {t('treasury.generalLedger', 'General Ledger')}
                        </p>
                    </button>
                </div>
            </div>

            <div className="text-center py-4 text-sm text-gray-500 border-t">
                <p>
                    <span aria-hidden="true">📊 </span>{t('treasury.ledgerSourced', 'All data sourced from immutable ledger')} •
                    <span aria-hidden="true">🔒 </span>{t('treasury.singleSource', 'Single source of truth')} •
                    <span aria-hidden="true">⏱️ </span>{t('treasury.lastUpdated', 'Last updated')}: {formatTime(new Date(), locale)}
                </p>
            </div>
        </div>
    )
}

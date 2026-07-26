/**
 * Treasury & Finance Control Center Dashboard
 * 
 * PHASE 5: Financial Command Center for Business Owners & Accountants
 * 
 * Answers in <5 seconds:
 * - How much cash do I have now?
 * - Where is my money?
 * - What is pending?
 * - Am I profitable?
 * - What is risky?
 */

import { useState, useEffect, useCallback, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../../contexts/SettingsContext'
import { useToast } from '../../components/Toast'
import LineChart from '../../components/LineChart'
import DonutChart from '../../components/DonutChart'
import ReportTruthBadge from '../../components/ReportTruthBadge'
import { isRequestCanceled } from '../../services/api'
import * as finApi from '../../services/financialReportsApi'
import { useRTL } from '../../hooks/useRTL'
import { formatDate, formatTime } from '../../utils/dateFormat'

export default function TreasuryDashboard() {
    const { t } = useTranslation()
    const navigate = useNavigate()
    const { showError } = useToast()
    const { formatCurrency } = useSettings()
    const { dir, locale } = useRTL()

    // State
    const [loading, setLoading] = useState(true)
    const [refreshing, setRefreshing] = useState(false)
    const [dashboard, setDashboard] = useState(null)
    const [balanceSheet, setBalanceSheet] = useState(null)
    const [profitLoss, setProfitLoss] = useState(null)
    const [clearingAging, setClearingAging] = useState(null)
    const [trialBalance, setTrialBalance] = useState(null)
    const activeRequestControllerRef = useRef(null)

    // Date range
    const [dateRange, setDateRange] = useState(() => finApi.getDefaultDateRange())

    // Load all data in parallel
    const loadData = useCallback(async (silent = false, signal) => {
        try {
            if (!silent) setLoading(true)
            else setRefreshing(true)

            const { from, to } = dateRange

            // Parallel API calls for performance
            const [dashboardData, bsData, plData, clearingData, tbData] = await Promise.all([
                finApi.getDashboard(from, to, !silent, { signal }),
                finApi.getBalanceSheet(to, { signal }),
                finApi.getProfitAndLoss(from, to, { signal }),
                finApi.getClearingAging(to, { signal }),
                finApi.getTrialBalance(from, to, { signal }),
            ])

            // Check if aborted after Promise.all resolves to prevent stale results
            if (signal?.aborted) {
                return
            }

            setDashboard(dashboardData)
            setBalanceSheet(bsData)
            setProfitLoss(plData)
            setClearingAging(clearingData)
            setTrialBalance(tbData)
        } catch (error) {
            if (isRequestCanceled(error)) {
                return
            }

            console.error('Failed to load treasury data:', error)
            if (!silent) {
                showError(t('treasury.loadError', 'Failed to load financial data'))
            }
        } finally {
            if (!signal?.aborted) {
                setLoading(false)
                setRefreshing(false)
            }
        }
    }, [dateRange, showError, t])

    const runLoadData = useCallback((silent = false, skipIfBusy = false) => {
        // Skip if already loading and skipIfBusy is true (for auto-refresh)
        if (skipIfBusy && activeRequestControllerRef.current) {
            return
        }

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

        // Auto-refresh every 60 seconds (skip if already loading)
        const interval = setInterval(() => runLoadData(true, true), 60000)
        return () => {
            clearInterval(interval)
            activeRequestControllerRef.current?.abort()
            activeRequestControllerRef.current = null
        }
    }, [runLoadData])

    // Calculate cash position from balance sheet
    const getCashPosition = () => {
        if (!balanceSheet?.assetLines) return { cash: 0, bank: 0, clearing: 0 }

        let cash = 0, bank = 0, clearing = 0

        balanceSheet.assetLines.forEach(a => {
            const code = parseInt(a.accountCode)
            if (code >= 1000 && code < 1050) cash += a.balance  // Cashbox
            else if (code >= 1050 && code < 1100) bank += a.balance  // Bank accounts
            else if (code >= 1100 && code < 1200) clearing += a.balance  // Clearing accounts
        })

        return { cash, bank, clearing }
    }

    const cashPosition = getCashPosition()

    // Format date for display
    const formatDateRange = () => {
        const from = formatDate(dateRange.from, locale, { month: 'short', day: 'numeric' })
        const to = formatDate(dateRange.to, locale, { month: 'short', day: 'numeric', year: 'numeric' })
        return `${from} - ${to}`
    }

    // Date range shortcuts
    const setThisMonth = () => setDateRange(finApi.getDefaultDateRange())
    const setThisYear = () => setDateRange(finApi.getFiscalYearRange())
    const setLastMonth = () => {
        const today = new Date()
        const from = new Date(today.getFullYear(), today.getMonth() - 1, 1)
        const to = new Date(today.getFullYear(), today.getMonth(), 0)
        setDateRange({ from, to })
    }

    if (loading) {
        return (
            <div className="flex items-center justify-center h-96">
                <div className="text-center">
                    <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary-600 mx-auto"></div>
                    <p className="mt-4 text-gray-600">{t('common.loading')}</p>
                </div>
            </div>
        )
    }

    return (
        <div dir={dir} className="space-y-6">
            {/* Phase C4: UI Truth Label */}
            <ReportTruthBadge type="financial" />

            {/* Header */}
            <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
                <div>
                    <h2 className="text-3xl font-bold text-gray-900">
                        🏦 {t('treasury.title', 'Treasury & Finance')}
                    </h2>
                    <p className="text-gray-600 mt-1">
                        {t('treasury.subtitle', 'Financial Control Center')} • {formatDateRange()}
                    </p>
                </div>
                <div className="flex items-center gap-3">
                    {/* Date Range Shortcuts */}
                    <div className="flex bg-gray-100 rounded-lg p-1">
                        <button
                            onClick={setThisMonth}
                            className="px-3 py-1 text-sm rounded-md hover:bg-white hover:shadow-sm transition-all"
                        >
                            {t('treasury.thisMonth', 'This Month')}
                        </button>
                        <button
                            onClick={setLastMonth}
                            className="px-3 py-1 text-sm rounded-md hover:bg-white hover:shadow-sm transition-all"
                        >
                            {t('treasury.lastMonth', 'Last Month')}
                        </button>
                        <button
                            onClick={setThisYear}
                            className="px-3 py-1 text-sm rounded-md hover:bg-white hover:shadow-sm transition-all"
                        >
                            {t('treasury.ytd', 'YTD')}
                        </button>
                    </div>

                    {/* Refresh Button */}
                    <button
                        onClick={() => runLoadData(true)}
                        disabled={refreshing}
                        className="btn-secondary flex items-center gap-2"
                    >
                        <svg className={`w-4 h-4 ${refreshing ? 'animate-spin' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                        </svg>
                        {refreshing ? t('common.refreshing') : t('common.refresh')}
                    </button>
                </div>
            </div>

            {/* ═══════════════════════════════════════════════════════════════════ */}
            {/* EXECUTIVE KPI STRIP - Top Bar */}
            {/* ═══════════════════════════════════════════════════════════════════ */}
            <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-4">
                {/* Total Cash & Bank */}
                <div className="card bg-gradient-to-br from-emerald-50 to-emerald-100 border-emerald-200">
                    <p className="text-sm font-medium text-emerald-600">
                        💵 {t('treasury.cashAndBank', 'Cash & Bank')}
                    </p>
                    <p className="text-2xl font-bold text-emerald-900 mt-1">
                        {formatCurrency(cashPosition.cash + cashPosition.bank)}
                    </p>
                </div>

                {/* Net Profit */}
                <div className={`card bg-gradient-to-br ${(dashboard?.netProfit || 0) >= 0
                    ? 'from-green-50 to-green-100 border-green-200'
                    : 'from-red-50 to-red-100 border-red-200'}`}>
                    <p className={`text-sm font-medium ${(dashboard?.netProfit || 0) >= 0 ? 'text-green-600' : 'text-red-600'}`}>
                        📈 {t('treasury.netProfit', 'Net Profit')}
                    </p>
                    <p className={`text-2xl font-bold mt-1 ${(dashboard?.netProfit || 0) >= 0 ? 'text-green-900' : 'text-red-900'}`}>
                        {formatCurrency(dashboard?.netProfit || 0)}
                    </p>
                    {dashboard?.netProfitMargin !== undefined && (
                        <p className="text-xs text-gray-500 mt-1">
                            {dashboard.netProfitMargin?.toFixed(1)}% {t('treasury.margin', 'margin')}
                        </p>
                    )}
                </div>

                {/* Revenue */}
                <div className="card bg-gradient-to-br from-blue-50 to-blue-100 border-blue-200">
                    <p className="text-sm font-medium text-blue-600">
                        💰 {t('treasury.revenue', 'Revenue')}
                    </p>
                    <p className="text-2xl font-bold text-blue-900 mt-1">
                        {formatCurrency(dashboard?.revenue || 0)}
                    </p>
                </div>

                {/* Expenses */}
                <div className="card bg-gradient-to-br from-orange-50 to-orange-100 border-orange-200">
                    <p className="text-sm font-medium text-orange-600">
                        📉 {t('treasury.expenses', 'Expenses')}
                    </p>
                    <p className="text-2xl font-bold text-orange-900 mt-1">
                        {formatCurrency((dashboard?.cogs || 0) + (dashboard?.expenses || 0))}
                    </p>
                </div>

                {/* Pending Clearing */}
                <div className={`card bg-gradient-to-br ${(dashboard?.pendingClearingAmount || 0) > 0
                    ? 'from-amber-50 to-amber-100 border-amber-200'
                    : 'from-gray-50 to-gray-100 border-gray-200'}`}>
                    <p className={`text-sm font-medium ${(dashboard?.pendingClearingAmount || 0) > 0 ? 'text-amber-600' : 'text-gray-600'}`}>
                        ⏳ {t('treasury.pendingClearing', 'Pending')}
                    </p>
                    <p className={`text-2xl font-bold mt-1 ${(dashboard?.pendingClearingAmount || 0) > 0 ? 'text-amber-900' : 'text-gray-900'}`}>
                        {formatCurrency(dashboard?.pendingClearingAmount || 0)}
                    </p>
                    {dashboard?.clearingAccountsWithBalance > 0 && (
                        <p className="text-xs text-amber-600 mt-1">
                            {dashboard.clearingAccountsWithBalance} {t('treasury.accounts', 'accounts')}
                        </p>
                    )}
                </div>

                {/* Current Ratio */}
                <div className={`card bg-gradient-to-br ${(dashboard?.currentRatio || 0) >= 1.5
                    ? 'from-teal-50 to-teal-100 border-teal-200'
                    : (dashboard?.currentRatio || 0) >= 1
                        ? 'from-yellow-50 to-yellow-100 border-yellow-200'
                        : 'from-red-50 to-red-100 border-red-200'}`}>
                    <p className="text-sm font-medium text-gray-600">
                        📊 {t('treasury.currentRatio', 'Current Ratio')}
                    </p>
                    <p className="text-2xl font-bold text-gray-900 mt-1">
                        {(dashboard?.currentRatio || 0).toFixed(2)}
                    </p>
                    <p className="text-xs text-gray-500 mt-1">
                        {(dashboard?.currentRatio || 0) >= 1.5 ? '✅ Healthy' :
                            (dashboard?.currentRatio || 0) >= 1 ? '⚠️ Watch' : '❌ Risk'}
                    </p>
                </div>
            </div>

            {/* ═══════════════════════════════════════════════════════════════════ */}
            {/* CASH POSITION PANEL + PROFITABILITY PANEL */}
            {/* ═══════════════════════════════════════════════════════════════════ */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                {/* Cash Position Panel */}
                <div className="card">
                    <h3 className="text-lg font-semibold text-gray-900 mb-4">
                        🏧 {t('treasury.cashPosition', 'Cash Position')}
                    </h3>
                    <div className="h-64">
                        <DonutChart
                            data={[
                                { label: t('treasury.cashbox', 'Cashbox'), value: cashPosition.cash },
                                { label: t('treasury.bank', 'Bank'), value: cashPosition.bank },
                                { label: t('treasury.clearing', 'Clearing'), value: cashPosition.clearing }
                            ].filter(d => d.value > 0)}
                            height={240}
                        />
                    </div>
                    <div className="grid grid-cols-3 gap-4 mt-4 pt-4 border-t">
                        <div className="text-center">
                            <p className="text-sm text-gray-500">{t('treasury.cashbox', 'Cashbox')}</p>
                            <p className="font-semibold text-gray-900">{formatCurrency(cashPosition.cash)}</p>
                        </div>
                        <div className="text-center">
                            <p className="text-sm text-gray-500">{t('treasury.bank', 'Bank')}</p>
                            <p className="font-semibold text-gray-900">{formatCurrency(cashPosition.bank)}</p>
                        </div>
                        <div className="text-center">
                            <p className="text-sm text-gray-500">{t('treasury.clearing', 'In Transit')}</p>
                            <p className="font-semibold text-amber-600">{formatCurrency(cashPosition.clearing)}</p>
                        </div>
                    </div>
                </div>

                {/* Profitability Panel */}
                <div className="card">
                    <h3 className="text-lg font-semibold text-gray-900 mb-4">
                        📊 {t('treasury.profitability', 'Profitability')}
                    </h3>
                    <div className="space-y-4">
                        {/* Revenue Bar */}
                        <div>
                            <div className="flex justify-between text-sm mb-1">
                                <span className="text-gray-600">{t('treasury.revenue', 'Revenue')}</span>
                                <span className="font-semibold text-blue-600">{formatCurrency(dashboard?.revenue || 0)}</span>
                            </div>
                            <div className="w-full bg-gray-200 rounded-full h-3">
                                <div className="bg-blue-500 h-3 rounded-full" style={{ width: '100%' }}></div>
                            </div>
                        </div>

                        {/* COGS Bar */}
                        <div>
                            <div className="flex justify-between text-sm mb-1">
                                <span className="text-gray-600">{t('treasury.cogs', 'Cost of Goods')}</span>
                                <span className="font-semibold text-orange-600">{formatCurrency(dashboard?.cogs || 0)}</span>
                            </div>
                            <div className="w-full bg-gray-200 rounded-full h-3">
                                <div
                                    className="bg-orange-500 h-3 rounded-full"
                                    style={{ width: `${dashboard?.revenue ? ((dashboard.cogs || 0) / dashboard.revenue * 100) : 0}%` }}
                                ></div>
                            </div>
                        </div>

                        {/* Gross Profit (visual indicator) */}
                        <div className="p-3 bg-green-50 rounded-lg">
                            <div className="flex justify-between">
                                <span className="text-green-700 font-medium">{t('treasury.grossProfit', 'Gross Profit')}</span>
                                <span className="font-bold text-green-800">{formatCurrency(dashboard?.grossProfit || 0)}</span>
                            </div>
                            <p className="text-xs text-green-600 mt-1">
                                {dashboard?.grossProfitMargin?.toFixed(1) || 0}% {t('treasury.margin', 'margin')}
                            </p>
                        </div>

                        {/* Expenses Bar */}
                        <div>
                            <div className="flex justify-between text-sm mb-1">
                                <span className="text-gray-600">{t('treasury.expenses', 'Operating Expenses')}</span>
                                <span className="font-semibold text-red-600">{formatCurrency(dashboard?.expenses || 0)}</span>
                            </div>
                            <div className="w-full bg-gray-200 rounded-full h-3">
                                <div
                                    className="bg-red-500 h-3 rounded-full"
                                    style={{ width: `${dashboard?.revenue ? ((dashboard.expenses || 0) / dashboard.revenue * 100) : 0}%` }}
                                ></div>
                            </div>
                        </div>

                        {/* Net Profit (visual indicator) */}
                        <div className={`p-3 rounded-lg ${(dashboard?.netProfit || 0) >= 0 ? 'bg-emerald-50' : 'bg-red-50'}`}>
                            <div className="flex justify-between">
                                <span className={`font-medium ${(dashboard?.netProfit || 0) >= 0 ? 'text-emerald-700' : 'text-red-700'}`}>
                                    {t('treasury.netProfit', 'Net Profit')}
                                </span>
                                <span className={`font-bold ${(dashboard?.netProfit || 0) >= 0 ? 'text-emerald-800' : 'text-red-800'}`}>
                                    {formatCurrency(dashboard?.netProfit || 0)}
                                </span>
                            </div>
                            <p className={`text-xs mt-1 ${(dashboard?.netProfit || 0) >= 0 ? 'text-emerald-600' : 'text-red-600'}`}>
                                {dashboard?.netProfitMargin?.toFixed(1) || 0}% {t('treasury.margin', 'margin')}
                            </p>
                        </div>
                    </div>
                </div>
            </div>

            {/* ═══════════════════════════════════════════════════════════════════ */}
            {/* CLEARING RISK PANEL + TRIAL BALANCE HEALTH */}
            {/* ═══════════════════════════════════════════════════════════════════ */}
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
                {/* Clearing Risk Panel (2/3 width) */}
                <div className="lg:col-span-2 card">
                    <div className="flex justify-between items-center mb-4">
                        <h3 className="text-lg font-semibold text-gray-900">
                            ⚠️ {t('treasury.clearingRisk', 'Clearing & Settlement Risk')}
                        </h3>
                        <button
                            onClick={() => navigate('/treasury/clearing-aging')}
                            className="text-sm text-primary-600 hover:text-primary-700"
                        >
                            {t('common.viewAll')} →
                        </button>
                    </div>

                    {clearingAging?.lines?.length > 0 ? (
                        <div className="overflow-x-auto">
                            <table className="min-w-full divide-y divide-gray-200">
                                <thead className="bg-gray-50">
                                    <tr>
                                        <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                                            {t('treasury.provider', 'Provider')}
                                        </th>
                                        <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase">
                                            {t('treasury.pending', 'Pending')}
                                        </th>
                                        <th className="px-4 py-3 text-center text-xs font-medium text-gray-500 uppercase">
                                            {t('treasury.age', 'Age')}
                                        </th>
                                        <th className="px-4 py-3 text-center text-xs font-medium text-gray-500 uppercase">
                                            {t('treasury.status', 'Status')}
                                        </th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-gray-200">
                                    {clearingAging.lines.slice(0, 5).map((line, idx) => {
                                        const isLate = (line.ageDays || 0) > 7
                                        return (
                                            <tr key={idx} className={`hover:bg-gray-50 ${isLate ? 'bg-red-50' : ''}`}>
                                                <td className="px-4 py-3">
                                                    <div className="font-medium text-gray-900">{line.accountName}</div>
                                                    <div className="text-xs text-gray-500">{line.accountCode}</div>
                                                </td>
                                                <td className="px-4 py-3 text-right">
                                                    <span className="font-semibold text-amber-600">
                                                        {formatCurrency(line.pendingAmount)}
                                                    </span>
                                                </td>
                                                <td className="px-4 py-3 text-center">
                                                    <span className={`font-medium ${isLate ? 'text-red-600' : 'text-gray-700'}`}>
                                                        {line.ageDays || 0} {t('common.days', 'days')}
                                                    </span>
                                                </td>
                                                <td className="px-4 py-3 text-center">
                                                    {isLate ? (
                                                        <span className="inline-flex px-2 py-1 text-xs font-semibold rounded-full bg-red-100 text-red-800">
                                                            🔴 {t('treasury.late', 'Late')}
                                                        </span>
                                                    ) : (
                                                        <span className="inline-flex px-2 py-1 text-xs font-semibold rounded-full bg-green-100 text-green-800">
                                                            🟢 {t('treasury.ok', 'OK')}
                                                        </span>
                                                    )}
                                                </td>
                                            </tr>
                                        )
                                    })}
                                </tbody>
                            </table>
                        </div>
                    ) : (
                        <div className="flex items-center justify-center h-32 text-gray-500">
                            <div className="text-center">
                                <span className="text-4xl">✨</span>
                                <p className="mt-2">{t('treasury.noPendingClearing', 'All clearing accounts settled!')}</p>
                            </div>
                        </div>
                    )}
                </div>

                {/* Trial Balance Health Widget (1/3 width) */}
                <div className="card">
                    <h3 className="text-lg font-semibold text-gray-900 mb-4">
                        ⚖️ {t('treasury.ledgerHealth', 'Ledger Health')}
                    </h3>

                    <div className="flex flex-col items-center justify-center py-6">
                        {trialBalance?.isBalanced ? (
                            <>
                                <div className="w-24 h-24 rounded-full bg-green-100 flex items-center justify-center mb-4">
                                    <span className="text-5xl">✅</span>
                                </div>
                                <p className="text-xl font-bold text-green-600">
                                    {t('treasury.balanced', 'BALANCED')}
                                </p>
                                <p className="text-sm text-gray-500 mt-2">
                                    {t('treasury.debitsCreditMatch', 'Debits equal Credits')}
                                </p>
                            </>
                        ) : (
                            <>
                                <div className="w-24 h-24 rounded-full bg-red-100 flex items-center justify-center mb-4">
                                    <span className="text-5xl">❌</span>
                                </div>
                                <p className="text-xl font-bold text-red-600">
                                    {t('treasury.variance', 'VARIANCE')}
                                </p>
                                <p className="text-2xl font-bold text-red-700 mt-2">
                                    {formatCurrency(Math.abs(trialBalance?.variance || 0))}
                                </p>
                                <p className="text-sm text-red-500 mt-2">
                                    {t('treasury.investigateNeeded', 'Investigation needed!')}
                                </p>
                            </>
                        )}
                    </div>

                    <button
                        onClick={() => navigate('/treasury/trial-balance')}
                        className="w-full mt-4 btn-secondary text-sm"
                    >
                        {t('treasury.viewTrialBalance', 'View Trial Balance')} →
                    </button>
                </div>
            </div>

            {/* ═══════════════════════════════════════════════════════════════════ */}
            {/* BALANCE SHEET SUMMARY + QUICK DRILL ACTIONS */}
            {/* ═══════════════════════════════════════════════════════════════════ */}
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
                {/* Balance Sheet Summary */}
                <div className="lg:col-span-2 card">
                    <div className="flex justify-between items-center mb-4">
                        <h3 className="text-lg font-semibold text-gray-900">
                            📋 {t('treasury.balanceSheetSummary', 'Balance Sheet Summary')}
                        </h3>
                        <span className={`px-2 py-1 text-xs font-semibold rounded-full ${dashboard?.balanceSheetBalanced
                            ? 'bg-green-100 text-green-800'
                            : 'bg-red-100 text-red-800'
                            }`}>
                            {dashboard?.balanceSheetBalanced ? '✓ Balanced' : '✗ Unbalanced'}
                        </span>
                    </div>

                    <div className="grid grid-cols-3 gap-6">
                        {/* Assets */}
                        <div className="p-4 bg-blue-50 rounded-lg">
                            <p className="text-sm text-blue-600 font-medium">{t('treasury.totalAssets', 'Total Assets')}</p>
                            <p className="text-2xl font-bold text-blue-900 mt-2">
                                {formatCurrency(dashboard?.totalAssets || 0)}
                            </p>
                        </div>

                        {/* Liabilities */}
                        <div className="p-4 bg-orange-50 rounded-lg">
                            <p className="text-sm text-orange-600 font-medium">{t('treasury.totalLiabilities', 'Total Liabilities')}</p>
                            <p className="text-2xl font-bold text-orange-900 mt-2">
                                {formatCurrency(dashboard?.totalLiabilities || 0)}
                            </p>
                        </div>

                        {/* Equity */}
                        <div className="p-4 bg-green-50 rounded-lg">
                            <p className="text-sm text-green-600 font-medium">{t('treasury.totalEquity', 'Total Equity')}</p>
                            <p className="text-2xl font-bold text-green-900 mt-2">
                                {formatCurrency(dashboard?.totalEquity || 0)}
                            </p>
                        </div>
                    </div>

                    {/* Equation Visualization */}
                    <div className="mt-6 p-4 bg-gray-50 rounded-lg">
                        <div className="flex items-center justify-center text-lg">
                            <span className="font-semibold text-blue-600">{formatCurrency(dashboard?.totalAssets || 0)}</span>
                            <span className="mx-3 text-gray-400">=</span>
                            <span className="font-semibold text-orange-600">{formatCurrency(dashboard?.totalLiabilities || 0)}</span>
                            <span className="mx-2 text-gray-400">+</span>
                            <span className="font-semibold text-green-600">{formatCurrency(dashboard?.totalEquity || 0)}</span>
                        </div>
                        <p className="text-center text-xs text-gray-500 mt-2">
                            {t('treasury.accountingEquation', 'Assets = Liabilities + Equity')}
                        </p>
                    </div>
                </div>

                {/* Quick Drill Actions */}
                <div className="card">
                    <h3 className="text-lg font-semibold text-gray-900 mb-4">
                        🔍 {t('treasury.quickReports', 'Quick Reports')}
                    </h3>
                    <div className="space-y-2">
                        <button
                            onClick={() => navigate('/treasury/trial-balance')}
                            className="w-full p-3 text-left bg-gray-50 hover:bg-gray-100 rounded-lg transition-colors flex items-center gap-3"
                        >
                            <span className="text-2xl">⚖️</span>
                            <div>
                                <p className="font-medium text-gray-900">{t('treasury.trialBalance', 'Trial Balance')}</p>
                                <p className="text-xs text-gray-500">{t('treasury.trialBalanceDesc', 'Debit/Credit verification')}</p>
                            </div>
                        </button>

                        <button
                            onClick={() => navigate('/treasury/profit-loss')}
                            className="w-full p-3 text-left bg-gray-50 hover:bg-gray-100 rounded-lg transition-colors flex items-center gap-3"
                        >
                            <span className="text-2xl">📈</span>
                            <div>
                                <p className="font-medium text-gray-900">{t('treasury.profitLoss', 'Profit & Loss')}</p>
                                <p className="text-xs text-gray-500">{t('treasury.profitLossDesc', 'Income statement')}</p>
                            </div>
                        </button>

                        <button
                            onClick={() => navigate('/treasury/balance-sheet')}
                            className="w-full p-3 text-left bg-gray-50 hover:bg-gray-100 rounded-lg transition-colors flex items-center gap-3"
                        >
                            <span className="text-2xl">📋</span>
                            <div>
                                <p className="font-medium text-gray-900">{t('treasury.balanceSheet', 'Balance Sheet')}</p>
                                <p className="text-xs text-gray-500">{t('treasury.balanceSheetDesc', 'Financial position')}</p>
                            </div>
                        </button>

                        <button
                            onClick={() => navigate('/treasury/cash-flow')}
                            className="w-full p-3 text-left bg-gray-50 hover:bg-gray-100 rounded-lg transition-colors flex items-center gap-3"
                        >
                            <span className="text-2xl">💧</span>
                            <div>
                                <p className="font-medium text-gray-900">{t('treasury.cashFlow', 'Cash Flow')}</p>
                                <p className="text-xs text-gray-500">{t('treasury.cashFlowDesc', 'Cash movement')}</p>
                            </div>
                        </button>

                        <button
                            onClick={() => navigate('/treasury/general-ledger')}
                            className="w-full p-3 text-left bg-gray-50 hover:bg-gray-100 rounded-lg transition-colors flex items-center gap-3"
                        >
                            <span className="text-2xl">📖</span>
                            <div>
                                <p className="font-medium text-gray-900">{t('treasury.generalLedger', 'General Ledger')}</p>
                                <p className="text-xs text-gray-500">{t('treasury.generalLedgerDesc', 'Account transactions')}</p>
                            </div>
                        </button>
                    </div>
                </div>
            </div>

            {/* Trust Footer */}
            <div className="text-center py-6 text-sm text-gray-500 border-t">
                <p>
                    📊 {t('treasury.ledgerSourced', 'All data sourced from immutable ledger')} •
                    🔒 {t('treasury.fullyReconciled', 'Fully reconciled')} •
                    ⏱️ {t('treasury.lastUpdated', 'Last updated')}: {formatTime(new Date(), locale)}
                </p>
            </div>
        </div>
    )
}

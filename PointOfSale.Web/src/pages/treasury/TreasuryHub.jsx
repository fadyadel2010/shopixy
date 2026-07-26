/**
 * Treasury Hub
 * 
 * PHASE FTM: Ledger-Driven Treasury UI
 * 
 * Central hub for all Treasury accounts.
 * Shows balances and provides navigation to individual account viewers.
 */

import { useState, useEffect, useCallback, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../../contexts/SettingsContext'
import { useToast } from '../../components/Toast'
import ReportTruthBadge from '../../components/ReportTruthBadge'
import * as treasuryApi from '../../services/treasuryApi'
import { isRequestCanceled } from '../../services/api'

// Account card configuration
const ACCOUNT_CARDS = [
    {
        code: treasuryApi.ACCOUNT_CODES.CASHBOX_MAIN,
        name: 'Cash',
        icon: '💵',
        gradient: 'from-emerald-50 to-emerald-100',
        border: 'border-emerald-200',
        text: 'text-emerald-600',
        textBold: 'text-emerald-900'
    },
    {
        code: treasuryApi.ACCOUNT_CODES.BANK_ACCOUNT,
        name: 'Bank',
        icon: '🏦',
        gradient: 'from-blue-50 to-blue-100',
        border: 'border-blue-200',
        text: 'text-blue-600',
        textBold: 'text-blue-900'
    },
    {
        code: treasuryApi.ACCOUNT_CODES.VISA_CLEARING,
        name: 'Visa Clearing',
        icon: '💳',
        gradient: 'from-purple-50 to-purple-100',
        border: 'border-purple-200',
        text: 'text-purple-600',
        textBold: 'text-purple-900',
        isClearing: true
    },
    {
        code: treasuryApi.ACCOUNT_CODES.STRIPE_CLEARING,
        name: 'Stripe Clearing',
        icon: '⚡',
        gradient: 'from-indigo-50 to-indigo-100',
        border: 'border-indigo-200',
        text: 'text-indigo-600',
        textBold: 'text-indigo-900',
        isClearing: true
    },
    {
        code: treasuryApi.ACCOUNT_CODES.COD_CLEARING,
        name: 'COD Clearing',
        icon: '📦',
        gradient: 'from-amber-50 to-amber-100',
        border: 'border-amber-200',
        text: 'text-amber-600',
        textBold: 'text-amber-900',
        isClearing: true
    },
    {
        code: treasuryApi.ACCOUNT_CODES.WALLET_CLEARING,
        name: 'Wallet Clearing',
        icon: '📱',
        gradient: 'from-pink-50 to-pink-100',
        border: 'border-pink-200',
        text: 'text-pink-600',
        textBold: 'text-pink-900',
        isClearing: true
    }
]

export default function TreasuryHub() {
    const { t } = useTranslation()
    const navigate = useNavigate()
    const { formatCurrency } = useSettings()
    const { showError } = useToast()

    // State
    const [loading, setLoading] = useState(true)
    const [refreshing, setRefreshing] = useState(false)
    const [balances, setBalances] = useState({})
    const [dashboard, setDashboard] = useState(null)
    const activeRequestControllerRef = useRef(null)

    // Load data
    const loadData = useCallback(async (silent = false, signal) => {
        try {
            if (!silent) setLoading(true)
            else setRefreshing(true)

            const accountCodes = ACCOUNT_CARDS.map(c => c.code)
            const today = new Date()
            const from = new Date(today.getFullYear(), today.getMonth(), 1)

            const [balancesData, dashboardData] = await Promise.all([
                treasuryApi.getTreasuryBalances(accountCodes, { signal }),
                treasuryApi.getTreasuryDashboard(from, today, { signal }),
            ])

            // Check if aborted after Promise.all resolves to prevent stale results
            if (signal?.aborted) {
                return
            }

            setBalances(balancesData || {})
            setDashboard(dashboardData)
        } catch (error) {
            if (isRequestCanceled(error)) {
                return
            }

            console.error('Failed to load treasury data:', error)
            if (!silent) {
                showError(t('treasury.loadError', 'Failed to load treasury data'))
            }
        } finally {
            if (!signal?.aborted) {
                setLoading(false)
                setRefreshing(false)
            }
        }
    }, [showError, t])

    const runLoadData = useCallback((silent = false) => {
        // Skip if silent refresh and already loading
        if (silent && activeRequestControllerRef.current) {
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

        // Auto-refresh every 60 seconds
        const interval = setInterval(() => runLoadData(true), 60000)
        return () => {
            clearInterval(interval)
            activeRequestControllerRef.current?.abort()
            activeRequestControllerRef.current = null
        }
    }, [runLoadData])

    // Calculate totals
    const totalCash = (balances[treasuryApi.ACCOUNT_CODES.CASHBOX_MAIN] || 0) +
        (balances[treasuryApi.ACCOUNT_CODES.BANK_ACCOUNT] || 0)

    const totalClearing = ACCOUNT_CARDS
        .filter(c => c.isClearing)
        .reduce((sum, c) => sum + (balances[c.code] || 0), 0)

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
        <div className="space-y-6">
            {/* Phase C4: UI Truth Label */}
            <ReportTruthBadge type="financial" />

            {/* Header */}
            <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
                <div>
                    <h2 className="text-3xl font-bold text-gray-900">
                        🏦 {t('treasury.hub', 'Treasury Hub')}
                    </h2>
                    <p className="text-gray-600 mt-1">
                        {t('treasury.hubSubtitle', 'Ledger-based account overview')}
                    </p>
                </div>
                <div className="flex items-center gap-3">
                    <button
                        onClick={() => navigate('/treasury')}
                        className="btn-secondary"
                    >
                        📊 {t('treasury.dashboard', 'Dashboard')}
                    </button>
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

            {/* Summary Cards */}
            <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
                <div className="bg-gradient-to-br from-green-50 to-green-100 border border-green-200 rounded-xl p-6">
                    <p className="text-sm font-medium text-green-600">
                        💰 {t('treasury.totalCashAndBank', 'Total Cash & Bank')}
                    </p>
                    <p className="text-4xl font-bold text-green-900 mt-2">
                        {formatCurrency(totalCash)}
                    </p>
                </div>
                <div className="bg-gradient-to-br from-amber-50 to-amber-100 border border-amber-200 rounded-xl p-6">
                    <p className="text-sm font-medium text-amber-600">
                        ⏳ {t('treasury.pendingClearing', 'Pending Clearing')}
                    </p>
                    <p className="text-4xl font-bold text-amber-900 mt-2">
                        {formatCurrency(totalClearing)}
                    </p>
                </div>
                <div className={`bg-gradient-to-br rounded-xl p-6 ${(dashboard?.netProfit || 0) >= 0
                        ? 'from-emerald-50 to-emerald-100 border border-emerald-200'
                        : 'from-red-50 to-red-100 border border-red-200'
                    }`}>
                    <p className={`text-sm font-medium ${(dashboard?.netProfit || 0) >= 0 ? 'text-emerald-600' : 'text-red-600'}`}>
                        📈 {t('treasury.netProfitMTD', 'Net Profit (MTD)')}
                    </p>
                    <p className={`text-4xl font-bold mt-2 ${(dashboard?.netProfit || 0) >= 0 ? 'text-emerald-900' : 'text-red-900'}`}>
                        {formatCurrency(dashboard?.netProfit || 0)}
                    </p>
                </div>
            </div>

            {/* Account Cards Grid */}
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                {ACCOUNT_CARDS.map(card => {
                    const balance = balances[card.code] || 0

                    return (
                        <div
                            key={card.code}
                            onClick={() => navigate(`/treasury/account/${card.code}`)}
                            className={`
                                bg-gradient-to-br ${card.gradient} border ${card.border} 
                                rounded-xl p-6 cursor-pointer 
                                hover:shadow-lg transition-all duration-200 hover:scale-102
                            `}
                        >
                            <div className="flex justify-between items-start">
                                <div>
                                    <p className={`text-sm font-medium ${card.text}`}>
                                        {card.icon} {t(`treasury.${card.code}`, card.name)}
                                    </p>
                                    <p className={`text-3xl font-bold ${card.textBold} mt-2`}>
                                        {formatCurrency(balance)}
                                    </p>
                                    <p className="text-xs text-gray-500 mt-1">
                                        {t('treasury.accountCode', 'Code')}: {card.code}
                                    </p>
                                </div>
                                <div className="text-4xl opacity-30">
                                    {card.icon}
                                </div>
                            </div>

                            {card.isClearing && balance > 0 && (
                                <div className="mt-4 pt-4 border-t border-white/50">
                                    <span className="inline-flex items-center px-2 py-1 rounded-full text-xs font-medium bg-amber-200 text-amber-800">
                                        ⏳ {t('treasury.pendingSettlement', 'Pending Settlement')}
                                    </span>
                                </div>
                            )}

                            <div className="mt-4 text-right">
                                <span className={`text-sm ${card.text} hover:underline`}>
                                    {t('treasury.viewMovements', 'View Movements')} →
                                </span>
                            </div>
                        </div>
                    )
                })}
            </div>

            {/* Quick Actions */}
            <div className="bg-white rounded-lg shadow p-6">
                <h3 className="text-lg font-semibold text-gray-900 mb-4">
                    🔗 {t('treasury.quickLinks', 'Quick Links')}
                </h3>
                <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                    <button
                        onClick={() => navigate('/treasury/trial-balance')}
                        className="p-4 bg-gray-50 hover:bg-gray-100 rounded-lg transition-colors text-left"
                    >
                        <span className="text-2xl">⚖️</span>
                        <p className="font-medium text-gray-900 mt-2">
                            {t('treasury.trialBalance', 'Trial Balance')}
                        </p>
                    </button>
                    <button
                        onClick={() => navigate('/treasury/profit-loss')}
                        className="p-4 bg-gray-50 hover:bg-gray-100 rounded-lg transition-colors text-left"
                    >
                        <span className="text-2xl">📈</span>
                        <p className="font-medium text-gray-900 mt-2">
                            {t('treasury.profitLoss', 'Profit & Loss')}
                        </p>
                    </button>
                    <button
                        onClick={() => navigate('/treasury/balance-sheet')}
                        className="p-4 bg-gray-50 hover:bg-gray-100 rounded-lg transition-colors text-left"
                    >
                        <span className="text-2xl">📋</span>
                        <p className="font-medium text-gray-900 mt-2">
                            {t('treasury.balanceSheet', 'Balance Sheet')}
                        </p>
                    </button>
                    <button
                        onClick={() => navigate('/treasury/general-ledger')}
                        className="p-4 bg-gray-50 hover:bg-gray-100 rounded-lg transition-colors text-left"
                    >
                        <span className="text-2xl">📖</span>
                        <p className="font-medium text-gray-900 mt-2">
                            {t('treasury.generalLedger', 'General Ledger')}
                        </p>
                    </button>
                </div>
            </div>

            {/* Trust Footer */}
            <div className="text-center py-4 text-sm text-gray-500 border-t">
                <p>
                    📊 {t('treasury.ledgerSourced', 'All data sourced from immutable ledger')} •
                    🔒 {t('treasury.singleSource', 'Single source of truth')} •
                    ⏱️ {t('treasury.lastUpdated', 'Last updated')}: {new Date().toLocaleTimeString()}
                </p>
            </div>
        </div>
    )
}

/**
 * Treasury Account Viewer
 * 
 * PHASE FTM: Ledger-Driven Treasury UI
 * 
 * Generic account viewer that displays movements from the Ledger.
 * Routes:
 *   /treasury/account/1010 → Cash Account
 *   /treasury/account/1030 → Bank Account
 *   /treasury/account/1110 → Visa Clearing
 *   /treasury/account/1120 → Stripe Clearing
 */

import { useState, useEffect, useCallback } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../../contexts/SettingsContext'
import { useToast } from '../../components/Toast'
import ReportTruthBadge from '../../components/ReportTruthBadge'
import * as treasuryApi from '../../services/treasuryApi'
import { useRTL } from '../../hooks/useRTL'
import { formatDate } from '../../utils/dateFormat'

export default function TreasuryAccountViewer() {
    const { accountCode } = useParams()
    const { t } = useTranslation()
    const navigate = useNavigate()
    const { formatCurrency } = useSettings()
    const { dir, locale } = useRTL()
    const { showError } = useToast()

    // State
    const [loading, setLoading] = useState(true)
    const [refreshing, setRefreshing] = useState(false)
    const [balance, setBalance] = useState(0)
    const [movements, setMovements] = useState([])

    // Date filters
    const [startDate, setStartDate] = useState(() => {
        const date = new Date()
        date.setMonth(date.getMonth() - 1)
        return date.toISOString().split('T')[0]
    })
    const [endDate, setEndDate] = useState(() => new Date().toISOString().split('T')[0])

    // Get account display name
    const accountName = treasuryApi.ACCOUNT_NAMES[accountCode] || `Account ${accountCode}`

    // Get account icon
    const getAccountIcon = (code) => {
        switch (code) {
            case treasuryApi.ACCOUNT_CODES.CASHBOX_MAIN: return '💵'
            case treasuryApi.ACCOUNT_CODES.BANK_ACCOUNT: return '🏦'
            case treasuryApi.ACCOUNT_CODES.VISA_CLEARING: return '💳'
            case treasuryApi.ACCOUNT_CODES.STRIPE_CLEARING: return '⚡'
            case treasuryApi.ACCOUNT_CODES.COD_CLEARING: return '📦'
            case treasuryApi.ACCOUNT_CODES.WALLET_CLEARING: return '📱'
            default: return '📊'
        }
    }

    // Load data
    const loadData = useCallback(async (silent = false) => {
        try {
            if (!silent) setLoading(true)
            else setRefreshing(true)

            const from = startDate ? new Date(startDate) : null
            const to = endDate ? new Date(endDate) : null

            // Parallel API calls
            const [balanceData, movementsData] = await Promise.all([
                treasuryApi.getTreasuryBalance(accountCode),
                treasuryApi.getTreasuryMovements(accountCode, from, to)
            ])

            setBalance(balanceData.balance || 0)
            setMovements(movementsData || [])
        } catch (error) {
            console.error('Failed to load account data:', error)
            if (!silent) {
                showError(t('treasury.loadError', 'Failed to load account data'))
            }
        } finally {
            setLoading(false)
            setRefreshing(false)
        }
    }, [accountCode, startDate, endDate, showError, t])

    useEffect(() => {
        loadData()
    }, [loadData])

    // Calculate totals from movements
    const totals = movements.reduce((acc, m) => ({
        debits: acc.debits + (m.debit || 0),
        credits: acc.credits + (m.credit || 0)
    }), { debits: 0, credits: 0 })

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
                <div className="flex items-center gap-4">
                    <button
                        onClick={() => navigate('/treasury')}
                        className="text-gray-500 hover:text-gray-700"
                    >
                        ← {t('common.back')}
                    </button>
                    <div>
                        <h2 className="text-3xl font-bold text-gray-900 flex items-center gap-3">
                            <span>{getAccountIcon(accountCode)}</span>
                            {accountName}
                        </h2>
                        <p className="text-gray-600 mt-1">
                            {t('treasury.accountCode', 'Account Code')}: {accountCode}
                        </p>
                    </div>
                </div>
                <div className="flex items-center gap-3">
                    <button
                        onClick={() => loadData(true)}
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

            {/* Balance Card */}
            <div className="bg-gradient-to-br from-emerald-50 to-emerald-100 border border-emerald-200 rounded-xl p-6">
                <div className="flex justify-between items-center">
                    <div>
                        <p className="text-sm font-medium text-emerald-600">
                            {t('treasury.currentBalance', 'Current Balance')}
                        </p>
                        <p className="text-4xl font-bold text-emerald-900 mt-2">
                            {formatCurrency(balance)}
                        </p>
                    </div>
                    <div className="text-6xl opacity-50">
                        {getAccountIcon(accountCode)}
                    </div>
                </div>
            </div>

            {/* Summary Cards */}
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                <div className="bg-white rounded-lg shadow p-6">
                    <p className="text-sm font-medium text-gray-500 mb-1">
                        {t('treasury.totalDebits', 'Total Debits (In)')}
                    </p>
                    <p className="text-2xl font-bold text-green-600">
                        {formatCurrency(totals.debits)}
                    </p>
                </div>
                <div className="bg-white rounded-lg shadow p-6">
                    <p className="text-sm font-medium text-gray-500 mb-1">
                        {t('treasury.totalCredits', 'Total Credits (Out)')}
                    </p>
                    <p className="text-2xl font-bold text-red-600">
                        {formatCurrency(totals.credits)}
                    </p>
                </div>
                <div className="bg-white rounded-lg shadow p-6">
                    <p className="text-sm font-medium text-gray-500 mb-1">
                        {t('treasury.netChange', 'Net Change')}
                    </p>
                    <p className={`text-2xl font-bold ${totals.debits - totals.credits >= 0 ? 'text-green-600' : 'text-red-600'}`}>
                        {formatCurrency(totals.debits - totals.credits)}
                    </p>
                </div>
            </div>

            {/* Filters */}
            <div className="bg-white rounded-lg shadow p-4">
                <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('common.startDate', 'Start Date')}
                        </label>
                        <input
                            type="date"
                            value={startDate}
                            onChange={(e) => setStartDate(e.target.value)}
                            className="input w-full"
                        />
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('common.endDate', 'End Date')}
                        </label>
                        <input
                            type="date"
                            value={endDate}
                            onChange={(e) => setEndDate(e.target.value)}
                            className="input w-full"
                        />
                    </div>
                    <div className="flex items-end">
                        <button
                            onClick={() => loadData()}
                            className="btn-primary w-full"
                        >
                            {t('common.applyFilter', 'Apply Filter')}
                        </button>
                    </div>
                    <div className="flex items-end">
                        <button
                            onClick={() => {
                                const date = new Date()
                                setStartDate(new Date(date.getFullYear(), date.getMonth(), 1).toISOString().split('T')[0])
                                setEndDate(date.toISOString().split('T')[0])
                            }}
                            className="btn-secondary w-full"
                        >
                            {t('treasury.thisMonth', 'This Month')}
                        </button>
                    </div>
                </div>
            </div>

            {/* Movements Table */}
            <div className="bg-white rounded-lg shadow overflow-hidden">
                <div className="overflow-x-auto">
                    <table className="w-full">
                        <thead className="bg-gray-50 border-b border-gray-200">
                            <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('treasury.date', 'Date')}
                                </th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('treasury.journal', 'Journal #')}
                                </th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('treasury.description', 'Description')}
                                </th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('treasury.source', 'Source')}
                                </th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('treasury.debit', 'Debit')}
                                </th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('treasury.credit', 'Credit')}
                                </th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('treasury.balance', 'Balance')}
                                </th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {movements.length === 0 ? (
                                <tr>
                                    <td colSpan="7" className="px-6 py-12 text-center">
                                        <div className="text-gray-400">
                                            <span className="text-4xl">📭</span>
                                            <p className="mt-2">{t('treasury.noMovements', 'No movements in this period')}</p>
                                        </div>
                                    </td>
                                </tr>
                            ) : (
                                movements.map((m) => (
                                    <tr key={m.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            {formatDate(m.date, locale)}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm">
                                            <span className="text-primary-600 font-medium">
                                                {m.journalNumber}
                                            </span>
                                        </td>
                                        <td className="px-6 py-4 text-sm text-gray-900">
                                            {m.description}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-gray-100 text-gray-800">
                                                {m.sourceType}
                                            </span>
                                            {m.sourceReference && (
                                                <span className="ml-2 text-gray-400">
                                                    {m.sourceReference}
                                                </span>
                                            )}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-right">
                                            {m.debit > 0 && (
                                                <span className="font-medium text-green-600">
                                                    {formatCurrency(m.debit)}
                                                </span>
                                            )}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-right">
                                            {m.credit > 0 && (
                                                <span className="font-medium text-red-600">
                                                    {formatCurrency(m.credit)}
                                                </span>
                                            )}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-right font-bold text-gray-900">
                                            {formatCurrency(m.balanceAfter)}
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>
            </div>

            {/* Trust Footer */}
            <div className="text-center py-4 text-sm text-gray-500 border-t">
                <p>
                    📊 {t('treasury.ledgerSourced', 'All data sourced from immutable ledger')} •
                    {movements.length} {t('treasury.entries', 'entries')}
                </p>
            </div>
        </div>
    )
}

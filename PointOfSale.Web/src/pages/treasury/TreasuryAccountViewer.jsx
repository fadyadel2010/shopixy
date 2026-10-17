/**
 * Treasury Account Viewer
 *
 * Catalog-backed identity. The route code is resolved against
 * GET /api/treasury/accounts; the server row owns the localized name and the
 * accountType role. Absent mappings fail closed before any balance or
 * movement request: no invented names, no code-owned icons.
 */

import { useState, useEffect, useCallback } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../../contexts/SettingsContext'
import { useToast } from '../../components/Toast'
import ReportTruthBadge from '../../components/ReportTruthBadge'
import * as treasuryApi from '../../services/treasuryApi'
import {
    findTreasuryAccountByCode,
    getLocalizedTreasuryAccountName,
    isAvailableCashAccount,
    isClearingAccount,
} from '../../services/treasuryAccountCatalog'
import { isRequestCanceled } from '../../services/api'
import { useRTL } from '../../hooks/useRTL'
import { formatDate, formatDateInputValue } from '../../utils/dateFormat'

const ICON_FOR_TYPE = {
    Cashbox: '💵',
    Bank: '🏦',
    Clearing: '⏳',
}

const ROLE_KEY_FOR_TYPE = {
    Cashbox: 'treasury.cashboxRole',
    Bank: 'treasury.bankRole',
    Clearing: 'treasury.clearingRole',
}

const ROLE_LABEL_KEY_FOR_TYPE = {
    Cashbox: 'treasury.cashbox',
    Bank: 'treasury.bank',
    Clearing: 'treasury.clearingAccount',
}

export default function TreasuryAccountViewer() {
    const { accountCode } = useParams()
    const { t, i18n } = useTranslation()
    const navigate = useNavigate()
    const { formatCurrency } = useSettings()
    const { dir, locale } = useRTL()
    const { showError } = useToast()

    const [loading, setLoading] = useState(true)
    const [refreshing, setRefreshing] = useState(false)
    const [catalogAccount, setCatalogAccount] = useState(null)
    const [catalogResolved, setCatalogResolved] = useState(false)
    const [catalogError, setCatalogError] = useState('')
    const [balance, setBalance] = useState(0)
    const [movements, setMovements] = useState([])

    const [startDate, setStartDate] = useState(() => {
        const date = new Date()
        date.setMonth(date.getMonth() - 1)
        return formatDateInputValue(date)
    })
    const [endDate, setEndDate] = useState(() => formatDateInputValue(new Date()))

    const displayName = catalogAccount
        ? getLocalizedTreasuryAccountName(catalogAccount, i18n?.language)
        : ''
    const accountIcon = (catalogAccount && ICON_FOR_TYPE[catalogAccount.accountType]) || '📊'
    const roleKey = catalogAccount ? ROLE_KEY_FOR_TYPE[catalogAccount.accountType] : null
    const roleLabelKey = catalogAccount ? ROLE_LABEL_KEY_FOR_TYPE[catalogAccount.accountType] : null

    const loadData = useCallback(async (silent = false, signal) => {
        try {
            if (!silent) setLoading(true)
            else setRefreshing(true)
            setCatalogError('')

            let catalog
            try {
                catalog = await treasuryApi.getTreasuryAccounts({ signal })
            } catch (error) {
                if (isRequestCanceled(error)) return
                setCatalogAccount(null)
                setCatalogResolved(false)
                setBalance(0)
                setMovements([])
                setCatalogError(t('treasury.catalogLoadFailed', 'The Treasury catalog could not be loaded. Balances are hidden until you retry.'))
                if (!silent) showError(t('treasury.loadError', 'Failed to load account data'))
                return
            }
            if (signal?.aborted) return
            const candidate = findTreasuryAccountByCode(catalog, accountCode)
            const match = candidate && (isAvailableCashAccount(candidate) || isClearingAccount(candidate))
                ? candidate
                : null
            setCatalogAccount(match)
            setCatalogResolved(true)

            // Fail closed: an absent mapping never triggers balance/movement calls.
            if (!match) {
                setBalance(0)
                setMovements([])
                return
            }

            const from = startDate ? new Date(startDate) : null
            const to = endDate ? new Date(endDate) : null

            const [balanceData, movementsData] = await Promise.all([
                treasuryApi.getTreasuryBalance(accountCode, { signal }),
                treasuryApi.getTreasuryMovements(accountCode, from, to, { signal }),
            ])
            if (signal?.aborted) return

            setBalance(balanceData.balance || 0)
            setMovements(movementsData || [])
        } catch (error) {
            if (signal && isRequestCanceled(error)) return
            if (!signal && error?.code === 'ERR_CANCELED') return
            console.error('Failed to load account data:', error)
            if (!silent) showError(t('treasury.loadError', 'Failed to load account data'))
        } finally {
            if (!signal?.aborted) {
                setLoading(false)
                setRefreshing(false)
            }
        }
    }, [accountCode, startDate, endDate, showError, t])

    useEffect(() => {
        const controller = new AbortController()
        void loadData(false, controller.signal)
        return () => controller.abort()
    }, [loadData])

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

    if (catalogError && !catalogResolved) {
        return (
            <div dir={dir} className="space-y-6">
                <ReportTruthBadge type="financial" />
                <div className="bg-red-50 border border-red-200 rounded-lg p-6 text-center" role="alert">
                    <p className="font-semibold text-red-800">{catalogError}</p>
                    <div className="mt-4 flex justify-center gap-3">
                        <button type="button" onClick={() => loadData()} className="btn-secondary text-sm">
                            {t('treasury.retry', 'Retry')}
                        </button>
                        <button type="button" onClick={() => navigate('/treasury/hub')} className="btn-secondary text-sm">
                            {t('treasury.backToHub', 'Back to Treasury Hub')}
                        </button>
                    </div>
                </div>
            </div>
        )
    }

    if (catalogResolved && !catalogAccount) {
        return (
            <div dir={dir} className="space-y-6">
                <ReportTruthBadge type="financial" />
                <div className="bg-white rounded-lg shadow p-10 text-center" role="alert">
                    <p className="text-4xl" aria-hidden="true">🔒</p>
                    <h2 className="mt-3 text-2xl font-bold text-gray-900">
                        {t('treasury.accountUnavailable', 'This account is not part of the Treasury catalog.')}
                    </h2>
                    <p className="mt-2 text-sm text-gray-600 max-w-2xl mx-auto">
                        {t('treasury.accountUnavailableHint', 'It may be unmapped, inactive, deleted, foreign, or a report-only ledger account. Report visibility grants no Treasury authority.')}
                    </p>
                    <p className="mt-2 text-xs text-gray-500 font-mono">
                        {t('treasury.accountCode', 'Account Code')}: {accountCode}
                    </p>
                    <div className="mt-6 flex justify-center gap-3">
                        <button type="button" onClick={() => navigate('/treasury/hub')} className="btn-primary text-sm">
                            {t('treasury.backToHub', 'Back to Treasury Hub')}
                        </button>
                        <button type="button" onClick={() => navigate('/treasury/general-ledger')} className="btn-secondary text-sm">
                            {t('treasury.viewGeneralLedger', 'View General Ledger')}
                        </button>
                    </div>
                </div>
            </div>
        )
    }

    return (
        <div dir={dir} className="space-y-6">
            <ReportTruthBadge type="financial" />

            <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:gap-4">
                    <button
                        onClick={() => navigate('/treasury/hub')}
                        className="self-start text-gray-500 hover:text-gray-700"
                    >
                        <span aria-hidden="true" className="inline-block rtl:rotate-180">←</span> {t('common.back')}
                    </button>
                    <div>
                        <h2 className="text-3xl font-bold text-gray-900 flex items-center gap-3">
                            <span aria-hidden="true">{accountIcon}</span>
                            {displayName}
                        </h2>
                        <p className="text-gray-600 mt-1">
                            {t('treasury.accountCode', 'Account Code')}: {accountCode}
                        </p>
                        <p className="mt-2">
                            <span className={`inline-flex items-center px-2.5 py-1 rounded-full text-xs font-semibold ${isClearingAccount(catalogAccount) ? 'bg-amber-100 text-amber-800' : 'bg-emerald-100 text-emerald-800'}`}>
                                {t('treasury.role', 'Treasury role')}: {roleLabelKey ? t(roleLabelKey, catalogAccount.accountType) : ''}
                            </span>
                        </p>
                         {roleKey && (
                             <p className="text-sm text-gray-600 mt-1">{t(roleKey)}</p>
                         )}
                         {isClearingAccount(catalogAccount) && (
                             <p className="text-sm text-gray-600 mt-2" role="note">
                                 {t('treasury.clearingSettlementUnavailable', 'COD and gateway clearing settlement is currently unavailable because authenticated carrier remittance or provider payout evidence is not integrated. Clearing balances cannot be moved through Treasury Transfers.')}
                             </p>
                         )}
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
                    <div className="text-6xl opacity-50" aria-hidden="true">
                        {accountIcon}
                    </div>
                </div>
            </div>

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

            <div className="bg-white rounded-lg shadow p-4">
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
                    <div>
                        <label htmlFor="treasury-account-viewer-from" className="block text-sm font-medium text-gray-700 mb-1">
                            {t('common.startDate', 'Start Date')}
                        </label>
                        <input
                            id="treasury-account-viewer-from"
                            type="date"
                            value={startDate}
                            onChange={(e) => setStartDate(e.target.value)}
                            className="input w-full"
                        />
                    </div>
                    <div>
                        <label htmlFor="treasury-account-viewer-to" className="block text-sm font-medium text-gray-700 mb-1">
                            {t('common.endDate', 'End Date')}
                        </label>
                        <input
                            id="treasury-account-viewer-to"
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
                                setStartDate(formatDateInputValue(new Date(date.getFullYear(), date.getMonth(), 1)))
                                setEndDate(formatDateInputValue(date))
                            }}
                            className="btn-secondary w-full"
                        >
                            {t('treasury.thisMonth', 'This Month')}
                        </button>
                    </div>
                </div>
            </div>

            <div className="bg-white rounded-lg shadow overflow-hidden">
                <div className="overflow-x-auto">
                    <table className="w-full">
                        <thead className="bg-gray-50 border-b border-gray-200">
                            <tr>
                                <th scope="col" className="px-6 py-3 text-start text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('treasury.date', 'Date')}
                                </th>
                                <th scope="col" className="px-6 py-3 text-start text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('treasury.journal', 'Journal #')}
                                </th>
                                <th scope="col" className="px-6 py-3 text-start text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('treasury.description', 'Description')}
                                </th>
                                <th scope="col" className="px-6 py-3 text-start text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('treasury.source', 'Source')}
                                </th>
                                <th scope="col" className="px-6 py-3 text-end text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('treasury.debit', 'Debit')}
                                </th>
                                <th scope="col" className="px-6 py-3 text-end text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('treasury.credit', 'Credit')}
                                </th>
                                <th scope="col" className="px-6 py-3 text-end text-xs font-medium text-gray-500 uppercase tracking-wider">
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
                                            <bdi className="text-primary-600 font-medium">
                                                {m.journalNumber}
                                            </bdi>
                                        </td>
                                        <td className="px-6 py-4 text-sm text-gray-900">
                                            {m.description}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-gray-100 text-gray-800">
                                                {t(`treasury.sourceTypes.${m.sourceType}`, t('treasury.sourceTypes.Other', 'Other source'))}
                                            </span>
                                            {m.sourceReference && (
                                                <bdi className="ms-2 text-gray-400">
                                                    {m.sourceReference}
                                                </bdi>
                                            )}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-end">
                                            {m.debit > 0 && (
                                                <span className="font-medium text-green-600">
                                                    {formatCurrency(m.debit)}
                                                </span>
                                            )}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-end">
                                            {m.credit > 0 && (
                                                <span className="font-medium text-red-600">
                                                    {formatCurrency(m.credit)}
                                                </span>
                                            )}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-end font-bold text-gray-900">
                                            {formatCurrency(m.balanceAfter)}
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>
            </div>

            <div className="text-center py-4 text-sm text-gray-500 border-t">
                <p>
                    📊 {t('treasury.ledgerSourced', 'All data sourced from immutable ledger')} •
                    {movements.length} {t('treasury.entries', 'entries')}
                </p>
            </div>
        </div>
    )
}

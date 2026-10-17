/**
 * General Ledger Report Page
 * 
 * PHASE 5: Treasury & Finance Control Center
 * 
 * Shows transaction history for a specific account.
 */

import { useState, useEffect, useMemo } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../../contexts/SettingsContext'
import { useToast } from '../../components/Toast'
import ReportTruthBadge from '../../components/ReportTruthBadge'
import * as finApi from '../../services/financialReportsApi'
import { isRequestCanceled } from '../../services/api'
import { useRTL } from '../../hooks/useRTL'
import { formatDate, formatDateInputValue, formatDateTime, parseDateInput } from '../../utils/dateFormat'

export default function TreasuryGeneralLedger() {
    const { t } = useTranslation()
    const navigate = useNavigate()
    const [searchParams, setSearchParams] = useSearchParams()
    const { showSuccess } = useToast()
    const { formatCurrency } = useSettings()
    const { dir, locale } = useRTL()

    const [loading, setLoading] = useState(false)
    const [accountsLoading, setAccountsLoading] = useState(true)
    const [accountsError, setAccountsError] = useState(false)
    const [ledgerError, setLedgerError] = useState(false)
    const [retry, setRetry] = useState(0)
    const [accounts, setAccounts] = useState([])
    const selectedAccountId = searchParams.get('accountId') || ''
    const [report, setReport] = useState(null)
    const [defaultDateRange] = useState(() => finApi.getDefaultDateRange())
    const fromParam = searchParams.get('from')
    const toParam = searchParams.get('to')
    const dateRange = useMemo(() => ({
        from: parseDateInput(fromParam) || defaultDateRange.from,
        to: parseDateInput(toParam) || defaultDateRange.to,
    }), [fromParam, toParam, defaultDateRange])

    // Account discovery must not depend on activity in the selected period.
    useEffect(() => {
        const controller = new AbortController()
        setAccountsLoading(true)
        setAccountsError(false)
        finApi.getAccountSummaries({ signal: controller.signal })
            .then(data => {
                if (!controller.signal.aborted) setAccounts(data || [])
            })
            .catch(error => {
                if (controller.signal.aborted || isRequestCanceled(error)) return
                console.error('Failed to load ledger accounts:', error)
                setAccounts([])
                setAccountsError(true)
            })
            .finally(() => {
                if (!controller.signal.aborted) setAccountsLoading(false)
            })
        return () => controller.abort()
    }, [retry])

    // Cancel obsolete reads; never display a previous account's report on failure.
    useEffect(() => {
        const controller = new AbortController()
        setReport(null)
        setLedgerError(false)
        setLoading(Boolean(selectedAccountId))
        if (!selectedAccountId) return () => controller.abort()
        finApi.getGeneralLedger(selectedAccountId, dateRange.from, dateRange.to, { signal: controller.signal })
            .then(data => {
                if (!controller.signal.aborted) setReport(data)
            })
            .catch(error => {
                if (controller.signal.aborted || isRequestCanceled(error)) return
                console.error('Failed to load general ledger:', error)
                setLedgerError(true)
            })
            .finally(() => {
                if (!controller.signal.aborted) setLoading(false)
            })
        return () => controller.abort()
    }, [selectedAccountId, dateRange, retry])

    const updateFilters = (changes) => {
        const next = new URLSearchParams(searchParams)
        const values = {
            accountId: selectedAccountId,
            from: formatDateInputValue(dateRange.from),
            to: formatDateInputValue(dateRange.to),
            ...changes,
        }
        Object.entries(values).forEach(([key, value]) => value ? next.set(key, value) : next.delete(key))
        setSearchParams(next, { replace: true })
    }

    const exportToExcel = () => {
        if (!report) return

        import('xlsx').then(XLSX => {
            const data = report.entries?.map(e => ({
                [t('treasury.date', 'Date')]: formatDate(e.journalDate, locale),
                [t('treasury.journalNumber', 'Journal #')]: e.journalNumber,
                [t('treasury.source', 'Source')]: t(`treasury.sourceTypes.${e.sourceType}`, t('treasury.sourceTypes.Other', 'Other source')),
                [t('treasury.description', 'Description')]: e.description,
                [t('treasury.debit', 'Debit')]: e.debitAmount || '',
                [t('treasury.credit', 'Credit')]: e.creditAmount || '',
                [t('treasury.balance', 'Balance')]: e.balanceAfter
            })) || []

            // Add opening and closing
            data.unshift({
                [t('treasury.date', 'Date')]: '',
                [t('treasury.journalNumber', 'Journal #')]: '',
                [t('treasury.source', 'Source')]: '',
                [t('treasury.description', 'Description')]: t('treasury.openingBalance', 'Opening Balance'),
                [t('treasury.debit', 'Debit')]: '',
                [t('treasury.credit', 'Credit')]: '',
                [t('treasury.balance', 'Balance')]: report.openingBalance
            })
            data.push({
                [t('treasury.date', 'Date')]: '',
                [t('treasury.journalNumber', 'Journal #')]: '',
                [t('treasury.source', 'Source')]: '',
                [t('treasury.description', 'Description')]: t('treasury.closingBalance', 'Closing Balance'),
                [t('treasury.debit', 'Debit')]: report.totalDebits,
                [t('treasury.credit', 'Credit')]: report.totalCredits,
                [t('treasury.balance', 'Balance')]: report.closingBalance
            })

            const ws = XLSX.utils.json_to_sheet(data)
            const wb = XLSX.utils.book_new()
            XLSX.utils.book_append_sheet(wb, ws, t('treasury.generalLedger', 'General Ledger').slice(0, 31))
            XLSX.writeFile(wb, `GeneralLedger_${report.accountCode}_${formatDateInputValue(dateRange.from)}.xlsx`)
            showSuccess(t('common.exportSuccess', 'Exported successfully'))
        })
    }

    const exportToPdf = () => {
        if (!report) return

        import('jspdf').then(({ default: jsPDF }) => {
            import('jspdf-autotable').then(() => {
                const doc = new jsPDF()

                doc.setFontSize(16)
                doc.text(`${t('treasury.generalLedger', 'General Ledger')}: ${report.accountCode} - ${report.accountName}`, 14, 20)
                doc.setFontSize(10)
                doc.text(`${t('common.period', 'Period')}: ${formatDate(dateRange.from, locale)} - ${formatDate(dateRange.to, locale)}`, 14, 28)
                doc.text(`${t('treasury.openingBalance', 'Opening Balance')}: ${formatCurrency(report.openingBalance)}`, 14, 35)

                doc.autoTable({
                    startY: 42,
                    head: [[t('treasury.date', 'Date'), t('treasury.journalNumber', 'Journal'), t('treasury.source', 'Source'), t('treasury.description', 'Description'), t('treasury.debit', 'Debit'), t('treasury.credit', 'Credit'), t('treasury.balance', 'Balance')]],
                    body: report.entries?.map(e => [
                        formatDate(e.journalDate, locale),
                        e.journalNumber || '',
                         t(`treasury.sourceTypes.${e.sourceType}`, t('treasury.sourceTypes.Other', 'Other source')),
                        e.description?.substring(0, 30) || '',
                        e.debitAmount ? formatCurrency(e.debitAmount) : '',
                        e.creditAmount ? formatCurrency(e.creditAmount) : '',
                        formatCurrency(e.balanceAfter)
                    ]) || [],
                    foot: [['', '', '', t('common.totals', 'Totals'), formatCurrency(report.totalDebits), formatCurrency(report.totalCredits), formatCurrency(report.closingBalance)]],
                    theme: 'striped',
                    styles: { fontSize: 8 },
                    headStyles: { fillColor: [59, 130, 246] }
                })

                doc.save(`GeneralLedger_${report.accountCode}.pdf`)
                showSuccess(t('common.exportSuccess', 'Exported successfully'))
            })
        })
    }

    return (
        <div dir={dir} className="space-y-6">
            {/* Phase C4: UI Truth Label */}
            <ReportTruthBadge type="financial" />

            {/* Complete history across all ledger accounts. */}
            <div className="p-4 rounded-lg bg-slate-50 border border-slate-200" role="note">
                <p className="text-sm text-slate-700">
                    {t('treasury.generalLedgerPerimeter', 'Choose from all ledger accounts, including clearing and non-cash accounts. Entries are shown for the selected account and date range; earlier activity is included in the opening balance. Report inclusion grants no transfer or settlement authority.')}
                </p>
            </div>

            {/* Header */}
            <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:gap-4">
                    <button onClick={() => navigate('/treasury')} className="self-start text-gray-500 hover:text-gray-700">
                        <span aria-hidden="true" className="inline-block rtl:rotate-180">←</span> {t('common.back')}
                    </button>
                    <div>
                        <h2 className="text-2xl font-bold text-gray-900">
                            <span aria-hidden="true">📖 </span>{t('treasury.generalLedger', 'General Ledger')}
                        </h2>
                        <p className="text-gray-600">
                            {t('treasury.transactionHistory', 'Account transaction history')}
                        </p>
                    </div>
                </div>

                <div className="flex w-full flex-wrap items-center gap-3 md:w-auto">
                    {/* Account Selector */}
                    <label htmlFor="treasury-general-ledger-account" className="sr-only">{t('treasury.account', 'Account')}</label>
                    <select
                        id="treasury-general-ledger-account"
                        value={selectedAccountId}
                        onChange={(e) => updateFilters({ accountId: e.target.value })}
                        disabled={accountsLoading || accountsError}
                        className="input min-w-0 basis-full sm:min-w-[250px] sm:basis-auto"
                    >
                        <option value="">{t('treasury.selectAccount', 'Select an account...')}</option>
                        {accounts.map(acc => (
                            <option key={acc.accountId} value={acc.accountId}>
                                {acc.accountCode} - {acc.accountName}
                            </option>
                        ))}
                    </select>

                    <input
                        aria-label={t('common.startDate', 'Start Date')}
                        type="date"
                        value={formatDateInputValue(dateRange.from)}
                        onChange={(e) => {
                            const date = parseDateInput(e.target.value)
                            if (date) updateFilters({ from: formatDateInputValue(date) })
                        }}
                        className="input min-w-0 flex-1 md:flex-none"
                    />
                    <span className="text-gray-500">-</span>
                    <input
                        aria-label={t('common.endDate', 'End Date')}
                        type="date"
                        value={formatDateInputValue(dateRange.to)}
                        onChange={(e) => {
                            const date = parseDateInput(e.target.value)
                            if (date) updateFilters({ to: formatDateInputValue(date) })
                        }}
                        className="input min-w-0 flex-1 md:flex-none"
                    />

                    {report && (
                        <>
                            <button onClick={exportToExcel} className="btn-secondary flex-1 text-sm sm:flex-none">📊 Excel</button>
                            <button onClick={exportToPdf} className="btn-secondary flex-1 text-sm sm:flex-none">📄 PDF</button>
                        </>
                    )}
                </div>
            </div>

            {(accountsError || ledgerError) && (
                <div className="card bg-red-50 text-red-800" role="alert">
                    <p>{t('treasury.loadError', 'Failed to load report')}</p>
                    <button type="button" onClick={() => setRetry(value => value + 1)} className="btn-secondary mt-3">
                        {t('treasury.retry', 'Retry')}
                    </button>
                </div>
            )}

            {accountsLoading && !selectedAccountId ? (
                <p role="status">{t('common.loading')}</p>
            ) : !selectedAccountId && !accountsError ? (
                <div className="card flex flex-col items-center justify-center py-16">
                    <span className="text-6xl mb-4" aria-hidden="true">📖</span>
                    <h3 className="text-xl font-semibold text-gray-700 mb-2">
                        {t('treasury.selectAccountPrompt', 'Select an Account')}
                    </h3>
                    <p className="text-gray-500">
                        {t('treasury.selectAccountDesc', 'Choose an account from the dropdown to view its transaction ledger')}
                    </p>
                </div>
            ) : loading ? (
                <div className="flex items-center justify-center h-96">
                    <div className="text-center">
                        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary-600 mx-auto" aria-hidden="true"></div>
                        <p className="mt-4 text-gray-600" role="status">{t('common.loading')}</p>
                    </div>
                </div>
            ) : report && !ledgerError ? (
                <>
                    {/* Account Header */}
                    <div className="card bg-gradient-to-r from-blue-50 to-blue-100 border-blue-200">
                        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                            <div>
                                <p className="text-sm text-blue-600 font-medium">{report?.accountCode}</p>
                                <h3 className="text-xl font-bold text-blue-900">{report?.accountName}</h3>
                            </div>
                            <div className="text-end">
                                <p className="text-sm text-gray-500">{t('treasury.closingBalance', 'Closing Balance')}</p>
                                <p className="text-2xl font-bold text-blue-900">
                                    {formatCurrency(report?.closingBalance || 0)}
                                </p>
                            </div>
                        </div>
                    </div>

                    {/* Balance Summary */}
                    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
                        <div className="card bg-gray-50">
                            <p className="text-sm text-gray-600">{t('treasury.openingBalance', 'Opening Balance')}</p>
                            <p className="text-xl font-bold text-gray-900">{formatCurrency(report?.openingBalance || 0)}</p>
                        </div>
                        <div className="card bg-green-50">
                            <p className="text-sm text-green-600">{t('treasury.totalDebits', 'Total Debits')}</p>
                            <p className="text-xl font-bold text-green-900">{formatCurrency(report?.totalDebits || 0)}</p>
                        </div>
                        <div className="card bg-red-50">
                            <p className="text-sm text-red-600">{t('treasury.totalCredits', 'Total Credits')}</p>
                            <p className="text-xl font-bold text-red-900">{formatCurrency(report?.totalCredits || 0)}</p>
                        </div>
                        <div className="card bg-blue-50">
                            <p className="text-sm text-blue-600">{t('treasury.closingBalance', 'Closing Balance')}</p>
                            <p className="text-xl font-bold text-blue-900">{formatCurrency(report?.closingBalance || 0)}</p>
                        </div>
                    </div>

                    {/* Transactions Table */}
                    <div className="card overflow-hidden">
                        <div className="px-4 py-3 bg-gray-50 border-b flex justify-between items-center">
                            <h3 className="font-semibold text-gray-900">
                                {t('treasury.transactions', 'Transactions')}
                            </h3>
                            <span className="text-sm text-gray-500">
                                {report?.entries?.length || 0} {t('treasury.entries', 'entries')}
                            </span>
                        </div>

                        {report?.entries?.length > 0 ? (
                            <div className="overflow-x-auto">
                                <table className="min-w-full divide-y divide-gray-200">
                                    <thead className="bg-gray-50">
                                        <tr>
                                            <th scope="col" className="px-4 py-3 text-start text-xs font-medium text-gray-500 uppercase">
                                                {t('treasury.date', 'Date')}
                                            </th>
                                            <th scope="col" className="px-4 py-3 text-start text-xs font-medium text-gray-500 uppercase">
                                                {t('treasury.journalNumber', 'Journal #')}
                                            </th>
                                            <th scope="col" className="px-4 py-3 text-start text-xs font-medium text-gray-500 uppercase">
                                                {t('treasury.source', 'Source')}
                                            </th>
                                            <th scope="col" className="px-4 py-3 text-start text-xs font-medium text-gray-500 uppercase">
                                                {t('treasury.description', 'Description')}
                                            </th>
                                            <th scope="col" className="px-4 py-3 text-end text-xs font-medium text-gray-500 uppercase">
                                                {t('treasury.debit', 'Debit')}
                                            </th>
                                            <th scope="col" className="px-4 py-3 text-end text-xs font-medium text-gray-500 uppercase">
                                                {t('treasury.credit', 'Credit')}
                                            </th>
                                            <th scope="col" className="px-4 py-3 text-end text-xs font-medium text-gray-500 uppercase">
                                                {t('treasury.balance', 'Balance')}
                                            </th>
                                        </tr>
                                    </thead>
                                    <tbody className="bg-white divide-y divide-gray-200">
                                        {/* Opening Balance Row */}
                                        <tr className="bg-gray-50">
                                            <td colSpan={6} className="px-4 py-2 text-sm font-medium text-gray-700">
                                                {t('treasury.openingBalance', 'Opening Balance')}
                                            </td>
                                            <td className="px-4 py-2 text-sm text-end font-semibold text-gray-900">
                                                {formatCurrency(report.openingBalance)}
                                            </td>
                                        </tr>

                                        {/* Transaction Rows */}
                                        {report.entries.map((entry, idx) => (
                                            <tr key={idx} className="hover:bg-gray-50">
                                                <td className="px-4 py-3 text-sm text-gray-900">
                                                    {formatDate(entry.journalDate, locale)}
                                                </td>
                                                <td className="px-4 py-3 text-sm font-mono text-gray-600">
                                                    <bdi>{entry.journalNumber}</bdi>
                                                </td>
                                                <td className="px-4 py-3 text-sm">
                                                    <span className="px-2 py-0.5 text-xs rounded-full bg-gray-100 text-gray-700">
                                                        {t(`treasury.sourceTypes.${entry.sourceType}`, t('treasury.sourceTypes.Other', 'Other source'))}
                                                    </span>
                                                </td>
                                                <td className="px-4 py-3 text-sm text-gray-900 max-w-xs truncate">
                                                    {entry.description}
                                                </td>
                                                <td className="px-4 py-3 text-sm text-end font-medium text-green-600">
                                                    {entry.debitAmount > 0 ? formatCurrency(entry.debitAmount) : '-'}
                                                </td>
                                                <td className="px-4 py-3 text-sm text-end font-medium text-red-600">
                                                    {entry.creditAmount > 0 ? formatCurrency(entry.creditAmount) : '-'}
                                                </td>
                                                <td className="px-4 py-3 text-sm text-end font-semibold text-gray-900">
                                                    {formatCurrency(entry.balanceAfter)}
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                    <tfoot className="bg-blue-50">
                                        <tr className="font-bold">
                                            <td colSpan={4} className="px-4 py-3 text-end text-gray-900">
                                                {t('treasury.closingBalance', 'Closing Balance')}
                                            </td>
                                            <td className="px-4 py-3 text-end text-green-700">
                                                {formatCurrency(report.totalDebits)}
                                            </td>
                                            <td className="px-4 py-3 text-end text-red-700">
                                                {formatCurrency(report.totalCredits)}
                                            </td>
                                            <td className="px-4 py-3 text-end text-blue-900">
                                                {formatCurrency(report.closingBalance)}
                                            </td>
                                        </tr>
                                    </tfoot>
                                </table>
                            </div>
                        ) : (
                            <div className="flex flex-col items-center justify-center py-16">
                                <span className="text-4xl mb-4" aria-hidden="true">📭</span>
                                <p className="text-gray-500">{t('treasury.noTransactions', 'No transactions in this period')}</p>
                            </div>
                        )}
                    </div>
                </>
            ) : null}

            {/* Footer */}
            <div className="text-center py-4 text-sm text-gray-500">
                {t('treasury.ledgerSourced', 'Sourced from immutable ledger')} •
                {t('treasury.generated', 'Generated')}: {formatDateTime(new Date(), locale)}
            </div>
        </div>
    )
}

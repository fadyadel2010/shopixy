/**
 * General Ledger Report Page
 * 
 * PHASE 5: Treasury & Finance Control Center
 * 
 * Shows transaction history for a specific account.
 */

import { useState, useEffect } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../../contexts/SettingsContext'
import { useToast } from '../../components/Toast'
import ReportTruthBadge from '../../components/ReportTruthBadge'
import * as finApi from '../../services/financialReportsApi'
import { useRTL } from '../../hooks/useRTL'
import { formatDate, formatDateTime } from '../../utils/dateFormat'

export default function TreasuryGeneralLedger() {
    const { t } = useTranslation()
    const navigate = useNavigate()
    const [searchParams] = useSearchParams()
    const { showError, showSuccess } = useToast()
    const { formatCurrency } = useSettings()
    const { dir, locale } = useRTL()

    const [loading, setLoading] = useState(true)
    const [accounts, setAccounts] = useState([])
    const [selectedAccountId, setSelectedAccountId] = useState(searchParams.get('accountId') || '')
    const [report, setReport] = useState(null)
    const [dateRange, setDateRange] = useState(() => finApi.getDefaultDateRange())

    // Load accounts list on mount
    useEffect(() => {
        loadAccounts()
    }, [])

    // Load ledger when account or date changes
    useEffect(() => {
        if (selectedAccountId) {
            loadLedger()
        }
    }, [selectedAccountId, dateRange])

    const loadAccounts = async () => {
        try {
            // Get trial balance to extract account list
            const tb = await finApi.getTrialBalance(dateRange.from, dateRange.to)
            setAccounts(tb.lines || [])

            // If we have an account ID from URL, select it
            if (searchParams.get('accountId') && !selectedAccountId) {
                setSelectedAccountId(searchParams.get('accountId'))
            }
        } catch (error) {
            console.error('Failed to load accounts:', error)
        }
    }

    const loadLedger = async () => {
        try {
            setLoading(true)
            const data = await finApi.getGeneralLedger(selectedAccountId, dateRange.from, dateRange.to)
            setReport(data)
        } catch (error) {
            console.error('Failed to load general ledger:', error)
            showError(t('treasury.loadError', 'Failed to load report'))
        } finally {
            setLoading(false)
        }
    }

    const exportToExcel = () => {
        if (!report) return

        import('xlsx').then(XLSX => {
            const data = report.entries?.map(e => ({
                Date: formatDate(e.journalDate, locale),
                'Journal #': e.journalNumber,
                Source: e.sourceType,
                Description: e.description,
                Debit: e.debitAmount || '',
                Credit: e.creditAmount || '',
                Balance: e.balanceAfter
            })) || []

            // Add opening and closing
            data.unshift({
                Date: '',
                'Journal #': '',
                Source: '',
                Description: 'Opening Balance',
                Debit: '',
                Credit: '',
                Balance: report.openingBalance
            })
            data.push({
                Date: '',
                'Journal #': '',
                Source: '',
                Description: 'Closing Balance',
                Debit: report.totalDebits,
                Credit: report.totalCredits,
                Balance: report.closingBalance
            })

            const ws = XLSX.utils.json_to_sheet(data)
            const wb = XLSX.utils.book_new()
            XLSX.utils.book_append_sheet(wb, ws, 'General Ledger')
            XLSX.writeFile(wb, `GeneralLedger_${report.accountCode}_${dateRange.from.toISOString().split('T')[0]}.xlsx`)
            showSuccess(t('common.exportSuccess', 'Exported successfully'))
        })
    }

    const exportToPdf = () => {
        if (!report) return

        import('jspdf').then(({ default: jsPDF }) => {
            import('jspdf-autotable').then(() => {
                const doc = new jsPDF()

                doc.setFontSize(16)
                doc.text(`General Ledger: ${report.accountCode} - ${report.accountName}`, 14, 20)
                doc.setFontSize(10)
                doc.text(`Period: ${formatDate(dateRange.from, locale)} - ${formatDate(dateRange.to, locale)}`, 14, 28)
                doc.text(`Opening Balance: ${formatCurrency(report.openingBalance)}`, 14, 35)

                doc.autoTable({
                    startY: 42,
                    head: [['Date', 'Journal', 'Source', 'Description', 'Debit', 'Credit', 'Balance']],
                    body: report.entries?.map(e => [
                        formatDate(e.journalDate, locale),
                        e.journalNumber || '',
                        e.sourceType || '',
                        e.description?.substring(0, 30) || '',
                        e.debitAmount ? formatCurrency(e.debitAmount) : '',
                        e.creditAmount ? formatCurrency(e.creditAmount) : '',
                        formatCurrency(e.balanceAfter)
                    ]) || [],
                    foot: [['', '', '', 'TOTALS', formatCurrency(report.totalDebits), formatCurrency(report.totalCredits), formatCurrency(report.closingBalance)]],
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

            {/* Header */}
            <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
                <div className="flex items-center gap-4">
                    <button onClick={() => navigate('/treasury')} className="text-gray-500 hover:text-gray-700">
                        ← {t('common.back')}
                    </button>
                    <div>
                        <h2 className="text-2xl font-bold text-gray-900">
                            📖 {t('treasury.generalLedger', 'General Ledger')}
                        </h2>
                        <p className="text-gray-600">
                            {t('treasury.transactionHistory', 'Account transaction history')}
                        </p>
                    </div>
                </div>

                <div className="flex items-center gap-3 flex-wrap">
                    {/* Account Selector */}
                    <select
                        value={selectedAccountId}
                        onChange={(e) => setSelectedAccountId(e.target.value)}
                        className="input min-w-[250px]"
                    >
                        <option value="">{t('treasury.selectAccount', 'Select an account...')}</option>
                        {accounts.map(acc => (
                            <option key={acc.accountId} value={acc.accountId}>
                                {acc.accountCode} - {acc.accountName}
                            </option>
                        ))}
                    </select>

                    <input
                        type="date"
                        value={dateRange.from.toISOString().split('T')[0]}
                        onChange={(e) => setDateRange(prev => ({ ...prev, from: new Date(e.target.value) }))}
                        className="input"
                    />
                    <span className="text-gray-500">-</span>
                    <input
                        type="date"
                        value={dateRange.to.toISOString().split('T')[0]}
                        onChange={(e) => setDateRange(prev => ({ ...prev, to: new Date(e.target.value) }))}
                        className="input"
                    />

                    {report && (
                        <>
                            <button onClick={exportToExcel} className="btn-secondary text-sm">📊 Excel</button>
                            <button onClick={exportToPdf} className="btn-secondary text-sm">📄 PDF</button>
                        </>
                    )}
                </div>
            </div>

            {!selectedAccountId ? (
                <div className="card flex flex-col items-center justify-center py-16">
                    <span className="text-6xl mb-4">📖</span>
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
                        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary-600 mx-auto"></div>
                        <p className="mt-4 text-gray-600">{t('common.loading')}</p>
                    </div>
                </div>
            ) : (
                <>
                    {/* Account Header */}
                    <div className="card bg-gradient-to-r from-blue-50 to-blue-100 border-blue-200">
                        <div className="flex justify-between items-start">
                            <div>
                                <p className="text-sm text-blue-600 font-medium">{report?.accountCode}</p>
                                <h3 className="text-xl font-bold text-blue-900">{report?.accountName}</h3>
                            </div>
                            <div className="text-right">
                                <p className="text-sm text-gray-500">{t('treasury.currentBalance', 'Current Balance')}</p>
                                <p className="text-2xl font-bold text-blue-900">
                                    {formatCurrency(report?.closingBalance || 0)}
                                </p>
                            </div>
                        </div>
                    </div>

                    {/* Balance Summary */}
                    <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
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
                                            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                                                {t('treasury.date', 'Date')}
                                            </th>
                                            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                                                {t('treasury.journalNumber', 'Journal #')}
                                            </th>
                                            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                                                {t('treasury.source', 'Source')}
                                            </th>
                                            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                                                {t('treasury.description', 'Description')}
                                            </th>
                                            <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase">
                                                {t('treasury.debit', 'Debit')}
                                            </th>
                                            <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase">
                                                {t('treasury.credit', 'Credit')}
                                            </th>
                                            <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase">
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
                                            <td className="px-4 py-2 text-sm text-right font-semibold text-gray-900">
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
                                                    {entry.journalNumber}
                                                </td>
                                                <td className="px-4 py-3 text-sm">
                                                    <span className="px-2 py-0.5 text-xs rounded-full bg-gray-100 text-gray-700">
                                                        {entry.sourceType}
                                                    </span>
                                                </td>
                                                <td className="px-4 py-3 text-sm text-gray-900 max-w-xs truncate">
                                                    {entry.description}
                                                </td>
                                                <td className="px-4 py-3 text-sm text-right font-medium text-green-600">
                                                    {entry.debitAmount > 0 ? formatCurrency(entry.debitAmount) : '-'}
                                                </td>
                                                <td className="px-4 py-3 text-sm text-right font-medium text-red-600">
                                                    {entry.creditAmount > 0 ? formatCurrency(entry.creditAmount) : '-'}
                                                </td>
                                                <td className="px-4 py-3 text-sm text-right font-semibold text-gray-900">
                                                    {formatCurrency(entry.balanceAfter)}
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                    <tfoot className="bg-blue-50">
                                        <tr className="font-bold">
                                            <td colSpan={4} className="px-4 py-3 text-right text-gray-900">
                                                {t('treasury.closingBalance', 'Closing Balance')}
                                            </td>
                                            <td className="px-4 py-3 text-right text-green-700">
                                                {formatCurrency(report.totalDebits)}
                                            </td>
                                            <td className="px-4 py-3 text-right text-red-700">
                                                {formatCurrency(report.totalCredits)}
                                            </td>
                                            <td className="px-4 py-3 text-right text-blue-900">
                                                {formatCurrency(report.closingBalance)}
                                            </td>
                                        </tr>
                                    </tfoot>
                                </table>
                            </div>
                        ) : (
                            <div className="flex flex-col items-center justify-center py-16">
                                <span className="text-4xl mb-4">📭</span>
                                <p className="text-gray-500">{t('treasury.noTransactions', 'No transactions in this period')}</p>
                            </div>
                        )}
                    </div>
                </>
            )}

            {/* Footer */}
            <div className="text-center py-4 text-sm text-gray-500">
                {t('treasury.ledgerSourced', 'Sourced from immutable ledger')} •
                {t('treasury.generated', 'Generated')}: {formatDateTime(new Date(), locale)}
            </div>
        </div>
    )
}

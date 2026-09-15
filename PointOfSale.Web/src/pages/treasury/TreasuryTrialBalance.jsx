/**
 * Trial Balance Report Page
 * 
 * PHASE 5: Treasury & Finance Control Center
 * 
 * Verifies: Total Debits = Total Credits
 */

import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../../contexts/SettingsContext'
import { useToast } from '../../components/Toast'
import ReportTruthBadge from '../../components/ReportTruthBadge'
import * as finApi from '../../services/financialReportsApi'
import { useRTL } from '../../hooks/useRTL'
import { formatDate, formatDateInputValue, formatDateTime, parseDateInput } from '../../utils/dateFormat'

export default function TreasuryTrialBalance() {
    const { t } = useTranslation()
    const navigate = useNavigate()
    const { showError, showSuccess } = useToast()
    const { formatCurrency } = useSettings()
    const { dir, locale } = useRTL()

    const [loading, setLoading] = useState(true)
    const [report, setReport] = useState(null)
    const [dateRange, setDateRange] = useState(() => finApi.getDefaultDateRange())

    useEffect(() => {
        loadReport()
    }, [dateRange])

    const loadReport = async () => {
        try {
            setLoading(true)
            const data = await finApi.getTrialBalance(dateRange.from, dateRange.to)
            setReport(data)
        } catch (error) {
            console.error('Failed to load trial balance:', error)
            showError(t('treasury.loadError', 'Failed to load report'))
        } finally {
            setLoading(false)
        }
    }

    const exportToExcel = () => {
        // Using xlsx library
        import('xlsx').then(XLSX => {
            const ws = XLSX.utils.json_to_sheet(report.lines.map(l => ({
                [t('treasury.accountCode', 'Account Code')]: l.accountCode,
                [t('treasury.accountName', 'Account Name')]: l.accountName,
                [t('treasury.type', 'Account Type')]: t(`treasury.accountTypes.${l.accountType}`, l.accountType),
                [t('treasury.debits', 'Debits')]: l.totalDebits,
                [t('treasury.credits', 'Credits')]: l.totalCredits,
                [t('treasury.balance', 'Balance')]: l.balance
            })))

            // Add totals row
            XLSX.utils.sheet_add_json(ws, [{
                [t('treasury.accountCode', 'Account Code')]: '',
                [t('treasury.accountName', 'Account Name')]: t('common.totals', 'Totals'),
                [t('treasury.type', 'Account Type')]: '',
                [t('treasury.debits', 'Debits')]: report.totalDebits,
                [t('treasury.credits', 'Credits')]: report.totalCredits,
                [t('treasury.balance', 'Balance')]: ''
            }], { skipHeader: true, origin: -1 })

            const wb = XLSX.utils.book_new()
            XLSX.utils.book_append_sheet(wb, ws, t('treasury.trialBalance', 'Trial Balance').slice(0, 31))
            XLSX.writeFile(wb, `TrialBalance_${formatDateInputValue(dateRange.from)}_${formatDateInputValue(dateRange.to)}.xlsx`)
            showSuccess(t('common.exportSuccess', 'Exported successfully'))
        })
    }

    const exportToPdf = () => {
        import('jspdf').then(({ default: jsPDF }) => {
            import('jspdf-autotable').then(() => {
                const doc = new jsPDF()

                doc.setFontSize(18)
                doc.text(t('treasury.trialBalance', 'Trial Balance'), 14, 22)
                doc.setFontSize(11)
                doc.text(`${t('common.period', 'Period')}: ${formatDate(dateRange.from, locale)} - ${formatDate(dateRange.to, locale)}`, 14, 32)

                doc.autoTable({
                    startY: 40,
                    head: [[t('treasury.accountCode', 'Code'), t('treasury.account', 'Account'), t('treasury.type', 'Type'), t('treasury.debits', 'Debits'), t('treasury.credits', 'Credits'), t('treasury.balance', 'Balance')]],
                    body: report.lines.map(l => [
                        l.accountCode,
                        l.accountName,
                        l.accountType,
                        formatCurrency(l.totalDebits),
                        formatCurrency(l.totalCredits),
                        formatCurrency(l.balance)
                    ]),
                    foot: [['', t('common.totals', 'Totals'), '', formatCurrency(report.totalDebits), formatCurrency(report.totalCredits), '']],
                    theme: 'striped',
                    styles: { fontSize: 9 }
                })

                // Add balance status
                const finalY = doc.lastAutoTable.finalY + 10
                doc.setFontSize(12)
                if (report.isBalanced) {
                    doc.setTextColor(0, 128, 0)
                    doc.text(`✓ ${t('treasury.balanced', 'Balanced')} - ${t('treasury.debitsCreditMatch', 'Debits equal Credits')}`, 14, finalY)
                } else {
                    doc.setTextColor(255, 0, 0)
                    doc.text(`✗ ${t('treasury.unbalanced', 'Unbalanced')} - ${t('treasury.variance', 'Variance')}: ${formatCurrency(report.variance)}`, 14, finalY)
                }

                doc.save(`TrialBalance_${formatDateInputValue(dateRange.from)}.pdf`)
                showSuccess(t('common.exportSuccess', 'Exported successfully'))
            })
        })
    }

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

    return (
        <div dir={dir} className="space-y-6">
            {/* Phase C4: UI Truth Label */}
            <ReportTruthBadge type="financial" />

            {/* Complete ledger, not the available-cash catalog. */}
            <div className="p-4 rounded-lg bg-slate-50 border border-slate-200" role="note">
                <p className="text-sm text-slate-700">
                    {t('treasury.trialBalancePerimeter', 'This is the complete ledger, not the available-cash catalog. Inclusion here grants no transfer or settlement authority.')}
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
                            <span aria-hidden="true">⚖️ </span>{t('treasury.trialBalance', 'Trial Balance')}
                        </h2>
                        <p className="text-gray-600">
                            {formatDate(dateRange.from, locale)} - {formatDate(dateRange.to, locale)}
                        </p>
                    </div>
                </div>

                <div className="flex w-full flex-wrap items-center gap-3 md:w-auto">
                    {/* Date Range */}
                    <input
                        aria-label={t('common.startDate', 'Start Date')}
                        type="date"
                        value={formatDateInputValue(dateRange.from)}
                        onChange={(e) => {
                            const date = parseDateInput(e.target.value)
                            if (date) setDateRange(prev => ({ ...prev, from: date }))
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
                            if (date) setDateRange(prev => ({ ...prev, to: date }))
                        }}
                        className="input min-w-0 flex-1 md:flex-none"
                    />

                    {/* Export Buttons */}
                    <button onClick={exportToExcel} disabled={!report?.lines?.length} className="btn-secondary flex-1 text-sm sm:flex-none">
                        📊 Excel
                    </button>
                    <button onClick={exportToPdf} disabled={!report?.lines?.length} className="btn-secondary flex-1 text-sm sm:flex-none">
                        📄 PDF
                    </button>
                </div>
            </div>

            {/* Balance Status Banner */}
            <div role="status" className={`p-4 rounded-lg flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between ${report?.isBalanced
                ? 'bg-green-50 border border-green-200'
                : 'bg-red-50 border border-red-200'
                }`}>
                <div className="flex items-center gap-3">
                    <span className="text-3xl" aria-hidden="true">{report?.isBalanced ? '✅' : '❌'}</span>
                    <div>
                        <p className={`font-semibold ${report?.isBalanced ? 'text-green-800' : 'text-red-800'}`}>
                            {report?.isBalanced
                                ? t('treasury.balanced', 'BALANCED')
                                : t('treasury.unbalanced', 'UNBALANCED')}
                        </p>
                        <p className={`text-sm ${report?.isBalanced ? 'text-green-600' : 'text-red-600'}`}>
                            {report?.isBalanced
                                ? t('treasury.debitsCreditMatch', 'Total debits equal total credits')
                                : `${t('treasury.variance', 'Variance')}: ${formatCurrency(Math.abs(report?.variance || 0))}`}
                        </p>
                    </div>
                </div>
                <div className="text-end">
                    <p className="text-sm text-gray-600">{t('treasury.totalDebits', 'Total Debits')}</p>
                    <p className="font-bold text-lg">{formatCurrency(report?.totalDebits || 0)}</p>
                </div>
                <div className="text-end">
                    <p className="text-sm text-gray-600">{t('treasury.totalCredits', 'Total Credits')}</p>
                    <p className="font-bold text-lg">{formatCurrency(report?.totalCredits || 0)}</p>
                </div>
            </div>

            {/* Trial Balance Table */}
            <div className="card overflow-hidden">
                <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-gray-200">
                        <thead className="bg-gray-50">
                            <tr>
                                <th scope="col" className="px-4 py-3 text-start text-xs font-medium text-gray-500 uppercase">
                                    {t('treasury.accountCode', 'Code')}
                                </th>
                                <th scope="col" className="px-4 py-3 text-start text-xs font-medium text-gray-500 uppercase">
                                    {t('treasury.accountName', 'Account')}
                                </th>
                                <th scope="col" className="px-4 py-3 text-start text-xs font-medium text-gray-500 uppercase">
                                    {t('treasury.type', 'Type')}
                                </th>
                                <th scope="col" className="px-4 py-3 text-end text-xs font-medium text-gray-500 uppercase">
                                    {t('treasury.debits', 'Debits')}
                                </th>
                                <th scope="col" className="px-4 py-3 text-end text-xs font-medium text-gray-500 uppercase">
                                    {t('treasury.credits', 'Credits')}
                                </th>
                                <th scope="col" className="px-4 py-3 text-end text-xs font-medium text-gray-500 uppercase">
                                    {t('treasury.balance', 'Balance')}
                                </th>
                                <th scope="col" className="px-4 py-3 text-center text-xs font-medium text-gray-500 uppercase">
                                    {t('common.actions', 'Actions')}
                                </th>
                            </tr>
                        </thead>
                        <tbody className="bg-white divide-y divide-gray-200">
                            {report?.lines?.map((line) => (
                                <tr key={line.accountId} className="hover:bg-gray-50">
                                    <td className="px-4 py-3 text-sm font-mono text-gray-900">
                                        <bdi>{line.accountCode}</bdi>
                                    </td>
                                    <td className="px-4 py-3 text-sm text-gray-900">
                                        {line.accountName}
                                    </td>
                                    <td className="px-4 py-3 text-sm">
                                        <span className={`px-2 py-1 text-xs rounded-full ${line.accountType === 'Asset' ? 'bg-blue-100 text-blue-800' :
                                            line.accountType === 'Liability' ? 'bg-orange-100 text-orange-800' :
                                                line.accountType === 'Equity' ? 'bg-green-100 text-green-800' :
                                                    line.accountType === 'Revenue' ? 'bg-emerald-100 text-emerald-800' :
                                                        line.accountType === 'CostOfGoodsSold' ? 'bg-amber-100 text-amber-800' :
                                                            'bg-red-100 text-red-800'
                                            }`}>
                                            {t(`treasury.accountTypes.${line.accountType}`, line.accountType)}
                                        </span>
                                    </td>
                                    <td className="px-4 py-3 text-sm text-end font-medium text-gray-900">
                                        {line.totalDebits > 0 ? formatCurrency(line.totalDebits) : '-'}
                                    </td>
                                    <td className="px-4 py-3 text-sm text-end font-medium text-gray-900">
                                        {line.totalCredits > 0 ? formatCurrency(line.totalCredits) : '-'}
                                    </td>
                                    <td className={`px-4 py-3 text-sm text-end font-semibold ${line.balance >= 0 ? 'text-gray-900' : 'text-red-600'
                                        }`}>
                                        {formatCurrency(line.balance)}
                                    </td>
                                    <td className="px-4 py-3 text-center">
                                        <button
                                            onClick={() => navigate(`/treasury/general-ledger?accountId=${line.accountId}`)}
                                            className="text-primary-600 hover:text-primary-800 text-sm"
                                        >
                                            📖 {t('treasury.viewLedger', 'Ledger')}
                                        </button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                        <tfoot className="bg-gray-100">
                            <tr className="font-bold">
                                <td colSpan={3} className="px-4 py-3 text-end text-gray-900">
                                    {t('common.totals', 'TOTALS')}
                                </td>
                                <td className="px-4 py-3 text-end text-gray-900">
                                    {formatCurrency(report?.totalDebits || 0)}
                                </td>
                                <td className="px-4 py-3 text-end text-gray-900">
                                    {formatCurrency(report?.totalCredits || 0)}
                                </td>
                                <td colSpan={2}></td>
                            </tr>
                        </tfoot>
                    </table>
                </div>
            </div>

            {/* Footer */}
            <div className="text-center py-4 text-sm text-gray-500">
                {t('treasury.ledgerSourced', 'Sourced from immutable ledger')} •
                {t('treasury.generated', 'Generated')}: {formatDateTime(new Date(), locale)}
            </div>
        </div>
    )
}

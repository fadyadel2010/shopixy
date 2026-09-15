/**
 * Balance Sheet Report Page
 * 
 * PHASE 5: Treasury & Finance Control Center
 * 
 * Verifies: Assets = Liabilities + Equity
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

export default function TreasuryBalanceSheet() {
    const { t } = useTranslation()
    const navigate = useNavigate()
    const { showError, showSuccess } = useToast()
    const { formatCurrency } = useSettings()
    const { dir, locale } = useRTL()

    const [loading, setLoading] = useState(true)
    const [report, setReport] = useState(null)
    const [asOfDate, setAsOfDate] = useState(new Date())

    useEffect(() => {
        loadReport()
    }, [asOfDate])

    const loadReport = async () => {
        try {
            setLoading(true)
            const data = await finApi.getBalanceSheet(asOfDate)
            setReport(data)
        } catch (error) {
            console.error('Failed to load balance sheet:', error)
            showError(t('treasury.loadError', 'Failed to load report'))
        } finally {
            setLoading(false)
        }
    }

    const exportToExcel = () => {
        import('xlsx').then(XLSX => {
            const data = []
            const sectionKey = t('common.section', 'Section')
            const accountKey = t('treasury.account', 'Account')
            const balanceKey = t('treasury.balance', 'Balance')

            data.push({ [sectionKey]: t('treasury.assets', 'Assets'), [accountKey]: '', [balanceKey]: '' })
            report.assetLines?.forEach(l => data.push({
                [sectionKey]: '',
                [accountKey]: `${l.accountCode} - ${l.accountName}`,
                [balanceKey]: l.balance
            }))
            data.push({ [sectionKey]: t('treasury.totalAssets', 'Total Assets'), [accountKey]: '', [balanceKey]: report.totalAssets })
            data.push({ [sectionKey]: '', [accountKey]: '', [balanceKey]: '' })

            data.push({ [sectionKey]: t('treasury.liabilities', 'Liabilities'), [accountKey]: '', [balanceKey]: '' })
            report.liabilityLines?.forEach(l => data.push({
                [sectionKey]: '',
                [accountKey]: `${l.accountCode} - ${l.accountName}`,
                [balanceKey]: l.balance
            }))
            data.push({ [sectionKey]: t('treasury.totalLiabilities', 'Total Liabilities'), [accountKey]: '', [balanceKey]: report.totalLiabilities })
            data.push({ [sectionKey]: '', [accountKey]: '', [balanceKey]: '' })

            data.push({ [sectionKey]: t('treasury.equity', 'Equity'), [accountKey]: '', [balanceKey]: '' })
            report.equityLines?.forEach(l => data.push({
                [sectionKey]: '',
                [accountKey]: `${l.accountCode} - ${l.accountName}`,
                [balanceKey]: l.balance
            }))
            data.push({ [sectionKey]: t('treasury.totalEquity', 'Total Equity'), [accountKey]: '', [balanceKey]: report.totalEquity })
            data.push({ [sectionKey]: '', [accountKey]: '', [balanceKey]: '' })

            data.push({ [sectionKey]: t('treasury.totalLiabilitiesEquity', 'Liabilities + Equity'), [accountKey]: '', [balanceKey]: report.totalLiabilitiesAndEquity })

            const ws = XLSX.utils.json_to_sheet(data)
            const wb = XLSX.utils.book_new()
            XLSX.utils.book_append_sheet(wb, ws, t('treasury.balanceSheet', 'Balance Sheet').slice(0, 31))
            XLSX.writeFile(wb, `BalanceSheet_${formatDateInputValue(asOfDate)}.xlsx`)
            showSuccess(t('common.exportSuccess', 'Exported successfully'))
        })
    }

    const exportToPdf = () => {
        import('jspdf').then(({ default: jsPDF }) => {
            import('jspdf-autotable').then(() => {
                const doc = new jsPDF()

                doc.setFontSize(18)
                doc.text(t('treasury.balanceSheet', 'Balance Sheet'), 14, 22)
                doc.setFontSize(11)
                doc.text(`${t('treasury.asOf', 'As of')}: ${formatDate(asOfDate, locale)}`, 14, 32)

                let y = 45

                // Assets
                doc.setFontSize(12)
                doc.setFont(undefined, 'bold')
                doc.text(t('treasury.assets', 'Assets'), 14, y)
                y += 8
                doc.setFont(undefined, 'normal')
                doc.setFontSize(10)
                report.assetLines?.forEach(l => {
                    doc.text(`  ${l.accountCode} - ${l.accountName}`, 14, y)
                    doc.text(formatCurrency(l.balance), 180, y, { align: 'right' })
                    y += 6
                })
                doc.setFont(undefined, 'bold')
                doc.setFontSize(11)
                doc.text(t('treasury.totalAssets', 'Total Assets'), 14, y)
                doc.text(formatCurrency(report.totalAssets), 180, y, { align: 'right' })
                y += 15

                // Liabilities
                doc.setFontSize(12)
                doc.text(t('treasury.liabilities', 'Liabilities'), 14, y)
                y += 8
                doc.setFont(undefined, 'normal')
                doc.setFontSize(10)
                report.liabilityLines?.forEach(l => {
                    doc.text(`  ${l.accountCode} - ${l.accountName}`, 14, y)
                    doc.text(formatCurrency(l.balance), 180, y, { align: 'right' })
                    y += 6
                })
                doc.setFont(undefined, 'bold')
                doc.setFontSize(11)
                doc.text(t('treasury.totalLiabilities', 'Total Liabilities'), 14, y)
                doc.text(formatCurrency(report.totalLiabilities), 180, y, { align: 'right' })
                y += 15

                // Equity
                doc.setFontSize(12)
                doc.text(t('treasury.equity', 'Equity'), 14, y)
                y += 8
                doc.setFont(undefined, 'normal')
                doc.setFontSize(10)
                report.equityLines?.forEach(l => {
                    doc.text(`  ${l.accountCode} - ${l.accountName}`, 14, y)
                    doc.text(formatCurrency(l.balance), 180, y, { align: 'right' })
                    y += 6
                })
                doc.setFont(undefined, 'bold')
                doc.setFontSize(11)
                doc.text(t('treasury.totalEquity', 'Total Equity'), 14, y)
                doc.text(formatCurrency(report.totalEquity), 180, y, { align: 'right' })
                y += 15

                // Equation
                doc.setFontSize(12)
                if (report.isBalanced) {
                    doc.setTextColor(0, 128, 0)
                    doc.text(`✓ ${t('treasury.balanced', 'Balanced')}: ${t('treasury.accountingEquation', 'Assets = Liabilities + Equity')}`, 14, y)
                } else {
                    doc.setTextColor(255, 0, 0)
                    doc.text(`✗ ${t('treasury.unbalanced', 'Unbalanced')}: ${t('treasury.variance', 'Variance')} ${formatCurrency(report.variance)}`, 14, y)
                }

                doc.save(`BalanceSheet_${formatDateInputValue(asOfDate)}.pdf`)
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

            {/* Asset perimeter: broader than available cash. */}
            <div className="p-4 rounded-lg bg-slate-50 border border-slate-200" role="note">
                <p className="text-sm text-slate-700">
                    {t('treasury.balanceSheetPerimeter', 'Assets include clearing, supplier-card funding, control, and other non-cash accounts. Only mapped Cashbox and Bank are available cash.')}
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
                            <span aria-hidden="true">📋 </span>{t('treasury.balanceSheet', 'Balance Sheet')}
                        </h2>
                        <p className="text-gray-600">
                            {t('treasury.asOf', 'As of')}: {formatDate(asOfDate, locale)}
                        </p>
                    </div>
                </div>

                <div className="flex w-full flex-wrap items-center gap-3 md:w-auto">
                    <label htmlFor="treasury-balance-sheet-date" className="text-sm text-gray-600">{t('treasury.asOf', 'As of')}:</label>
                    <input
                        id="treasury-balance-sheet-date"
                        type="date"
                        value={formatDateInputValue(asOfDate)}
                        onChange={(e) => {
                            const date = parseDateInput(e.target.value)
                            if (date) setAsOfDate(date)
                        }}
                        className="input min-w-0 flex-1 md:flex-none"
                    />
                    <button onClick={exportToExcel} disabled={!report} className="btn-secondary flex-1 text-sm sm:flex-none">📊 Excel</button>
                    <button onClick={exportToPdf} disabled={!report} className="btn-secondary flex-1 text-sm sm:flex-none">📄 PDF</button>
                </div>
            </div>

            {/* Balance Status */}
            <div role="status" className={`p-4 rounded-lg flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between ${report?.isBalanced
                ? 'bg-green-50 border border-green-200'
                : 'bg-red-50 border border-red-200'
                }`}>
                <div className="flex items-center gap-3">
                    <span className="text-3xl" aria-hidden="true">{report?.isBalanced ? '✅' : '❌'}</span>
                    <div>
                        <p className={`font-semibold ${report?.isBalanced ? 'text-green-800' : 'text-red-800'}`}>
                            {report?.isBalanced ? t('treasury.balanced', 'BALANCED') : t('treasury.unbalanced', 'UNBALANCED')}
                        </p>
                        <p className="text-sm text-gray-600">
                            {t('treasury.accountingEquation', 'Assets = Liabilities + Equity')}
                        </p>
                    </div>
                </div>
                <div className="flex w-full justify-around gap-8 sm:w-auto">
                    <div className="text-center">
                        <p className="text-xs text-gray-500">{t('treasury.currentRatio', 'Current Ratio')}</p>
                        <p className={`text-xl font-bold ${(report?.currentRatio || 0) >= 1.5 ? 'text-green-600' :
                            (report?.currentRatio || 0) >= 1 ? 'text-yellow-600' : 'text-red-600'
                            }`}>
                            {(report?.currentRatio || 0).toFixed(2)}
                        </p>
                    </div>
                    <div className="text-center">
                        <p className="text-xs text-gray-500">{t('treasury.debtToEquity', 'Debt/Equity')}</p>
                        <p className="text-xl font-bold text-gray-900">
                            {(report?.debtToEquityRatio || 0).toFixed(2)}
                        </p>
                    </div>
                </div>
            </div>

            {/* Accounting Equation Visualization */}
            <div className="p-4 bg-gray-50 rounded-lg">
                <div className="flex items-center justify-center text-lg gap-4 flex-wrap">
                    <div className="text-center">
                        <p className="text-xs text-gray-500 mb-1">{t('treasury.assets', 'Assets')}</p>
                        <p className="font-bold text-2xl text-blue-600">{formatCurrency(report?.totalAssets || 0)}</p>
                    </div>
                    <span className="text-2xl text-gray-400">=</span>
                    <div className="text-center">
                        <p className="text-xs text-gray-500 mb-1">{t('treasury.liabilities', 'Liabilities')}</p>
                        <p className="font-bold text-2xl text-orange-600">{formatCurrency(report?.totalLiabilities || 0)}</p>
                    </div>
                    <span className="text-2xl text-gray-400">+</span>
                    <div className="text-center">
                        <p className="text-xs text-gray-500 mb-1">{t('treasury.equity', 'Equity')}</p>
                        <p className="font-bold text-2xl text-green-600">{formatCurrency(report?.totalEquity || 0)}</p>
                    </div>
                </div>
            </div>

            {/* Two-Column Layout */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                {/* Assets Column */}
                <div className="card overflow-x-auto">
                    <h3 className="text-lg font-semibold text-blue-800 mb-4">
                        📈 {t('treasury.assets', 'ASSETS')}
                    </h3>
                    <table className="min-w-full">
                        <tbody className="divide-y divide-gray-100">
                            {report?.assetLines?.map((line) => (
                                <tr key={line.accountId} className="hover:bg-gray-50">
                                    <td className="py-2 text-sm font-mono text-gray-500"><bdi>{line.accountCode}</bdi></td>
                                    <td className="py-2 text-sm text-gray-900">
                                        <button type="button" className="text-start hover:underline" onClick={() => navigate(`/treasury/general-ledger?accountId=${line.accountId}`)}>{line.accountName}</button>
                                        {line.isCurrent && (
                                            <span className="ms-2 text-xs bg-blue-100 text-blue-600 px-1.5 py-0.5 rounded">
                                                {t('treasury.currentClassification', 'Current')}
                                            </span>
                                        )}
                                    </td>
                                    <td className="py-2 text-sm text-end font-semibold text-gray-900">
                                        {formatCurrency(line.balance)}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                        <tfoot className="bg-blue-50">
                            <tr>
                                <td colSpan={2} className="py-3 font-bold text-blue-900">
                                    {t('treasury.totalAssets', 'Total Assets')}
                                </td>
                                <td className="py-3 text-end font-bold text-blue-900">
                                    {formatCurrency(report?.totalAssets || 0)}
                                </td>
                            </tr>
                        </tfoot>
                    </table>
                </div>

                {/* Liabilities & Equity Column */}
                <div className="space-y-6">
                    {/* Liabilities */}
                    <div className="card overflow-x-auto">
                        <h3 className="text-lg font-semibold text-orange-800 mb-4">
                            💳 {t('treasury.liabilities', 'LIABILITIES')}
                        </h3>
                        <table className="min-w-full">
                            <tbody className="divide-y divide-gray-100">
                                {report?.liabilityLines?.map((line) => (
                                    <tr key={line.accountId} className="hover:bg-gray-50">
                                        <td className="py-2 text-sm font-mono text-gray-500"><bdi>{line.accountCode}</bdi></td>
                                        <td className="py-2 text-sm text-gray-900">
                                            <button type="button" className="text-start hover:underline" onClick={() => navigate(`/treasury/general-ledger?accountId=${line.accountId}`)}>{line.accountName}</button>
                                            {line.isCurrent && (
                                                <span className="ms-2 text-xs bg-orange-100 text-orange-600 px-1.5 py-0.5 rounded">
                                                    {t('treasury.currentClassification', 'Current')}
                                                </span>
                                            )}
                                        </td>
                                        <td className="py-2 text-sm text-end font-semibold text-gray-900">
                                            {formatCurrency(line.balance)}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                            <tfoot className="bg-orange-50">
                                <tr>
                                    <td colSpan={2} className="py-3 font-bold text-orange-900">
                                        {t('treasury.totalLiabilities', 'Total Liabilities')}
                                    </td>
                                    <td className="py-3 text-end font-bold text-orange-900">
                                        {formatCurrency(report?.totalLiabilities || 0)}
                                    </td>
                                </tr>
                            </tfoot>
                        </table>
                    </div>

                    {/* Equity */}
                    <div className="card overflow-x-auto">
                        <h3 className="text-lg font-semibold text-green-800 mb-4">
                            🏛️ {t('treasury.equity', 'EQUITY')}
                        </h3>
                        <table className="min-w-full">
                            <tbody className="divide-y divide-gray-100">
                                {report?.equityLines?.map((line) => (
                                    <tr key={line.accountId} className="hover:bg-gray-50">
                                        <td className="py-2 text-sm font-mono text-gray-500"><bdi>{line.accountCode}</bdi></td>
                                        <td className="py-2 text-sm text-gray-900"><button type="button" className="text-start hover:underline" onClick={() => navigate(`/treasury/general-ledger?accountId=${line.accountId}`)}>{line.accountName}</button></td>
                                        <td className="py-2 text-sm text-end font-semibold text-gray-900">
                                            {formatCurrency(line.balance)}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                            <tfoot className="bg-green-50">
                                <tr>
                                    <td colSpan={2} className="py-3 font-bold text-green-900">
                                        {t('treasury.totalEquity', 'Total Equity')}
                                    </td>
                                    <td className="py-3 text-end font-bold text-green-900">
                                        {formatCurrency(report?.totalEquity || 0)}
                                    </td>
                                </tr>
                            </tfoot>
                        </table>
                    </div>

                    {/* Total L+E */}
                    <div className="p-4 bg-gray-100 rounded-lg">
                        <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                            <span className="font-bold text-gray-800">
                                {t('treasury.totalLiabilitiesEquity', 'LIABILITIES + EQUITY')}
                            </span>
                            <span className="text-xl font-bold text-gray-900">
                                {formatCurrency(report?.totalLiabilitiesAndEquity || 0)}
                            </span>
                        </div>
                    </div>
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

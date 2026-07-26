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
import { formatDate, formatDateTime } from '../../utils/dateFormat'

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

            data.push({ Section: 'ASSETS', Account: '', Balance: '' })
            report.assetLines?.forEach(l => data.push({
                Section: '',
                Account: `${l.accountCode} - ${l.accountName}`,
                Balance: l.balance
            }))
            data.push({ Section: 'Total Assets', Account: '', Balance: report.totalAssets })
            data.push({ Section: '', Account: '', Balance: '' })

            data.push({ Section: 'LIABILITIES', Account: '', Balance: '' })
            report.liabilityLines?.forEach(l => data.push({
                Section: '',
                Account: `${l.accountCode} - ${l.accountName}`,
                Balance: l.balance
            }))
            data.push({ Section: 'Total Liabilities', Account: '', Balance: report.totalLiabilities })
            data.push({ Section: '', Account: '', Balance: '' })

            data.push({ Section: 'EQUITY', Account: '', Balance: '' })
            report.equityLines?.forEach(l => data.push({
                Section: '',
                Account: `${l.accountCode} - ${l.accountName}`,
                Balance: l.balance
            }))
            data.push({ Section: 'Total Equity', Account: '', Balance: report.totalEquity })
            data.push({ Section: '', Account: '', Balance: '' })

            data.push({ Section: 'LIABILITIES + EQUITY', Account: '', Balance: report.totalLiabilitiesAndEquity })

            const ws = XLSX.utils.json_to_sheet(data)
            const wb = XLSX.utils.book_new()
            XLSX.utils.book_append_sheet(wb, ws, 'Balance Sheet')
            XLSX.writeFile(wb, `BalanceSheet_${asOfDate.toISOString().split('T')[0]}.xlsx`)
            showSuccess(t('common.exportSuccess', 'Exported successfully'))
        })
    }

    const exportToPdf = () => {
        import('jspdf').then(({ default: jsPDF }) => {
            import('jspdf-autotable').then(() => {
                const doc = new jsPDF()

                doc.setFontSize(18)
                doc.text('Balance Sheet', 14, 22)
                doc.setFontSize(11)
                doc.text(`As of: ${formatDate(asOfDate, locale)}`, 14, 32)

                let y = 45

                // Assets
                doc.setFontSize(12)
                doc.setFont(undefined, 'bold')
                doc.text('ASSETS', 14, y)
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
                doc.text('Total Assets', 14, y)
                doc.text(formatCurrency(report.totalAssets), 180, y, { align: 'right' })
                y += 15

                // Liabilities
                doc.setFontSize(12)
                doc.text('LIABILITIES', 14, y)
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
                doc.text('Total Liabilities', 14, y)
                doc.text(formatCurrency(report.totalLiabilities), 180, y, { align: 'right' })
                y += 15

                // Equity
                doc.setFontSize(12)
                doc.text('EQUITY', 14, y)
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
                doc.text('Total Equity', 14, y)
                doc.text(formatCurrency(report.totalEquity), 180, y, { align: 'right' })
                y += 15

                // Equation
                doc.setFontSize(12)
                if (report.isBalanced) {
                    doc.setTextColor(0, 128, 0)
                    doc.text('✓ BALANCED: Assets = Liabilities + Equity', 14, y)
                } else {
                    doc.setTextColor(255, 0, 0)
                    doc.text(`✗ UNBALANCED: Variance ${formatCurrency(report.variance)}`, 14, y)
                }

                doc.save(`BalanceSheet_${asOfDate.toISOString().split('T')[0]}.pdf`)
                showSuccess(t('common.exportSuccess', 'Exported successfully'))
            })
        })
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
                <div className="flex items-center gap-4">
                    <button onClick={() => navigate('/treasury')} className="text-gray-500 hover:text-gray-700">
                        ← {t('common.back')}
                    </button>
                    <div>
                        <h2 className="text-2xl font-bold text-gray-900">
                            📋 {t('treasury.balanceSheet', 'Balance Sheet')}
                        </h2>
                        <p className="text-gray-600">
                            {t('treasury.asOf', 'As of')}: {formatDate(asOfDate, locale)}
                        </p>
                    </div>
                </div>

                <div className="flex items-center gap-3">
                    <label className="text-sm text-gray-600">{t('treasury.asOf', 'As of')}:</label>
                    <input
                        type="date"
                        value={asOfDate.toISOString().split('T')[0]}
                        onChange={(e) => setAsOfDate(new Date(e.target.value))}
                        className="input"
                    />
                    <button onClick={exportToExcel} className="btn-secondary text-sm">📊 Excel</button>
                    <button onClick={exportToPdf} className="btn-secondary text-sm">📄 PDF</button>
                </div>
            </div>

            {/* Balance Status */}
            <div className={`p-4 rounded-lg flex items-center justify-between ${report?.isBalanced
                ? 'bg-green-50 border border-green-200'
                : 'bg-red-50 border border-red-200'
                }`}>
                <div className="flex items-center gap-3">
                    <span className="text-3xl">{report?.isBalanced ? '✅' : '❌'}</span>
                    <div>
                        <p className={`font-semibold ${report?.isBalanced ? 'text-green-800' : 'text-red-800'}`}>
                            {report?.isBalanced ? t('treasury.balanced', 'BALANCED') : t('treasury.unbalanced', 'UNBALANCED')}
                        </p>
                        <p className="text-sm text-gray-600">
                            {t('treasury.accountingEquation', 'Assets = Liabilities + Equity')}
                        </p>
                    </div>
                </div>
                <div className="flex gap-8">
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
                <div className="card">
                    <h3 className="text-lg font-semibold text-blue-800 mb-4">
                        📈 {t('treasury.assets', 'ASSETS')}
                    </h3>
                    <table className="min-w-full">
                        <tbody className="divide-y divide-gray-100">
                            {report?.assetLines?.map((line, idx) => (
                                <tr key={idx} className="hover:bg-gray-50 cursor-pointer"
                                    onClick={() => navigate(`/treasury/general-ledger?accountId=${line.accountId}`)}>
                                    <td className="py-2 text-sm font-mono text-gray-500">{line.accountCode}</td>
                                    <td className="py-2 text-sm text-gray-900">
                                        {line.accountName}
                                        {line.isCurrent && (
                                            <span className="ml-2 text-xs bg-blue-100 text-blue-600 px-1.5 py-0.5 rounded">
                                                Current
                                            </span>
                                        )}
                                    </td>
                                    <td className="py-2 text-sm text-right font-semibold text-gray-900">
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
                                <td className="py-3 text-right font-bold text-blue-900">
                                    {formatCurrency(report?.totalAssets || 0)}
                                </td>
                            </tr>
                        </tfoot>
                    </table>
                </div>

                {/* Liabilities & Equity Column */}
                <div className="space-y-6">
                    {/* Liabilities */}
                    <div className="card">
                        <h3 className="text-lg font-semibold text-orange-800 mb-4">
                            💳 {t('treasury.liabilities', 'LIABILITIES')}
                        </h3>
                        <table className="min-w-full">
                            <tbody className="divide-y divide-gray-100">
                                {report?.liabilityLines?.map((line, idx) => (
                                    <tr key={idx} className="hover:bg-gray-50 cursor-pointer"
                                        onClick={() => navigate(`/treasury/general-ledger?accountId=${line.accountId}`)}>
                                        <td className="py-2 text-sm font-mono text-gray-500">{line.accountCode}</td>
                                        <td className="py-2 text-sm text-gray-900">
                                            {line.accountName}
                                            {line.isCurrent && (
                                                <span className="ml-2 text-xs bg-orange-100 text-orange-600 px-1.5 py-0.5 rounded">
                                                    Current
                                                </span>
                                            )}
                                        </td>
                                        <td className="py-2 text-sm text-right font-semibold text-gray-900">
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
                                    <td className="py-3 text-right font-bold text-orange-900">
                                        {formatCurrency(report?.totalLiabilities || 0)}
                                    </td>
                                </tr>
                            </tfoot>
                        </table>
                    </div>

                    {/* Equity */}
                    <div className="card">
                        <h3 className="text-lg font-semibold text-green-800 mb-4">
                            🏛️ {t('treasury.equity', 'EQUITY')}
                        </h3>
                        <table className="min-w-full">
                            <tbody className="divide-y divide-gray-100">
                                {report?.equityLines?.map((line, idx) => (
                                    <tr key={idx} className="hover:bg-gray-50 cursor-pointer"
                                        onClick={() => navigate(`/treasury/general-ledger?accountId=${line.accountId}`)}>
                                        <td className="py-2 text-sm font-mono text-gray-500">{line.accountCode}</td>
                                        <td className="py-2 text-sm text-gray-900">{line.accountName}</td>
                                        <td className="py-2 text-sm text-right font-semibold text-gray-900">
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
                                    <td className="py-3 text-right font-bold text-green-900">
                                        {formatCurrency(report?.totalEquity || 0)}
                                    </td>
                                </tr>
                            </tfoot>
                        </table>
                    </div>

                    {/* Total L+E */}
                    <div className="p-4 bg-gray-100 rounded-lg">
                        <div className="flex justify-between items-center">
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

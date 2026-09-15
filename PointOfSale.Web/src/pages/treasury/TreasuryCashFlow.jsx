/**
 * Cash Flow Statement Report Page
 * 
 * PHASE 5: Treasury & Finance Control Center
 * 
 * Shows cash movement using indirect method.
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

export default function TreasuryCashFlow() {
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
            const data = await finApi.getCashFlow(dateRange.from, dateRange.to)
            setReport(data)
        } catch (error) {
            console.error('Failed to load cash flow:', error)
            showError(t('treasury.loadError', 'Failed to load report'))
        } finally {
            setLoading(false)
        }
    }

    const exportToPdf = () => {
        import('jspdf').then(({ default: jsPDF }) => {
            const doc = new jsPDF()

            doc.setFontSize(18)
            doc.text(t('treasury.cashFlow', 'Cash Flow Statement'), 14, 22)
            doc.setFontSize(11)
            doc.text(`${t('common.period', 'Period')}: ${formatDate(dateRange.from, locale)} - ${formatDate(dateRange.to, locale)}`, 14, 32)

            let y = 50

            // Opening Balance
            doc.setFont(undefined, 'normal')
            doc.text(t('treasury.openingCash', 'Opening Cash Balance'), 14, y)
            doc.text(formatCurrency(report.openingCashBalance), 180, y, { align: 'right' })
            y += 10

            // Operating Activities
            doc.setFont(undefined, 'bold')
            doc.text(t('treasury.operatingActivities', 'Cash from Operating Activities'), 14, y)
            y += 8
            doc.setFont(undefined, 'normal')
            doc.text(`  ${t('treasury.netProfit', 'Net Profit')}`, 14, y)
            doc.text(formatCurrency(report.netProfit), 180, y, { align: 'right' })
            y += 6

            report.workingCapitalChanges?.forEach(item => {
                doc.text(`  ${item.description}`, 14, y)
                doc.text(formatCurrency(item.amount), 180, y, { align: 'right' })
                y += 6
            })

            doc.setFont(undefined, 'bold')
            doc.text(t('treasury.netOperatingFlow', 'Net Operating Cash Flow'), 14, y)
            doc.text(formatCurrency(report.operatingCashFlow), 180, y, { align: 'right' })
            y += 10

            // Investing & Financing (if applicable)
            if (report.investingCashFlow !== 0) {
                doc.text(t('treasury.netInvestingFlow', 'Net Investing Cash Flow'), 14, y)
                doc.text(formatCurrency(report.investingCashFlow), 180, y, { align: 'right' })
                y += 8
            }
            if (report.financingCashFlow !== 0) {
                doc.text(t('treasury.netFinancingFlow', 'Net Financing Cash Flow'), 14, y)
                doc.text(formatCurrency(report.financingCashFlow), 180, y, { align: 'right' })
                y += 8
            }

            // Net Change & Closing
            y += 5
            doc.setFontSize(12)
            doc.text(t('treasury.netChange', 'Net Change in Cash'), 14, y)
            doc.text(formatCurrency(report.netCashChange), 180, y, { align: 'right' })
            y += 10

            doc.text(t('treasury.closingCash', 'Closing Cash Balance'), 14, y)
            doc.text(formatCurrency(report.closingCashBalance), 180, y, { align: 'right' })
            y += 12

            // Reconciliation status
            if (report.reconciles) {
                doc.setTextColor(0, 128, 0)
                doc.text(`✓ ${t('treasury.reconciled', 'Reconciled')}`, 14, y)
            } else {
                doc.setTextColor(255, 0, 0)
                doc.text(`✗ ${t('treasury.notReconciled', 'Not Reconciled')}`, 14, y)
            }

            doc.save(`CashFlow_${formatDateInputValue(dateRange.from)}.pdf`)
            showSuccess(t('common.exportSuccess', 'Exported successfully'))
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

            {/* Cash perimeter: certified Cashbox/Bank only. */}
            <div className="p-4 rounded-lg bg-slate-50 border border-slate-200" role="note">
                <p className="text-sm text-slate-700">
                    {t('treasury.cashFlowPerimeter', 'Cash in this report means mapped Cashbox and Bank accounts only. Clearing and restricted card assets are excluded.')}
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
                            <span aria-hidden="true">💧 </span>{t('treasury.cashFlow', 'Cash Flow Statement')}
                        </h2>
                        <p className="text-gray-600">
                            {formatDate(dateRange.from, locale)} - {formatDate(dateRange.to, locale)}
                        </p>
                    </div>
                </div>

                <div className="flex w-full flex-wrap items-center gap-3 md:w-auto">
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
                    <button onClick={exportToPdf} disabled={!report} className="btn-secondary flex-1 text-sm sm:flex-none">📄 PDF</button>
                </div>
            </div>

            {/* Reconciliation Status */}
            <div role="status" className={`p-4 rounded-lg flex items-center justify-between ${report?.reconciles
                ? 'bg-green-50 border border-green-200'
                : 'bg-red-50 border border-red-200'
                }`}>
                <div className="flex items-center gap-3">
                    <span className="text-3xl" aria-hidden="true">{report?.reconciles ? '✅' : '❌'}</span>
                    <div>
                        <p className={`font-semibold ${report?.reconciles ? 'text-green-800' : 'text-red-800'}`}>
                            {report?.reconciles ? t('treasury.reconciled', 'RECONCILED') : t('treasury.notReconciled', 'NOT RECONCILED')}
                        </p>
                        <p className="text-sm text-gray-600">
                            {t('treasury.cashFlowFormula', 'Opening + Net Change = Closing')}
                        </p>
                    </div>
                </div>
            </div>

            {/* Cash Flow Summary Cards */}
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
                <div className="card bg-gray-50">
                    <p className="text-sm text-gray-600">{t('treasury.openingCash', 'Opening Cash')}</p>
                    <p className="text-2xl font-bold text-gray-900">{formatCurrency(report?.openingCashBalance || 0)}</p>
                </div>
                <div className={`card ${(report?.netCashChange || 0) >= 0 ? 'bg-green-50' : 'bg-red-50'}`}>
                    <p className={`text-sm ${(report?.netCashChange || 0) >= 0 ? 'text-green-600' : 'text-red-600'}`}>
                        {t('treasury.netChange', 'Net Change')}
                    </p>
                    <p className={`text-2xl font-bold ${(report?.netCashChange || 0) >= 0 ? 'text-green-900' : 'text-red-900'}`}>
                        {(report?.netCashChange || 0) >= 0 ? '+' : ''}{formatCurrency(report?.netCashChange || 0)}
                    </p>
                </div>
                <div className="card bg-blue-50">
                    <p className="text-sm text-blue-600">{t('treasury.closingCash', 'Closing Cash')}</p>
                    <p className="text-2xl font-bold text-blue-900">{formatCurrency(report?.closingCashBalance || 0)}</p>
                </div>
                <div className="card bg-emerald-50">
                    <p className="text-sm text-emerald-600">{t('treasury.operatingFlow', 'Operating Flow')}</p>
                    <p className="text-2xl font-bold text-emerald-900">{formatCurrency(report?.operatingCashFlow || 0)}</p>
                </div>
            </div>

            {/* Cash Flow Sections */}
            <div className="space-y-6">
                {/* Operating Activities */}
                <div className="card overflow-x-auto">
                    <h3 className="text-lg font-semibold text-emerald-800 mb-4">
                        🏭 {t('treasury.operatingActivities', 'Cash from Operating Activities')}
                    </h3>
                    <table className="min-w-full">
                        <tbody className="divide-y divide-gray-100">
                            {/* Net Profit */}
                            <tr className="bg-gray-50">
                                <td className="py-3 font-medium text-gray-900">
                                    {t('treasury.netProfit', 'Net Profit')}
                                </td>
                                <td className="py-3 text-end font-semibold text-gray-900">
                                    {formatCurrency(report?.netProfit || 0)}
                                </td>
                            </tr>

                            {/* Working Capital Changes */}
                            <tr>
                                <td colSpan={2} className="pt-4 pb-2 text-sm font-medium text-gray-500">
                                    {t('treasury.workingCapitalChanges', 'Adjustments for Working Capital:')}
                                </td>
                            </tr>
                            {report?.workingCapitalChanges?.map((item, idx) => (
                                <tr key={idx}>
                                    <td className="py-2 ps-4 text-sm text-gray-700">{item.description}</td>
                                    <td className={`py-2 text-end text-sm font-medium ${item.amount >= 0 ? 'text-green-600' : 'text-red-600'}`}>
                                        {item.amount >= 0 ? '+' : ''}{formatCurrency(item.amount)}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                        <tfoot className="bg-emerald-50">
                            <tr>
                                <td className="py-3 font-bold text-emerald-900">
                                    {t('treasury.netOperatingFlow', 'Net Cash from Operating Activities')}
                                </td>
                                <td className="py-3 text-end font-bold text-emerald-900">
                                    {formatCurrency(report?.operatingCashFlow || 0)}
                                </td>
                            </tr>
                        </tfoot>
                    </table>
                </div>

                {/* Investing Activities (if any) */}
                {(report?.investingCashFlow !== 0 || report?.investingActivities?.length > 0) && (
                    <div className="card overflow-x-auto">
                        <h3 className="text-lg font-semibold text-blue-800 mb-4">
                            📊 {t('treasury.investingActivities', 'Cash from Investing Activities')}
                        </h3>
                        <table className="min-w-full">
                            <tbody>
                                {report?.investingActivities?.map((item, idx) => (
                                    <tr key={idx} className="border-b border-gray-100">
                                        <td className="py-2 text-sm text-gray-700">{item.description}</td>
                                        <td className="py-2 text-end text-sm font-medium text-gray-900">
                                            {formatCurrency(item.amount)}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                            <tfoot className="bg-blue-50">
                                <tr>
                                    <td className="py-3 font-bold text-blue-900">
                                        {t('treasury.netInvestingFlow', 'Net Cash from Investing')}
                                    </td>
                                    <td className="py-3 text-end font-bold text-blue-900">
                                        {formatCurrency(report?.investingCashFlow || 0)}
                                    </td>
                                </tr>
                            </tfoot>
                        </table>
                    </div>
                )}

                {/* Financing Activities (if any) */}
                {(report?.financingCashFlow !== 0 || report?.financingActivities?.length > 0) && (
                    <div className="card overflow-x-auto">
                        <h3 className="text-lg font-semibold text-purple-800 mb-4">
                            💳 {t('treasury.financingActivities', 'Cash from Financing Activities')}
                        </h3>
                        <table className="min-w-full">
                            <tbody>
                                {report?.financingActivities?.map((item, idx) => (
                                    <tr key={idx} className="border-b border-gray-100">
                                        <td className="py-2 text-sm text-gray-700">{item.description}</td>
                                        <td className="py-2 text-end text-sm font-medium text-gray-900">
                                            {formatCurrency(item.amount)}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                            <tfoot className="bg-purple-50">
                                <tr>
                                    <td className="py-3 font-bold text-purple-900">
                                        {t('treasury.netFinancingFlow', 'Net Cash from Financing')}
                                    </td>
                                    <td className="py-3 text-end font-bold text-purple-900">
                                        {formatCurrency(report?.financingCashFlow || 0)}
                                    </td>
                                </tr>
                            </tfoot>
                        </table>
                    </div>
                )}

                {/* Summary Box */}
                <div className="p-6 bg-gradient-to-br from-gray-50 to-gray-100 rounded-lg border-2 border-gray-200">
                    <h3 className="text-lg font-semibold text-gray-800 mb-4">
                        📋 {t('treasury.cashReconciliation', 'Cash Reconciliation')}
                    </h3>
                    <table className="min-w-full">
                        <tbody>
                            <tr>
                                <td className="py-2 text-gray-700">{t('treasury.openingCash', 'Opening Cash Balance')}</td>
                                <td className="py-2 text-end font-medium text-gray-900">
                                    {formatCurrency(report?.openingCashBalance || 0)}
                                </td>
                            </tr>
                            <tr>
                                <td className="py-2 text-gray-700">{t('treasury.operatingFlow', 'Operating Cash Flow')}</td>
                                <td className={`py-2 text-end font-medium ${(report?.operatingCashFlow || 0) >= 0 ? 'text-green-600' : 'text-red-600'}`}>
                                    {(report?.operatingCashFlow || 0) >= 0 ? '+' : ''}{formatCurrency(report?.operatingCashFlow || 0)}
                                </td>
                            </tr>
                            {report?.investingCashFlow !== 0 && (
                                <tr>
                                    <td className="py-2 text-gray-700">{t('treasury.investingFlow', 'Investing Cash Flow')}</td>
                                    <td className={`py-2 text-end font-medium ${(report?.investingCashFlow || 0) >= 0 ? 'text-green-600' : 'text-red-600'}`}>
                                        {(report?.investingCashFlow || 0) >= 0 ? '+' : ''}{formatCurrency(report?.investingCashFlow || 0)}
                                    </td>
                                </tr>
                            )}
                            {report?.financingCashFlow !== 0 && (
                                <tr>
                                    <td className="py-2 text-gray-700">{t('treasury.financingFlow', 'Financing Cash Flow')}</td>
                                    <td className={`py-2 text-end font-medium ${(report?.financingCashFlow || 0) >= 0 ? 'text-green-600' : 'text-red-600'}`}>
                                        {(report?.financingCashFlow || 0) >= 0 ? '+' : ''}{formatCurrency(report?.financingCashFlow || 0)}
                                    </td>
                                </tr>
                            )}
                            <tr className="border-t-2 border-gray-300">
                                <td className="py-3 font-bold text-gray-900">{t('treasury.closingCash', 'Closing Cash Balance')}</td>
                                <td className="py-3 text-end text-xl font-bold text-blue-600">
                                    {formatCurrency(report?.closingCashBalance || 0)}
                                </td>
                            </tr>
                        </tbody>
                    </table>
                </div>
            </div>

            {/* Footer */}
            <div className="text-center py-4 text-sm text-gray-500">
                {t('treasury.indirectMethod', 'Indirect method')} •
                {t('treasury.ledgerSourced', 'Sourced from immutable ledger')} •
                {t('treasury.generated', 'Generated')}: {formatDateTime(new Date(), locale)}
            </div>
        </div>
    )
}

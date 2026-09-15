/**
 * Profit & Loss (Income Statement) Report Page
 * 
 * PHASE 5: Treasury & Finance Control Center
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

export default function TreasuryProfitLoss() {
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
            const data = await finApi.getProfitAndLoss(dateRange.from, dateRange.to)
            setReport(data)
        } catch (error) {
            console.error('Failed to load P&L:', error)
            showError(t('treasury.loadError', 'Failed to load report'))
        } finally {
            setLoading(false)
        }
    }

    const exportToExcel = () => {
        import('xlsx').then(XLSX => {
            const data = []

            // Revenue section
            const sectionKey = t('common.section', 'Section')
            const accountKey = t('treasury.account', 'Account')
            const amountKey = t('treasury.amount', 'Amount')
            data.push({ [sectionKey]: t('treasury.revenue', 'Revenue'), [accountKey]: '', [amountKey]: '' })
            report.revenueLines?.forEach(l => data.push({
                [sectionKey]: '',
                [accountKey]: `${l.accountCode} - ${l.accountName}`,
                [amountKey]: l.amount
            }))
            data.push({ [sectionKey]: t('treasury.totalRevenue', 'Total Revenue'), [accountKey]: '', [amountKey]: report.totalRevenue })
            data.push({ [sectionKey]: '', [accountKey]: '', [amountKey]: '' })

            // COGS section
            data.push({ [sectionKey]: t('treasury.cogs', 'Cost of Goods Sold'), [accountKey]: '', [amountKey]: '' })
            report.costOfGoodsSoldLines?.forEach(l => data.push({
                [sectionKey]: '',
                [accountKey]: `${l.accountCode} - ${l.accountName}`,
                [amountKey]: l.amount
            }))
            data.push({ [sectionKey]: t('treasury.totalCogs', 'Total Cost of Goods Sold'), [accountKey]: '', [amountKey]: report.totalCostOfGoodsSold })
            data.push({ [sectionKey]: '', [accountKey]: '', [amountKey]: '' })

            // Gross Profit
            data.push({ [sectionKey]: t('treasury.grossProfit', 'Gross Profit'), [accountKey]: `${report.grossProfitMargin?.toFixed(1)}% ${t('treasury.margin', 'margin')}`, [amountKey]: report.grossProfit })
            data.push({ [sectionKey]: '', [accountKey]: '', [amountKey]: '' })

            // Expenses section
            data.push({ [sectionKey]: t('treasury.expenses', 'Operating Expenses'), [accountKey]: '', [amountKey]: '' })
            report.expenseLines?.forEach(l => data.push({
                [sectionKey]: '',
                [accountKey]: `${l.accountCode} - ${l.accountName}`,
                [amountKey]: l.amount
            }))
            data.push({ [sectionKey]: t('treasury.totalExpenses', 'Total Expenses'), [accountKey]: '', [amountKey]: report.totalExpenses })
            data.push({ [sectionKey]: '', [accountKey]: '', [amountKey]: '' })

            // Net Profit
            data.push({ [sectionKey]: t('treasury.netProfit', 'Net Profit'), [accountKey]: `${report.netProfitMargin?.toFixed(1)}% ${t('treasury.margin', 'margin')}`, [amountKey]: report.netProfit })

            const ws = XLSX.utils.json_to_sheet(data)
            const wb = XLSX.utils.book_new()
            XLSX.utils.book_append_sheet(wb, ws, t('treasury.profitLoss', 'Profit & Loss').slice(0, 31))
            XLSX.writeFile(wb, `ProfitLoss_${formatDateInputValue(dateRange.from)}_${formatDateInputValue(dateRange.to)}.xlsx`)
            showSuccess(t('common.exportSuccess', 'Exported successfully'))
        })
    }

    const exportToPdf = () => {
        import('jspdf').then(({ default: jsPDF }) => {
            import('jspdf-autotable').then(() => {
                const doc = new jsPDF()

                doc.setFontSize(18)
                doc.text(t('treasury.profitLoss', 'Profit & Loss Statement'), 14, 22)
                doc.setFontSize(11)
                doc.text(`${t('common.period', 'Period')}: ${formatDate(dateRange.from, locale)} - ${formatDate(dateRange.to, locale)}`, 14, 32)

                let y = 45

                // Revenue
                doc.setFontSize(12)
                doc.setFont(undefined, 'bold')
                doc.text(t('treasury.revenue', 'Revenue'), 14, y)
                y += 8
                doc.setFont(undefined, 'normal')
                doc.setFontSize(10)
                report.revenueLines?.forEach(l => {
                    doc.text(`  ${l.accountCode} - ${l.accountName}`, 14, y)
                    doc.text(formatCurrency(l.amount), 180, y, { align: 'right' })
                    y += 6
                })
                doc.setFont(undefined, 'bold')
                doc.text(t('treasury.totalRevenue', 'Total Revenue'), 14, y)
                doc.text(formatCurrency(report.totalRevenue), 180, y, { align: 'right' })
                y += 12

                // COGS
                doc.setFontSize(12)
                doc.text(t('treasury.cogs', 'Cost of Goods Sold'), 14, y)
                y += 8
                doc.setFont(undefined, 'normal')
                doc.setFontSize(10)
                report.costOfGoodsSoldLines?.forEach(l => {
                    doc.text(`  ${l.accountCode} - ${l.accountName}`, 14, y)
                    doc.text(formatCurrency(l.amount), 180, y, { align: 'right' })
                    y += 6
                })
                doc.setFont(undefined, 'bold')
                doc.text(t('treasury.totalCogs', 'Total Cost of Goods Sold'), 14, y)
                doc.text(formatCurrency(report.totalCostOfGoodsSold), 180, y, { align: 'right' })
                y += 12

                // Gross Profit
                doc.setFontSize(12)
                doc.setTextColor(0, 128, 0)
                doc.text(`${t('treasury.grossProfit', 'Gross Profit')} (${report.grossProfitMargin?.toFixed(1)}% ${t('treasury.margin', 'margin')})`, 14, y)
                doc.text(formatCurrency(report.grossProfit), 180, y, { align: 'right' })
                doc.setTextColor(0, 0, 0)
                y += 12

                // Expenses
                doc.text(t('treasury.expenses', 'Operating Expenses'), 14, y)
                y += 8
                doc.setFont(undefined, 'normal')
                doc.setFontSize(10)
                report.expenseLines?.forEach(l => {
                    doc.text(`  ${l.accountCode} - ${l.accountName}`, 14, y)
                    doc.text(formatCurrency(l.amount), 180, y, { align: 'right' })
                    y += 6
                })
                doc.setFont(undefined, 'bold')
                doc.text(t('treasury.totalExpenses', 'Total Expenses'), 14, y)
                doc.text(formatCurrency(report.totalExpenses), 180, y, { align: 'right' })
                y += 12

                // Net Profit
                doc.setFontSize(14)
                if (report.netProfit >= 0) {
                    doc.setTextColor(0, 128, 0)
                } else {
                    doc.setTextColor(255, 0, 0)
                }
                doc.text(`${t('treasury.netProfit', 'Net Profit')} (${report.netProfitMargin?.toFixed(1)}% ${t('treasury.margin', 'margin')})`, 14, y)
                doc.text(formatCurrency(report.netProfit), 180, y, { align: 'right' })

                doc.save(`ProfitLoss_${formatDateInputValue(dateRange.from)}.pdf`)
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

            {/* Header */}
            <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:gap-4">
                    <button onClick={() => navigate('/treasury')} className="self-start text-gray-500 hover:text-gray-700">
                        <span aria-hidden="true" className="inline-block rtl:rotate-180">←</span> {t('common.back')}
                    </button>
                    <div>
                        <h2 className="text-2xl font-bold text-gray-900">
                            <span aria-hidden="true">📈 </span>{t('treasury.profitLoss', 'Profit & Loss Statement')}
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
                    <button onClick={exportToExcel} disabled={!report} className="btn-secondary flex-1 text-sm sm:flex-none">📊 Excel</button>
                    <button onClick={exportToPdf} disabled={!report} className="btn-secondary flex-1 text-sm sm:flex-none">📄 PDF</button>
                </div>
            </div>

            {/* Summary Cards */}
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
                <div className="card bg-blue-50 border-blue-200">
                    <p className="text-sm text-blue-600">{t('treasury.revenue', 'Revenue')}</p>
                    <p className="text-2xl font-bold text-blue-900">{formatCurrency(report?.totalRevenue || 0)}</p>
                </div>
                <div className="card bg-orange-50 border-orange-200">
                    <p className="text-sm text-orange-600">{t('treasury.cogs', 'Cost of Goods')}</p>
                    <p className="text-2xl font-bold text-orange-900">{formatCurrency(report?.totalCostOfGoodsSold || 0)}</p>
                </div>
                <div className="card bg-green-50 border-green-200">
                    <p className="text-sm text-green-600">{t('treasury.grossProfit', 'Gross Profit')}</p>
                    <p className="text-2xl font-bold text-green-900">{formatCurrency(report?.grossProfit || 0)}</p>
                    <p className="text-xs text-green-500">{report?.grossProfitMargin?.toFixed(1)}% {t('treasury.margin')}</p>
                </div>
                <div className={`card ${(report?.netProfit || 0) >= 0 ? 'bg-emerald-50 border-emerald-200' : 'bg-red-50 border-red-200'}`}>
                    <p className={`text-sm ${(report?.netProfit || 0) >= 0 ? 'text-emerald-600' : 'text-red-600'}`}>
                        {t('treasury.netProfit', 'Net Profit')}
                    </p>
                    <p className={`text-2xl font-bold ${(report?.netProfit || 0) >= 0 ? 'text-emerald-900' : 'text-red-900'}`}>
                        {formatCurrency(report?.netProfit || 0)}
                    </p>
                    <p className={`text-xs ${(report?.netProfit || 0) >= 0 ? 'text-emerald-500' : 'text-red-500'}`}>
                        {report?.netProfitMargin?.toFixed(1)}% {t('treasury.margin')}
                    </p>
                </div>
            </div>

            {/* P&L Sections */}
            <div className="space-y-6">
                {/* Revenue Section */}
                <div className="card overflow-x-auto">
                    <h3 className="text-lg font-semibold text-blue-800 mb-4 flex items-center gap-2">
                        💰 {t('treasury.revenue', 'Revenue')}
                    </h3>
                    <table className="min-w-full">
                        <tbody className="divide-y divide-gray-100">
                            {report?.revenueLines?.map((line) => (
                                <tr key={line.accountId} className="hover:bg-gray-50">
                                    <td className="py-3 text-sm font-mono text-gray-500"><bdi>{line.accountCode}</bdi></td>
                                    <td className="py-3 text-sm text-gray-900"><button type="button" className="text-start hover:underline" onClick={() => navigate(`/treasury/general-ledger?accountId=${line.accountId}`)}>{line.accountName}</button></td>
                                    <td className="py-3 text-sm text-end font-semibold text-blue-600">
                                        {formatCurrency(line.amount)}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                        <tfoot className="bg-blue-50">
                            <tr>
                                <td colSpan={2} className="py-3 text-sm font-bold text-blue-900">
                                    {t('treasury.totalRevenue', 'Total Revenue')}
                                </td>
                                <td className="py-3 text-end font-bold text-blue-900">
                                    {formatCurrency(report?.totalRevenue || 0)}
                                </td>
                            </tr>
                        </tfoot>
                    </table>
                </div>

                {/* COGS Section */}
                <div className="card overflow-x-auto">
                    <h3 className="text-lg font-semibold text-orange-800 mb-4 flex items-center gap-2">
                        📦 {t('treasury.cogs', 'Cost of Goods Sold')}
                    </h3>
                    <table className="min-w-full">
                        <tbody className="divide-y divide-gray-100">
                            {report?.costOfGoodsSoldLines?.map((line) => (
                                <tr key={line.accountId} className="hover:bg-gray-50">
                                    <td className="py-3 text-sm font-mono text-gray-500"><bdi>{line.accountCode}</bdi></td>
                                    <td className="py-3 text-sm text-gray-900"><button type="button" className="text-start hover:underline" onClick={() => navigate(`/treasury/general-ledger?accountId=${line.accountId}`)}>{line.accountName}</button></td>
                                    <td className="py-3 text-sm text-end font-semibold text-orange-600">
                                        ({formatCurrency(line.amount)})
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                        <tfoot className="bg-orange-50">
                            <tr>
                                <td colSpan={2} className="py-3 text-sm font-bold text-orange-900">
                                    {t('treasury.totalCogs', 'Total COGS')}
                                </td>
                                <td className="py-3 text-end font-bold text-orange-900">
                                    ({formatCurrency(report?.totalCostOfGoodsSold || 0)})
                                </td>
                            </tr>
                        </tfoot>
                    </table>
                </div>

                {/* Gross Profit Highlight */}
                <div className="p-4 bg-green-100 rounded-lg border-2 border-green-300">
                    <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                        <span className="text-lg font-bold text-green-800">
                            {t('treasury.grossProfit', 'GROSS PROFIT')}
                        </span>
                        <div className="text-end">
                            <span className="text-2xl font-bold text-green-900">
                                {formatCurrency(report?.grossProfit || 0)}
                            </span>
                            <p className="text-sm text-green-600">
                                {report?.grossProfitMargin?.toFixed(1)}% {t('treasury.margin', 'margin')}
                            </p>
                        </div>
                    </div>
                </div>

                {/* Expenses Section */}
                <div className="card overflow-x-auto">
                    <h3 className="text-lg font-semibold text-red-800 mb-4 flex items-center gap-2">
                        📉 {t('treasury.expenses', 'Operating Expenses')}
                    </h3>
                    <table className="min-w-full">
                        <tbody className="divide-y divide-gray-100">
                            {report?.expenseLines?.map((line) => (
                                <tr key={line.accountId} className="hover:bg-gray-50">
                                    <td className="py-3 text-sm font-mono text-gray-500"><bdi>{line.accountCode}</bdi></td>
                                    <td className="py-3 text-sm text-gray-900"><button type="button" className="text-start hover:underline" onClick={() => navigate(`/treasury/general-ledger?accountId=${line.accountId}`)}>{line.accountName}</button></td>
                                    <td className="py-3 text-sm text-end font-semibold text-red-600">
                                        ({formatCurrency(line.amount)})
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                        <tfoot className="bg-red-50">
                            <tr>
                                <td colSpan={2} className="py-3 text-sm font-bold text-red-900">
                                    {t('treasury.totalExpenses', 'Total Expenses')}
                                </td>
                                <td className="py-3 text-end font-bold text-red-900">
                                    ({formatCurrency(report?.totalExpenses || 0)})
                                </td>
                            </tr>
                        </tfoot>
                    </table>
                </div>

                {/* Net Profit Highlight */}
                <div className={`p-6 rounded-lg border-2 ${(report?.netProfit || 0) >= 0
                    ? 'bg-emerald-100 border-emerald-400'
                    : 'bg-red-100 border-red-400'
                    }`}>
                    <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                        <span className={`text-xl font-bold ${(report?.netProfit || 0) >= 0 ? 'text-emerald-800' : 'text-red-800'
                            }`}>
                            {t('treasury.netProfit', 'NET PROFIT')}
                        </span>
                        <div className="text-end">
                            <span className={`text-3xl font-bold ${(report?.netProfit || 0) >= 0 ? 'text-emerald-900' : 'text-red-900'
                                }`}>
                                {formatCurrency(report?.netProfit || 0)}
                            </span>
                            <p className={`text-sm ${(report?.netProfit || 0) >= 0 ? 'text-emerald-600' : 'text-red-600'
                                }`}>
                                {report?.netProfitMargin?.toFixed(1)}% {t('treasury.margin', 'margin')}
                            </p>
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

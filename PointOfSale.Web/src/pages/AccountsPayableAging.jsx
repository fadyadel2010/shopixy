import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'
import { useToast } from '../components/Toast'
import { useSettings } from '../contexts/SettingsContext'
import { useRTL } from '../hooks/useRTL'
import { formatDate } from '../utils/dateFormat'

export default function AccountsPayableAging() {
    const { t } = useTranslation()
    const { isRTL, dir, locale } = useRTL()
    const { formatCurrency } = useSettings()
    const { showSuccess, showError } = useToast()
    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const checkboxLabelSpacingClass = isRTL ? 'mr-2' : 'ml-2'

    const [loading, setLoading] = useState(false)
    const [reportData, setReportData] = useState(null)
    const [asOfDate, setAsOfDate] = useState(new Date().toISOString().split('T')[0])
    const [includeZeroBalance, setIncludeZeroBalance] = useState(false)

    useEffect(() => {
        handleGenerateReport()
    }, [])

    const handleGenerateReport = async () => {
        setLoading(true)
        try {
            const params = {
                asOfDate,
                includeZeroBalance
            }

            const response = await api.get('/reports/accounts-payable-aging', { params })
            setReportData(response.data)
        } catch (error) {
            console.error('Error generating report:', error)
            showError(t('apAging.errorGenerating'))
        } finally {
            setLoading(false)
        }
    }

    const handlePrint = () => {
        window.print()
    }

    const handleExportCSV = () => {
        if (!reportData || !reportData.suppliers.length) {
            showError(t('apAging.noDataToExport'))
            return
        }

        const headers = [
            t('apAging.supplierName'),
            t('apAging.phone'),
            t('apAging.email'),
            t('apAging.totalBalance'),
            t('apAging.current'),
            t('apAging.days1To30'),
            t('apAging.days31To60'),
            t('apAging.days61To90'),
            t('apAging.days90Plus'),
            t('apAging.daysOverdue')
        ].join(',')

        const rows = reportData.suppliers.map(s => [
            s.supplierName,
            s.phone || '-',
            s.email || '-',
            s.totalBalance.toFixed(2),
            s.current.toFixed(2),
            s.days1To30.toFixed(2),
            s.days31To60.toFixed(2),
            s.days61To90.toFixed(2),
            s.days90Plus.toFixed(2),
            s.daysOverdue
        ].join(','))

        const csv = [headers, ...rows].join('\n')
        const blob = new Blob([csv], { type: 'text/csv' })
        const url = window.URL.createObjectURL(blob)
        const a = document.createElement('a')
        a.href = url
        a.download = `ap-aging-${asOfDate}.csv`
        a.click()
        window.URL.revokeObjectURL(url)
        showSuccess(t('apAging.exportSuccess'))
    }

    const getAgingBadgeColor = (daysOverdue) => {
        if (daysOverdue <= 0) return 'bg-green-100 text-green-800'
        if (daysOverdue <= 30) return 'bg-yellow-100 text-yellow-800'
        if (daysOverdue <= 60) return 'bg-orange-100 text-orange-800'
        if (daysOverdue <= 90) return 'bg-red-100 text-red-800'
        return 'bg-red-200 text-red-900'
    }

    const calculatePercentage = (amount, total) => {
        if (total === 0) return 0
        return ((amount / total) * 100).toFixed(1)
    }

    return (
        <div dir={dir} className="p-6 max-w-7xl mx-auto">
            {/* Header */}
            <div className="mb-6 print:mb-4">
                <h1 className="text-3xl font-bold text-gray-800 mb-2 print:text-2xl">
                    {t('apAging.title')}
                </h1>
                <p className="text-gray-600 print:hidden">{t('apAging.subtitle')}</p>
            </div>

            {/* Filters */}
            <div className="bg-white rounded-xl shadow-md p-6 mb-6 print:hidden">
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-4">
                    {/* As Of Date */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('apAging.asOfDate')}
                        </label>
                        <input
                            type="date"
                            value={asOfDate}
                            onChange={(e) => setAsOfDate(e.target.value)}
                            className="w-full px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                        />
                    </div>

                    {/* Include Zero Balance */}
                    <div className="flex items-end">
                        <label className="flex items-center cursor-pointer">
                            <input
                                type="checkbox"
                                checked={includeZeroBalance}
                                onChange={(e) => setIncludeZeroBalance(e.target.checked)}
                                className="w-5 h-5 text-indigo-600 border-gray-300 rounded focus:ring-indigo-500"
                            />
                            <span className={`${checkboxLabelSpacingClass} text-sm text-gray-700`}>
                                {t('apAging.includeZeroBalance')}
                            </span>
                        </label>
                    </div>
                </div>

                {/* Action Buttons */}
                <div className="flex gap-3">
                    <button
                        onClick={handleGenerateReport}
                        disabled={loading}
                        className="px-6 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:bg-gray-300 disabled:cursor-not-allowed transition-colors"
                    >
                        {loading ? t('common.loading') : t('apAging.generateReport')}
                    </button>
                    {reportData && (
                        <>
                            <button
                                onClick={handlePrint}
                                className="px-6 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 transition-colors"
                            >
                                🖨️ {t('common.print')}
                            </button>
                            <button
                                onClick={handleExportCSV}
                                className="px-6 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
                            >
                                📊 {t('common.exportCSV')}
                            </button>
                        </>
                    )}
                </div>
            </div>

            {/* Report Content */}
            {reportData && (
                <div className="space-y-6">
                    {/* Report Header */}
                    <div className="bg-white rounded-xl shadow-md p-6 print:shadow-none">
                        <div className="mb-4 pb-4 border-b border-gray-200">
                            <h2 className="text-2xl font-bold text-gray-800 mb-2">
                                {t('apAging.reportTitle')}
                            </h2>
                            <p className="text-gray-600">
                                {t('apAging.asOfDate')}: {formatDate(reportData.reportDate, locale)}
                            </p>
                        </div>

                        {/* Summary Cards */}
                        <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-4">
                            {/* Total Outstanding */}
                            <div className="bg-gradient-to-br from-purple-50 to-purple-100 rounded-lg p-4 border border-purple-200">
                                <div className="text-xs text-purple-600 mb-1 font-medium">{t('apAging.totalOutstanding')}</div>
                                <div className="text-xl font-bold text-purple-900">{formatCurrency(reportData.totalOutstanding)}</div>
                                <div className="text-xs text-purple-600 mt-1">100%</div>
                            </div>

                            {/* Current */}
                            <div className="bg-gradient-to-br from-green-50 to-green-100 rounded-lg p-4 border border-green-200">
                                <div className="text-xs text-green-600 mb-1 font-medium">{t('apAging.current')}</div>
                                <div className="text-xl font-bold text-green-900">{formatCurrency(reportData.current)}</div>
                                <div className="text-xs text-green-600 mt-1">
                                    {calculatePercentage(reportData.current, reportData.totalOutstanding)}%
                                </div>
                            </div>

                            {/* 1-30 Days */}
                            <div className="bg-gradient-to-br from-yellow-50 to-yellow-100 rounded-lg p-4 border border-yellow-200">
                                <div className="text-xs text-yellow-600 mb-1 font-medium">{t('apAging.days1To30')}</div>
                                <div className="text-xl font-bold text-yellow-900">{formatCurrency(reportData.days1To30)}</div>
                                <div className="text-xs text-yellow-600 mt-1">
                                    {calculatePercentage(reportData.days1To30, reportData.totalOutstanding)}%
                                </div>
                            </div>

                            {/* 31-60 Days */}
                            <div className="bg-gradient-to-br from-orange-50 to-orange-100 rounded-lg p-4 border border-orange-200">
                                <div className="text-xs text-orange-600 mb-1 font-medium">{t('apAging.days31To60')}</div>
                                <div className="text-xl font-bold text-orange-900">{formatCurrency(reportData.days31To60)}</div>
                                <div className="text-xs text-orange-600 mt-1">
                                    {calculatePercentage(reportData.days31To60, reportData.totalOutstanding)}%
                                </div>
                            </div>

                            {/* 61-90 Days */}
                            <div className="bg-gradient-to-br from-red-50 to-red-100 rounded-lg p-4 border border-red-200">
                                <div className="text-xs text-red-600 mb-1 font-medium">{t('apAging.days61To90')}</div>
                                <div className="text-xl font-bold text-red-900">{formatCurrency(reportData.days61To90)}</div>
                                <div className="text-xs text-red-600 mt-1">
                                    {calculatePercentage(reportData.days61To90, reportData.totalOutstanding)}%
                                </div>
                            </div>

                            {/* 90+ Days */}
                            <div className="bg-gradient-to-br from-red-100 to-red-200 rounded-lg p-4 border border-red-300">
                                <div className="text-xs text-red-700 mb-1 font-medium">{t('apAging.days90Plus')}</div>
                                <div className="text-xl font-bold text-red-900">{formatCurrency(reportData.days90Plus)}</div>
                                <div className="text-xs text-red-700 mt-1">
                                    {calculatePercentage(reportData.days90Plus, reportData.totalOutstanding)}%
                                </div>
                            </div>
                        </div>
                    </div>

                    {/* Supplier Details Table */}
                    <div className="bg-white rounded-xl shadow-md overflow-hidden print:shadow-none">
                        <div className="px-6 py-4 bg-gray-50 border-b border-gray-200">
                            <h3 className="text-lg font-semibold text-gray-800">{t('apAging.supplierBreakdown')}</h3>
                        </div>

                        {reportData.suppliers.length > 0 ? (
                            <div className="overflow-x-auto">
                                <table className="min-w-full divide-y divide-gray-200">
                                    <thead className="bg-gray-50">
                                        <tr>
                                            <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                                {t('apAging.supplierName')}
                                            </th>
                                            <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider print:hidden ${tableTextAlignClass}`}>
                                                {t('apAging.contact')}
                                            </th>
                                            <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('apAging.totalBalance')}
                                            </th>
                                            <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('apAging.current')}
                                            </th>
                                            <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('apAging.days1To30')}
                                            </th>
                                            <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('apAging.days31To60')}
                                            </th>
                                            <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('apAging.days61To90')}
                                            </th>
                                            <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('apAging.days90Plus')}
                                            </th>
                                            <th className="px-4 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('apAging.status')}
                                            </th>
                                        </tr>
                                    </thead>
                                    <tbody className="bg-white divide-y divide-gray-200">
                                        {reportData.suppliers.map((supplier, index) => (
                                            <tr key={index} className="hover:bg-gray-50">
                                                <td className="px-4 py-3 whitespace-nowrap">
                                                    <div className="font-medium text-gray-900">{supplier.supplierName}</div>
                                                </td>
                                                <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-600 print:hidden">
                                                    {supplier.phone && <div>📞 {supplier.phone}</div>}
                                                    {supplier.email && <div className="text-xs">✉️ {supplier.email}</div>}
                                                </td>
                                                <td className="px-4 py-3 whitespace-nowrap text-right font-bold text-gray-900">
                                                    {formatCurrency(supplier.totalBalance)}
                                                </td>
                                                <td className="px-4 py-3 whitespace-nowrap text-right text-sm text-green-600">
                                                    {supplier.current > 0 ? formatCurrency(supplier.current) : '-'}
                                                </td>
                                                <td className="px-4 py-3 whitespace-nowrap text-right text-sm text-yellow-600">
                                                    {supplier.days1To30 > 0 ? formatCurrency(supplier.days1To30) : '-'}
                                                </td>
                                                <td className="px-4 py-3 whitespace-nowrap text-right text-sm text-orange-600">
                                                    {supplier.days31To60 > 0 ? formatCurrency(supplier.days31To60) : '-'}
                                                </td>
                                                <td className="px-4 py-3 whitespace-nowrap text-right text-sm text-red-600">
                                                    {supplier.days61To90 > 0 ? formatCurrency(supplier.days61To90) : '-'}
                                                </td>
                                                <td className="px-4 py-3 whitespace-nowrap text-right text-sm font-bold text-red-800">
                                                    {supplier.days90Plus > 0 ? formatCurrency(supplier.days90Plus) : '-'}
                                                </td>
                                                <td className="px-4 py-3 whitespace-nowrap text-center">
                                                    <span className={`px-2 py-1 rounded-full text-xs font-medium ${getAgingBadgeColor(supplier.daysOverdue)}`}>
                                                        {supplier.daysOverdue > 0
                                                            ? `${supplier.daysOverdue} ${t('apAging.daysOverdue')}`
                                                            : t('apAging.current')}
                                                    </span>
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                    {/* Totals Row */}
                                    <tfoot className="bg-gray-100 font-bold">
                                        <tr>
                                            <td className="px-4 py-3 text-gray-900" colSpan="2">{t('common.total')}</td>
                                            <td className="px-4 py-3 text-right text-gray-900">{formatCurrency(reportData.totalOutstanding)}</td>
                                            <td className="px-4 py-3 text-right text-green-700">{formatCurrency(reportData.current)}</td>
                                            <td className="px-4 py-3 text-right text-yellow-700">{formatCurrency(reportData.days1To30)}</td>
                                            <td className="px-4 py-3 text-right text-orange-700">{formatCurrency(reportData.days31To60)}</td>
                                            <td className="px-4 py-3 text-right text-red-700">{formatCurrency(reportData.days61To90)}</td>
                                            <td className="px-4 py-3 text-right text-red-800">{formatCurrency(reportData.days90Plus)}</td>
                                            <td className="px-4 py-3"></td>
                                        </tr>
                                    </tfoot>
                                </table>
                            </div>
                        ) : (
                            <div className="text-center py-12 text-gray-500">
                                <div className="text-4xl mb-2">💰</div>
                                <div>{t('apAging.noSuppliers')}</div>
                            </div>
                        )}
                    </div>
                </div>
            )}

            {/* Empty State */}
            {!reportData && !loading && (
                <div className="bg-white rounded-xl shadow-md p-12 text-center">
                    <div className="text-6xl mb-4">📊</div>
                    <h3 className="text-xl font-semibold text-gray-700 mb-2">
                        {t('apAging.emptyState')}
                    </h3>
                    <p className="text-gray-500">
                        {t('apAging.emptyStateDesc')}
                    </p>
                </div>
            )}
        </div>
    )
}

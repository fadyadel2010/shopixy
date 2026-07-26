import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'
import { useToast } from '../components/Toast'
import { useSettings } from '../contexts/SettingsContext'
import { useRTL } from '../hooks/useRTL'
import { formatDate } from '../utils/dateFormat'

export default function AccountsReceivableAging() {
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

            const response = await api.get('/reports/accounts-receivable-aging', { params })
            setReportData(response.data)
        } catch (error) {
            console.error('Error generating report:', error)
            showError(t('arAging.errorGenerating'))
        } finally {
            setLoading(false)
        }
    }

    const handlePrint = () => {
        window.print()
    }

    const handleExportCSV = () => {
        if (!reportData || !reportData.customers.length) {
            showError(t('arAging.noDataToExport'))
            return
        }

        const headers = [
            t('arAging.customerName'),
            t('arAging.phone'),
            t('arAging.email'),
            t('arAging.totalBalance'),
            t('arAging.current'),
            t('arAging.days1To30'),
            t('arAging.days31To60'),
            t('arAging.days61To90'),
            t('arAging.days90Plus'),
            t('arAging.daysOverdue')
        ].join(',')

        const rows = reportData.customers.map(c => [
            c.customerName,
            c.phone || '-',
            c.email || '-',
            c.totalBalance.toFixed(2),
            c.current.toFixed(2),
            c.days1To30.toFixed(2),
            c.days31To60.toFixed(2),
            c.days61To90.toFixed(2),
            c.days90Plus.toFixed(2),
            c.daysOverdue
        ].join(','))

        const csv = [headers, ...rows].join('\n')
        const blob = new Blob([csv], { type: 'text/csv' })
        const url = window.URL.createObjectURL(blob)
        const a = document.createElement('a')
        a.href = url
        a.download = `ar-aging-${asOfDate}.csv`
        a.click()
        window.URL.revokeObjectURL(url)
        showSuccess(t('arAging.exportSuccess'))
    }

    const getAgingColor = (daysOverdue) => {
        if (daysOverdue <= 0) return 'text-green-600'
        if (daysOverdue <= 30) return 'text-yellow-600'
        if (daysOverdue <= 60) return 'text-orange-600'
        if (daysOverdue <= 90) return 'text-red-600'
        return 'text-red-800'
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
                    {t('arAging.title')}
                </h1>
                <p className="text-gray-600 print:hidden">{t('arAging.subtitle')}</p>
            </div>

            {/* Filters */}
            <div className="bg-white rounded-xl shadow-md p-6 mb-6 print:hidden">
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-4">
                    {/* As Of Date */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('arAging.asOfDate')}
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
                                {t('arAging.includeZeroBalance')}
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
                        {loading ? t('common.loading') : t('arAging.generateReport')}
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
                                {t('arAging.reportTitle')}
                            </h2>
                            <p className="text-gray-600">
                                {t('arAging.asOfDate')}: {formatDate(reportData.reportDate, locale)}
                            </p>
                        </div>

                        {/* Summary Cards */}
                        <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-4">
                            {/* Total Outstanding */}
                            <div className="bg-gradient-to-br from-indigo-50 to-indigo-100 rounded-lg p-4 border border-indigo-200">
                                <div className="text-xs text-indigo-600 mb-1 font-medium">{t('arAging.totalOutstanding')}</div>
                                <div className="text-xl font-bold text-indigo-900">{formatCurrency(reportData.totalOutstanding)}</div>
                                <div className="text-xs text-indigo-600 mt-1">100%</div>
                            </div>

                            {/* Current */}
                            <div className="bg-gradient-to-br from-green-50 to-green-100 rounded-lg p-4 border border-green-200">
                                <div className="text-xs text-green-600 mb-1 font-medium">{t('arAging.current')}</div>
                                <div className="text-xl font-bold text-green-900">{formatCurrency(reportData.current)}</div>
                                <div className="text-xs text-green-600 mt-1">
                                    {calculatePercentage(reportData.current, reportData.totalOutstanding)}%
                                </div>
                            </div>

                            {/* 1-30 Days */}
                            <div className="bg-gradient-to-br from-yellow-50 to-yellow-100 rounded-lg p-4 border border-yellow-200">
                                <div className="text-xs text-yellow-600 mb-1 font-medium">{t('arAging.days1To30')}</div>
                                <div className="text-xl font-bold text-yellow-900">{formatCurrency(reportData.days1To30)}</div>
                                <div className="text-xs text-yellow-600 mt-1">
                                    {calculatePercentage(reportData.days1To30, reportData.totalOutstanding)}%
                                </div>
                            </div>

                            {/* 31-60 Days */}
                            <div className="bg-gradient-to-br from-orange-50 to-orange-100 rounded-lg p-4 border border-orange-200">
                                <div className="text-xs text-orange-600 mb-1 font-medium">{t('arAging.days31To60')}</div>
                                <div className="text-xl font-bold text-orange-900">{formatCurrency(reportData.days31To60)}</div>
                                <div className="text-xs text-orange-600 mt-1">
                                    {calculatePercentage(reportData.days31To60, reportData.totalOutstanding)}%
                                </div>
                            </div>

                            {/* 61-90 Days */}
                            <div className="bg-gradient-to-br from-red-50 to-red-100 rounded-lg p-4 border border-red-200">
                                <div className="text-xs text-red-600 mb-1 font-medium">{t('arAging.days61To90')}</div>
                                <div className="text-xl font-bold text-red-900">{formatCurrency(reportData.days61To90)}</div>
                                <div className="text-xs text-red-600 mt-1">
                                    {calculatePercentage(reportData.days61To90, reportData.totalOutstanding)}%
                                </div>
                            </div>

                            {/* 90+ Days */}
                            <div className="bg-gradient-to-br from-red-100 to-red-200 rounded-lg p-4 border border-red-300">
                                <div className="text-xs text-red-700 mb-1 font-medium">{t('arAging.days90Plus')}</div>
                                <div className="text-xl font-bold text-red-900">{formatCurrency(reportData.days90Plus)}</div>
                                <div className="text-xs text-red-700 mt-1">
                                    {calculatePercentage(reportData.days90Plus, reportData.totalOutstanding)}%
                                </div>
                            </div>
                        </div>
                    </div>

                    {/* Customer Details Table */}
                    <div className="bg-white rounded-xl shadow-md overflow-hidden print:shadow-none">
                        <div className="px-6 py-4 bg-gray-50 border-b border-gray-200">
                            <h3 className="text-lg font-semibold text-gray-800">{t('arAging.customerBreakdown')}</h3>
                        </div>

                        {reportData.customers.length > 0 ? (
                            <div className="overflow-x-auto">
                                <table className="min-w-full divide-y divide-gray-200">
                                    <thead className="bg-gray-50">
                                        <tr>
                                            <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                                {t('arAging.customerName')}
                                            </th>
                                            <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider print:hidden ${tableTextAlignClass}`}>
                                                {t('arAging.contact')}
                                            </th>
                                            <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('arAging.totalBalance')}
                                            </th>
                                            <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('arAging.current')}
                                            </th>
                                            <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('arAging.days1To30')}
                                            </th>
                                            <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('arAging.days31To60')}
                                            </th>
                                            <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('arAging.days61To90')}
                                            </th>
                                            <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('arAging.days90Plus')}
                                            </th>
                                            <th className="px-4 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('arAging.status')}
                                            </th>
                                        </tr>
                                    </thead>
                                    <tbody className="bg-white divide-y divide-gray-200">
                                        {reportData.customers.map((customer, index) => (
                                            <tr key={index} className="hover:bg-gray-50">
                                                <td className="px-4 py-3 whitespace-nowrap">
                                                    <div className="font-medium text-gray-900">{customer.customerName}</div>
                                                </td>
                                                <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-600 print:hidden">
                                                    {customer.phone && <div>📞 {customer.phone}</div>}
                                                    {customer.email && <div className="text-xs">✉️ {customer.email}</div>}
                                                </td>
                                                <td className="px-4 py-3 whitespace-nowrap text-right font-bold text-gray-900">
                                                    {formatCurrency(customer.totalBalance)}
                                                </td>
                                                <td className="px-4 py-3 whitespace-nowrap text-right text-sm text-green-600">
                                                    {customer.current > 0 ? formatCurrency(customer.current) : '-'}
                                                </td>
                                                <td className="px-4 py-3 whitespace-nowrap text-right text-sm text-yellow-600">
                                                    {customer.days1To30 > 0 ? formatCurrency(customer.days1To30) : '-'}
                                                </td>
                                                <td className="px-4 py-3 whitespace-nowrap text-right text-sm text-orange-600">
                                                    {customer.days31To60 > 0 ? formatCurrency(customer.days31To60) : '-'}
                                                </td>
                                                <td className="px-4 py-3 whitespace-nowrap text-right text-sm text-red-600">
                                                    {customer.days61To90 > 0 ? formatCurrency(customer.days61To90) : '-'}
                                                </td>
                                                <td className="px-4 py-3 whitespace-nowrap text-right text-sm font-bold text-red-800">
                                                    {customer.days90Plus > 0 ? formatCurrency(customer.days90Plus) : '-'}
                                                </td>
                                                <td className="px-4 py-3 whitespace-nowrap text-center">
                                                    <span className={`px-2 py-1 rounded-full text-xs font-medium ${getAgingBadgeColor(customer.daysOverdue)}`}>
                                                        {customer.daysOverdue > 0
                                                            ? `${customer.daysOverdue} ${t('arAging.daysOverdue')}`
                                                            : t('arAging.current')}
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
                                <div>{t('arAging.noCustomers')}</div>
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
                        {t('arAging.emptyState')}
                    </h3>
                    <p className="text-gray-500">
                        {t('arAging.emptyStateDesc')}
                    </p>
                </div>
            )}
        </div>
    )
}

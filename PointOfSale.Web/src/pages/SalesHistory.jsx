import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import api from '../services/api'
import { useToast } from '../components/Toast'
import SaleDetailsModal from '../components/SaleDetailsModal'
import { useRTL } from '../hooks/useRTL'
import { formatDateTime } from '../utils/dateFormat'

export default function SalesHistory() {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()
    const { dir, isRTL, locale } = useRTL()
    const [sales, setSales] = useState([])
    const [loading, setLoading] = useState(true)
    const [page, setPage] = useState(1)
    const [totalPages, setTotalPages] = useState(1)
    const [search, setSearch] = useState('')
    const [startDate, setStartDate] = useState('')
    const [endDate, setEndDate] = useState('')
    const [selectedSale, setSelectedSale] = useState(null)

    const { showError } = useToast()

    useEffect(() => {
        fetchSales()
    }, [page, search, startDate, endDate])

    const fetchSales = async () => {
        setLoading(true)
        try {
            const params = {
                page,
                pageSize: 20,
                search: search || undefined,
                startDate: startDate || undefined,
                endDate: endDate || undefined
            }
            const response = await api.get('/sales', { params })
            setSales(response.data.items)
            setTotalPages(response.data.totalPages)
        } catch (error) {
            console.error('Error fetching sales:', error)
            showError(t('salesHistory.failedToLoad'))
        } finally {
            setLoading(false)
        }
    }

    const handleSearch = (e) => {
        e.preventDefault()
        setPage(1) // Reset to first page on new search
        fetchSales()
    }

    return (
        <div dir={dir} className="h-full flex flex-col">
            <div className="mb-6 flex justify-between items-center">
                <h1 className="text-2xl font-bold text-gray-800">{t('salesHistory.title')}</h1>
            </div>

            {/* Filters */}
            <div className="bg-white p-4 rounded-lg shadow mb-6">
                <form onSubmit={handleSearch} className="grid grid-cols-1 md:grid-cols-4 gap-4">
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">{t('salesHistory.searchLabel')}</label>
                        <input
                            type="text"
                            placeholder={t('salesHistory.searchPlaceholder')}
                            value={search}
                            onChange={(e) => setSearch(e.target.value)}
                            className="input w-full"
                        />
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">{t('salesHistory.startDate')}</label>
                        <input
                            type="date"
                            value={startDate}
                            onChange={(e) => setStartDate(e.target.value)}
                            className="input w-full"
                        />
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">{t('salesHistory.endDate')}</label>
                        <input
                            type="date"
                            value={endDate}
                            onChange={(e) => setEndDate(e.target.value)}
                            className="input w-full"
                        />
                    </div>
                    <div className="flex items-end">
                        <button type="submit" className="btn-primary w-full">
                            {t('common.filter')}
                        </button>
                    </div>
                </form>
            </div>

            {/* Sales Table */}
            <div className="bg-white rounded-lg shadow flex-1 overflow-hidden flex flex-col">
                <div className="overflow-x-auto flex-1">
                    <table className="w-full">
                        <thead className="bg-gray-50 border-b border-gray-200">
                            <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('common.date')}</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('salesHistory.invoiceNumber')}</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('pos.customer')}</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('common.total')}</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('common.status')}</th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">{t('common.actions')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {loading ? (
                                <tr>
                                    <td colSpan="6" className="px-6 py-4 text-center text-gray-500">{t('common.loading')}</td>
                                </tr>
                            ) : sales.length === 0 ? (
                                <tr>
                                    <td colSpan="6" className="px-6 py-4 text-center text-gray-500">{t('salesHistory.noSalesFound')}</td>
                                </tr>
                            ) : (
                                sales.map((sale) => (
                                    <tr key={sale.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            {formatDateTime(sale.invoiceDate, locale)}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                            {sale.invoiceNumber}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            {sale.customer?.name || t('salesHistory.walkInCustomer')}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                            {formatCurrency(sale.totalAmount)}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <span className={`px-2 inline-flex text-xs leading-5 font-semibold rounded-full ${sale.totalAmount < 0
                                                ? 'bg-red-100 text-red-800' // Return
                                                : 'bg-green-100 text-green-800' // Sale
                                                }`}>
                                                {sale.totalAmount < 0 ? t('salesHistory.return') : t('salesHistory.completed')}
                                            </span>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                                            <button
                                                onClick={() => setSelectedSale(sale)}
                                                className={`text-primary-600 hover:text-primary-900 ${isRTL ? 'ml-3' : 'mr-3'}`}
                                            >
                                                {t('common.view')}
                                            </button>
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>

                {/* Pagination */}
                <div className="bg-gray-50 px-4 py-3 border-t border-gray-200 flex items-center justify-between">
                    <div className="flex-1 flex justify-between sm:hidden">
                        <button
                            onClick={() => setPage(p => Math.max(1, p - 1))}
                            disabled={page === 1}
                            className="btn-secondary text-sm"
                        >
                            {t('salesHistory.previous')}
                        </button>
                        <button
                            onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                            disabled={page === totalPages}
                            className="btn-secondary text-sm"
                        >
                            {t('salesHistory.next')}
                        </button>
                    </div>
                    <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
                        <div>
                            <p className="text-sm text-gray-700">
                                {t('salesHistory.page')} <span className="font-medium">{page}</span> {t('salesHistory.of')} <span className="font-medium">{totalPages}</span>
                            </p>
                        </div>
                        <div>
                            <nav className="relative z-0 inline-flex rounded-md shadow-sm -space-x-px" aria-label="Pagination">
                                <button
                                    onClick={() => setPage(p => Math.max(1, p - 1))}
                                    disabled={page === 1}
                                    className="relative inline-flex items-center px-2 py-2 rounded-l-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:bg-gray-100"
                                >
                                    {t('salesHistory.previous')}
                                </button>
                                <button
                                    onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                                    disabled={page === totalPages}
                                    className="relative inline-flex items-center px-2 py-2 rounded-r-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:bg-gray-100"
                                >
                                    {t('salesHistory.next')}
                                </button>
                            </nav>
                        </div>
                    </div>
                </div>
            </div>

            {/* Modal placeholders */}
            {selectedSale && (
                <SaleDetailsModal
                    sale={selectedSale}
                    onClose={() => setSelectedSale(null)}
                    onReturnProcessed={() => {
                        fetchSales() // Refresh list to show return
                        setSelectedSale(null)
                    }}
                />
            )}
        </div>
    )
}

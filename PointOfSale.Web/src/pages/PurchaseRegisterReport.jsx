import { useState, useEffect, useRef, useCallback, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useReactToPrint } from 'react-to-print'
import api from '../services/api'
import { useSettings } from '../contexts/SettingsContext'
import ReportTruthBadge from '../components/ReportTruthBadge'
import { format } from 'date-fns'
import { createPdfDocument, loadExcelModule } from '../utils/reportExports'
import AsyncEntitySelect from '../components/AsyncEntitySelect'

export default function PurchaseRegisterReport() {
    const { t } = useTranslation()
    const componentRef = useRef()
    const { formatCurrency } = useSettings()

    const [loading, setLoading] = useState(false)
    const [data, setData] = useState(null)
    const [filters, setFilters] = useState({
        fromDate: new Date().toISOString().split('T')[0],
        toDate: new Date().toISOString().split('T')[0],
        supplierId: '',
        supplierName: '',
        warehouseId: '',
        categoryId: '',
        productId: '',
        productName: '',
        paymentMethod: '',
        posted: '' // '' = all, 'true' = posted only, 'false' = unposted only
    })

    // Metadata for dropdowns
    const [warehouses, setWarehouses] = useState([])
    const [categories, setCategories] = useState([])

    // Pagination
    const [currentPage, setCurrentPage] = useState(1)
    const itemsPerPage = 20

    // Modal state for viewing invoice details
    const [selectedInvoice, setSelectedInvoice] = useState(null)
    const [invoiceDetails, setInvoiceDetails] = useState(null)
    const [loadingDetails, setLoadingDetails] = useState(false)

    // PERFORMANCE: Async loaders instead of loading all with pageSize=1000
    const loadSuppliersAsync = useCallback(async ({ search, page, pageSize = 20 }) => {
        const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
        if (search) params.set('search', search)
        const response = await api.get(`/Suppliers?${params}`)
        return { items: response.data.items || [], hasMore: page < (response.data.totalPages || 1) }
    }, [])

    const loadProductsAsync = useCallback(async ({ search, page, pageSize = 20 }) => {
        const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
        if (search) params.set('search', search)
        const response = await api.get(`/Products?${params}`)
        return { items: response.data.items || [], hasMore: page < (response.data.totalPages || 1) }
    }, [])

    // PERFORMANCE: Memoized selected entities for display
    const selectedSupplier = useMemo(() =>
        filters.supplierId ? { id: filters.supplierId, name: filters.supplierName || '' } : null,
        [filters.supplierId, filters.supplierName]
    )
    const selectedProduct = useMemo(() =>
        filters.productId ? { id: filters.productId, name: filters.productName || '' } : null,
        [filters.productId, filters.productName]
    )

    useEffect(() => {
        fetchMetadata()
    }, [])

    const fetchMetadata = async () => {
        try {
            const [whRes, catRes] = await Promise.all([
                api.get('/warehouses'),
                api.get('/categories')
            ])
            setWarehouses(whRes.data)
            setCategories(catRes.data)
        } catch (error) {
            console.error('Failed to load metadata', error)
        }
    }

    const fetchReport = async () => {
        setLoading(true)
        setCurrentPage(1)
        try {
            const params = {
                fromDate: filters.fromDate,
                toDate: filters.toDate,
                supplierId: filters.supplierId || undefined,
                warehouseId: filters.warehouseId || undefined,
                categoryId: filters.categoryId || undefined,
                productId: filters.productId || undefined,
                paymentMethod: filters.paymentMethod || undefined,
                posted: filters.posted === '' ? undefined : filters.posted === 'true'
            }
            const response = await api.get('/reports/purchase-register', { params })
            setData(response.data)
        } catch (error) {
            console.error('Failed to load report', error)
        } finally {
            setLoading(false)
        }
    }

    const handleViewInvoice = async (invoiceId) => {
        setSelectedInvoice(invoiceId)
        setLoadingDetails(true)
        try {
            const response = await api.get(`/purchases/${invoiceId}`)
            setInvoiceDetails(response.data)
        } catch (error) {
            console.error('Failed to load invoice details', error)
        } finally {
            setLoadingDetails(false)
        }
    }

    const closeModal = () => {
        setSelectedInvoice(null)
        setInvoiceDetails(null)
    }

    const handlePrint = useReactToPrint({
        content: () => componentRef.current,
        documentTitle: t('purchaseRegister.title')
    })

    const handleExportExcel = async () => {
        if (!data) return
        const XLSX = await loadExcelModule()

        const wb = XLSX.utils.book_new()

        // Summary Sheet
        const summaryRows = [
            [t('purchaseRegister.totalGrossPurchases'), data.summary.totalGrossPurchases],
            [t('purchaseRegister.totalDiscount'), data.summary.totalDiscount],
            [t('purchaseRegister.totalNetPurchases'), data.summary.totalNetPurchases],
            [t('purchaseRegister.totalTax'), data.summary.totalTax],
            [t('purchaseRegister.totalFinalAmount'), data.summary.totalFinalAmount],
            [t('purchaseRegister.totalCashPurchases'), data.summary.totalCashPurchases],
            [t('purchaseRegister.totalCreditPurchases'), data.summary.totalCreditPurchases],
            [t('purchaseRegister.totalBankPurchases'), data.summary.totalBankPurchases],
            [t('purchaseRegister.totalVisaPurchases'), data.summary.totalVisaPurchases],
            [t('purchaseRegister.totalReturnsValue'), data.summary.totalReturnsValue],
            [t('purchaseRegister.totalInvoiceCount'), data.summary.totalInvoiceCount],
            [t('purchaseRegister.supplierWithHighestVolume'), data.summary.supplierWithHighestVolume]
        ]
        const wsSummary = XLSX.utils.aoa_to_sheet(summaryRows)
        XLSX.utils.book_append_sheet(wb, wsSummary, "Summary")

        // Items Sheet
        const itemsHeader = [
            t('purchaseRegister.invoiceNo'),
            t('purchaseRegister.date'),
            t('purchaseRegister.supplier'),
            t('purchaseRegister.supplierCode'),
            t('purchaseRegister.paymentMethod'),
            t('purchaseRegister.itemsCount'),
            t('purchaseRegister.grossAmount'),
            t('purchaseRegister.discount'),
            t('purchaseRegister.netAmount'),
            t('purchaseRegister.tax'),
            t('purchaseRegister.total'),
            t('purchaseRegister.status'),
            t('purchaseRegister.warehouse'),
            t('purchaseRegister.linkedReturn')
        ]

        const itemsData = data.items.map(item => [
            item.invoiceNumber,
            format(new Date(item.invoiceDate), 'yyyy-MM-dd'),
            item.supplierName,
            item.supplierCode,
            item.paymentMethod,
            item.itemsCount,
            item.grossAmount,
            item.discountAmount,
            item.netAmount,
            item.taxAmount,
            item.totalAmount,
            item.status,
            item.warehouseName,
            item.linkedReturnNumber || '-'
        ])

        const wsItems = XLSX.utils.aoa_to_sheet([itemsHeader, ...itemsData])
        XLSX.utils.book_append_sheet(wb, wsItems, "Invoices")

        XLSX.writeFile(wb, "PurchaseRegisterReport.xlsx")
    }

    const handleExportPDF = async () => {
        if (!data) return
        const doc = await createPdfDocument('landscape')
        doc.text(t('purchaseRegister.title'), 14, 15)
        doc.setFontSize(10)
        doc.text(`${filters.fromDate} - ${filters.toDate}`, 14, 22)

        // Summary
        doc.autoTable({
            startY: 30,
            head: [['Metric', 'Value']],
            body: [
                [t('purchaseRegister.totalNetPurchases'), formatCurrency(data.summary.totalNetPurchases)],
                [t('purchaseRegister.totalCashPurchases'), formatCurrency(data.summary.totalCashPurchases)],
                [t('purchaseRegister.totalCreditPurchases'), formatCurrency(data.summary.totalCreditPurchases)],
                [t('purchaseRegister.totalReturnsValue'), formatCurrency(data.summary.totalReturnsValue)],
                [t('purchaseRegister.supplierWithHighestVolume'), data.summary.supplierWithHighestVolume]
            ],
            theme: 'striped',
            headStyles: { fillColor: [79, 70, 229] }
        })

        // Details
        doc.addPage('landscape')
        doc.text(t('purchaseRegister.subtitle'), 14, 15)

        const tableColumn = [
            t('purchaseRegister.invoiceNo'),
            t('purchaseRegister.date'),
            t('purchaseRegister.supplier'),
            t('purchaseRegister.paymentMethod'),
            t('purchaseRegister.gross'),
            t('purchaseRegister.net'),
            t('purchaseRegister.total'),
            t('purchaseRegister.status')
        ]
        const tableRows = data.items.map(item => [
            item.invoiceNumber,
            format(new Date(item.invoiceDate), 'yyyy-MM-dd'),
            item.supplierName,
            item.paymentMethod,
            formatCurrency(item.grossAmount),
            formatCurrency(item.netAmount),
            formatCurrency(item.totalAmount),
            item.status
        ])

        doc.autoTable({
            head: [tableColumn],
            body: tableRows,
            startY: 25,
            theme: 'grid',
            styles: { fontSize: 8 },
            headStyles: { fillColor: [79, 70, 229] }
        })

        doc.save("PurchaseRegisterReport.pdf")
    }

    // Pagination logic
    const paginatedItems = data?.items?.slice((currentPage - 1) * itemsPerPage, currentPage * itemsPerPage) || []
    const totalPages = data ? Math.ceil(data.items.length / itemsPerPage) : 0

    return (
        <div className="space-y-6">
            {/* Phase C4: UI Truth Label */}
            <ReportTruthBadge type="operational" />

            {/* Header */}
            <div className="flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-800">{t('purchaseRegister.title')}</h1>
                    <p className="text-gray-500">{t('purchaseRegister.subtitle')}</p>
                </div>
                <div className="flex gap-2">
                    <button
                        onClick={handleExportExcel}
                        disabled={!data || loading}
                        className="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50 flex items-center gap-2"
                    >
                        <span>📊</span> {t('common.exportExcel')}
                    </button>
                    <button
                        onClick={handlePrint}
                        disabled={!data || loading}
                        className="px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
                    >
                        <span>🖨️</span> {t('common.print')}
                    </button>
                    <button
                        onClick={handleExportPDF}
                        disabled={!data || loading}
                        className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:opacity-50 flex items-center gap-2"
                    >
                        <span>📄</span> {t('common.exportPDF')}
                    </button>
                </div>
            </div>

            {/* Filters */}
            <div className="bg-white p-4 rounded-xl shadow-sm border border-gray-100 grid grid-cols-1 md:grid-cols-4 gap-4">
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('common.from')}</label>
                    <input
                        type="date"
                        value={filters.fromDate}
                        onChange={(e) => setFilters(prev => ({ ...prev, fromDate: e.target.value }))}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('common.to')}</label>
                    <input
                        type="date"
                        value={filters.toDate}
                        onChange={(e) => setFilters(prev => ({ ...prev, toDate: e.target.value }))}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('purchaseRegister.supplier')}</label>
                    <AsyncEntitySelect
                        value={selectedSupplier}
                        onChange={(s) => setFilters(prev => ({ 
                            ...prev, 
                            supplierId: s?.id || '',
                            supplierName: s?.name || ''
                        }))}
                        loadOptions={loadSuppliersAsync}
                        placeholder={t('common.all')}
                        className="w-full"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('dashboard.warehouse')}</label>
                    <select
                        value={filters.warehouseId}
                        onChange={(e) => setFilters(prev => ({ ...prev, warehouseId: e.target.value }))}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        {warehouses.map(w => (
                            <option key={w.id} value={w.id}>{w.name}</option>
                        ))}
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('nav.mainSubCategories')}</label>
                    <select
                        value={filters.categoryId}
                        onChange={(e) => setFilters(prev => ({ ...prev, categoryId: e.target.value }))}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        {categories.map(c => (
                            <option key={c.id} value={c.id}>{c.name}</option>
                        ))}
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('purchaseRegister.product')}</label>
                    <AsyncEntitySelect
                        value={selectedProduct}
                        onChange={(p) => setFilters(prev => ({ 
                            ...prev, 
                            productId: p?.id || '',
                            productName: p?.name || ''
                        }))}
                        loadOptions={loadProductsAsync}
                        placeholder={t('common.all')}
                        className="w-full"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('purchaseRegister.paymentMethod')}</label>
                    <select
                        value={filters.paymentMethod}
                        onChange={(e) => setFilters(prev => ({ ...prev, paymentMethod: e.target.value }))}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        <option value="Cash">{t('purchases.cash')}</option>
                        <option value="Visa">{t('pos.card')}</option>
                        <option value="Bank">{t('purchases.bankTransfer')}</option>
                        <option value="Credit">{t('purchases.credit')}</option>
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('purchaseRegister.postingStatus')}</label>
                    <select
                        value={filters.posted}
                        onChange={(e) => setFilters(prev => ({ ...prev, posted: e.target.value }))}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        <option value="true">{t('purchaseRegister.posted')}</option>
                        <option value="false">{t('purchaseRegister.draft')}</option>
                    </select>
                </div>
            </div>

            {/* Generate Button */}
            <div className="flex justify-end">
                <button
                    onClick={fetchReport}
                    disabled={loading}
                    className="px-6 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
                >
                    {loading ? t('common.processing') : t('purchaseRegister.generate')}
                </button>
            </div>

            {/* Report Content */}
            <div ref={componentRef} className="space-y-6 print:p-8">
                {/* Print Header */}
                <div className="hidden print:block text-center mb-8">
                    <h1 className="text-2xl font-bold">{t('purchaseRegister.title')}</h1>
                    <p className="text-gray-500">{filters.fromDate} - {filters.toDate}</p>
                </div>

                {data && (
                    <>
                        {/* KPI Cards */}
                        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-4">
                            <KpiCard
                                title={t('purchaseRegister.totalNetPurchases')}
                                value={formatCurrency(data.summary.totalNetPurchases)}
                                color="text-indigo-600"
                                icon="📦"
                            />
                            <KpiCard
                                title={t('purchaseRegister.totalReturnsValue')}
                                value={formatCurrency(data.summary.totalReturnsValue)}
                                color="text-red-600"
                                icon="↩️"
                            />
                            <KpiCard
                                title={t('purchaseRegister.totalCashPurchases')}
                                value={formatCurrency(data.summary.totalCashPurchases)}
                                color="text-green-600"
                                icon="💵"
                            />
                            <KpiCard
                                title={t('purchaseRegister.totalCreditPurchases')}
                                value={formatCurrency(data.summary.totalCreditPurchases)}
                                color="text-orange-600"
                                icon="📋"
                            />
                            <KpiCard
                                title={t('purchaseRegister.totalInvoiceCount')}
                                value={data.summary.totalInvoiceCount}
                                color="text-blue-600"
                                icon="📑"
                            />
                        </div>

                        {/* Top Supplier */}
                        <div className="bg-gradient-to-r from-indigo-500 to-purple-600 p-4 rounded-xl shadow-sm text-white">
                            <div className="flex items-center justify-between">
                                <div>
                                    <h3 className="text-sm font-medium opacity-80">{t('purchaseRegister.supplierWithHighestVolume')}</h3>
                                    <p className="text-2xl font-bold mt-1">{data.summary.supplierWithHighestVolume || '-'}</p>
                                </div>
                                <span className="text-4xl">🏆</span>
                            </div>
                        </div>

                        {/* Table */}
                        <div className="bg-white rounded-xl shadow-sm border border-gray-100 overflow-hidden">
                            <div className="overflow-x-auto">
                                <table className="w-full text-sm text-left">
                                    <thead className="text-xs text-gray-700 uppercase bg-gray-50">
                                        <tr>
                                            <th className="px-4 py-3">{t('purchaseRegister.invoiceNo')}</th>
                                            <th className="px-4 py-3">{t('purchaseRegister.date')}</th>
                                            <th className="px-4 py-3">{t('purchaseRegister.supplier')}</th>
                                            <th className="px-4 py-3">{t('purchaseRegister.paymentMethod')}</th>
                                            <th className="px-4 py-3 text-center">{t('purchaseRegister.itemsCount')}</th>
                                            <th className="px-4 py-3 text-right">{t('purchaseRegister.grossAmount')}</th>
                                            <th className="px-4 py-3 text-right">{t('purchaseRegister.discount')}</th>
                                            <th className="px-4 py-3 text-right">{t('purchaseRegister.netAmount')}</th>
                                            <th className="px-4 py-3 text-right">{t('purchaseRegister.tax')}</th>
                                            <th className="px-4 py-3 text-right">{t('purchaseRegister.total')}</th>
                                            <th className="px-4 py-3">{t('purchaseRegister.status')}</th>
                                            <th className="px-4 py-3">{t('purchaseRegister.linkedReturn')}</th>
                                            <th className="px-4 py-3 print:hidden">{t('purchaseRegister.view')}</th>
                                        </tr>
                                    </thead>
                                    <tbody className="divide-y divide-gray-200">
                                        {paginatedItems.map((item, index) => (
                                            <tr key={index} className="hover:bg-gray-50">
                                                <td className="px-4 py-3 font-medium text-indigo-600">{item.invoiceNumber}</td>
                                                <td className="px-4 py-3">{format(new Date(item.invoiceDate), 'MMM dd, yyyy')}</td>
                                                <td className="px-4 py-3">
                                                    <div>{item.supplierName}</div>
                                                    <div className="text-xs text-gray-500">{item.supplierCode}</div>
                                                </td>
                                                <td className="px-4 py-3">
                                                    <span className={`px-2 py-1 rounded-full text-xs font-medium ${item.paymentMethod === 'Cash' ? 'bg-green-100 text-green-800' :
                                                        item.paymentMethod === 'Credit' ? 'bg-orange-100 text-orange-800' :
                                                            item.paymentMethod === 'Bank' || item.paymentMethod === 'BankTransfer' ? 'bg-blue-100 text-blue-800' :
                                                                'bg-purple-100 text-purple-800'
                                                        }`}>
                                                        {item.paymentMethod}
                                                    </span>
                                                </td>
                                                <td className="px-4 py-3 text-center">{item.itemsCount}</td>
                                                <td className="px-4 py-3 text-right">{formatCurrency(item.grossAmount)}</td>
                                                <td className="px-4 py-3 text-right text-red-500">
                                                    {item.discountAmount > 0 ? `-${formatCurrency(item.discountAmount)}` : '-'}
                                                </td>
                                                <td className="px-4 py-3 text-right">{formatCurrency(item.netAmount)}</td>
                                                <td className="px-4 py-3 text-right">{formatCurrency(item.taxAmount)}</td>
                                                <td className="px-4 py-3 text-right font-bold">{formatCurrency(item.totalAmount)}</td>
                                                <td className="px-4 py-3">
                                                    <span className={`px-2 py-1 rounded-full text-xs font-medium ${item.isPosted ? 'bg-green-100 text-green-800' : 'bg-yellow-100 text-yellow-800'
                                                        }`}>
                                                        {item.status}
                                                    </span>
                                                </td>
                                                <td className="px-4 py-3">
                                                    {item.linkedReturnNumber ? (
                                                        <div className="text-xs">
                                                            <span className="text-red-600 font-medium">{item.linkedReturnNumber}</span>
                                                            <div className="text-gray-500">{formatCurrency(item.linkedReturnAmount || 0)}</div>
                                                        </div>
                                                    ) : (
                                                        <span className="text-gray-400">-</span>
                                                    )}
                                                </td>
                                                <td className="px-4 py-3 print:hidden">
                                                    <button
                                                        onClick={() => handleViewInvoice(item.invoiceId)}
                                                        className="text-indigo-600 hover:text-indigo-800 text-sm font-medium"
                                                    >
                                                        {t('purchaseRegister.viewDetails')}
                                                    </button>
                                                </td>
                                            </tr>
                                        ))}
                                        {paginatedItems.length === 0 && (
                                            <tr>
                                                <td colSpan="13" className="px-4 py-8 text-center text-gray-500">
                                                    {t('common.noData')}
                                                </td>
                                            </tr>
                                        )}
                                    </tbody>
                                    <tfoot className="bg-gray-50 font-bold">
                                        <tr>
                                            <td colSpan="5" className="px-4 py-3 text-right">{t('common.total')}</td>
                                            <td className="px-4 py-3 text-right">{formatCurrency(data.summary.totalGrossPurchases)}</td>
                                            <td className="px-4 py-3 text-right text-red-600">{formatCurrency(data.summary.totalDiscount)}</td>
                                            <td className="px-4 py-3 text-right">{formatCurrency(data.summary.totalNetPurchases)}</td>
                                            <td className="px-4 py-3 text-right">{formatCurrency(data.summary.totalTax)}</td>
                                            <td className="px-4 py-3 text-right">{formatCurrency(data.summary.totalFinalAmount)}</td>
                                            <td colSpan="3"></td>
                                        </tr>
                                    </tfoot>
                                </table>
                            </div>

                            {/* Pagination */}
                            {totalPages > 1 && (
                                <div className="px-4 py-3 border-t flex items-center justify-between print:hidden">
                                    <div className="text-sm text-gray-500">
                                        {t('common.showing')} {((currentPage - 1) * itemsPerPage) + 1} - {Math.min(currentPage * itemsPerPage, data.items.length)} {t('common.of')} {data.items.length}
                                    </div>
                                    <div className="flex gap-2">
                                        <button
                                            onClick={() => setCurrentPage(p => Math.max(1, p - 1))}
                                            disabled={currentPage === 1}
                                            className="px-3 py-1 border rounded hover:bg-gray-50 disabled:opacity-50"
                                        >
                                            {t('common.previous')}
                                        </button>
                                        <span className="px-3 py-1 text-sm">
                                            {currentPage} / {totalPages}
                                        </span>
                                        <button
                                            onClick={() => setCurrentPage(p => Math.min(totalPages, p + 1))}
                                            disabled={currentPage === totalPages}
                                            className="px-3 py-1 border rounded hover:bg-gray-50 disabled:opacity-50"
                                        >
                                            {t('common.next')}
                                        </button>
                                    </div>
                                </div>
                            )}
                        </div>
                    </>
                )}

                {/* Empty State */}
                {!data && !loading && (
                    <div className="text-center py-12 bg-white rounded-xl shadow-sm border border-gray-100">
                        <span className="text-4xl">📦</span>
                        <h3 className="mt-2 text-sm font-medium text-gray-900">{t('purchaseRegister.startPrompt')}</h3>
                        <p className="mt-1 text-sm text-gray-500">{t('purchaseRegister.startPromptDesc')}</p>
                    </div>
                )}
            </div>

            {/* Invoice Details Modal */}
            {selectedInvoice && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-xl shadow-xl w-full max-w-4xl max-h-[90vh] overflow-y-auto">
                        <div className="sticky top-0 bg-white border-b px-6 py-4 flex items-center justify-between">
                            <h2 className="text-xl font-bold">{t('purchaseRegister.invoiceDetails')}</h2>
                            <button
                                onClick={closeModal}
                                className="text-gray-500 hover:text-gray-700 text-2xl"
                            >
                                ×
                            </button>
                        </div>
                        <div className="p-6">
                            {loadingDetails ? (
                                <div className="text-center py-8">
                                    <div className="animate-spin w-8 h-8 border-4 border-indigo-500 border-t-transparent rounded-full mx-auto"></div>
                                </div>
                            ) : invoiceDetails ? (
                                <div className="space-y-6">
                                    {/* Invoice Header */}
                                    <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                                        <div>
                                            <label className="text-sm text-gray-500">{t('purchaseRegister.invoiceNo')}</label>
                                            <p className="font-bold text-indigo-600">{invoiceDetails.invoiceNumber}</p>
                                        </div>
                                        <div>
                                            <label className="text-sm text-gray-500">{t('purchaseRegister.date')}</label>
                                            <p className="font-medium">{format(new Date(invoiceDetails.invoiceDate), 'MMM dd, yyyy')}</p>
                                        </div>
                                        <div>
                                            <label className="text-sm text-gray-500">{t('purchaseRegister.supplier')}</label>
                                            <p className="font-medium">{invoiceDetails.supplier?.name}</p>
                                        </div>
                                        <div>
                                            <label className="text-sm text-gray-500">{t('purchaseRegister.warehouse')}</label>
                                            <p className="font-medium">{invoiceDetails.warehouse?.name}</p>
                                        </div>
                                    </div>

                                    {/* Items Table */}
                                    <div className="border rounded-lg overflow-hidden">
                                        <table className="w-full text-sm">
                                            <thead className="bg-gray-50">
                                                <tr>
                                                    <th className="px-4 py-2 text-left">{t('purchaseRegister.product')}</th>
                                                    <th className="px-4 py-2 text-right">{t('purchaseRegister.qty')}</th>
                                                    <th className="px-4 py-2 text-right">{t('purchaseRegister.unitPrice')}</th>
                                                    <th className="px-4 py-2 text-right">{t('purchaseRegister.tax')}</th>
                                                    <th className="px-4 py-2 text-right">{t('purchaseRegister.total')}</th>
                                                </tr>
                                            </thead>
                                            <tbody className="divide-y">
                                                {invoiceDetails.items?.map((item, idx) => (
                                                    <tr key={idx}>
                                                        <td className="px-4 py-2">{item.product?.name || 'Unknown'}</td>
                                                        <td className="px-4 py-2 text-right">{item.quantity}</td>
                                                        <td className="px-4 py-2 text-right">{formatCurrency(item.unitPrice)}</td>
                                                        <td className="px-4 py-2 text-right">{formatCurrency(item.taxAmount)}</td>
                                                        <td className="px-4 py-2 text-right font-medium">{formatCurrency(item.totalAmount)}</td>
                                                    </tr>
                                                ))}
                                            </tbody>
                                        </table>
                                    </div>

                                    {/* Totals */}
                                    <div className="bg-gray-50 rounded-lg p-4 space-y-2">
                                        <div className="flex justify-between">
                                            <span>{t('purchaseRegister.subtotal')}</span>
                                            <span>{formatCurrency(invoiceDetails.subtotal)}</span>
                                        </div>
                                        <div className="flex justify-between text-red-600">
                                            <span>{t('purchaseRegister.discount')}</span>
                                            <span>-{formatCurrency(invoiceDetails.discountAmount)}</span>
                                        </div>
                                        <div className="flex justify-between">
                                            <span>{t('purchaseRegister.tax')}</span>
                                            <span>{formatCurrency(invoiceDetails.taxAmount)}</span>
                                        </div>
                                        <div className="flex justify-between font-bold text-lg border-t pt-2">
                                            <span>{t('purchaseRegister.total')}</span>
                                            <span>{formatCurrency(invoiceDetails.totalAmount)}</span>
                                        </div>
                                    </div>
                                </div>
                            ) : (
                                <div className="text-center py-8 text-gray-500">
                                    {t('common.error')}
                                </div>
                            )}
                        </div>
                    </div>
                </div>
            )}
        </div>
    )
}

function KpiCard({ title, value, color, icon }) {
    return (
        <div className="bg-white p-4 rounded-xl shadow-sm border border-gray-100">
            <div className="flex items-center justify-between">
                <div>
                    <h3 className="text-sm font-medium text-gray-500">{title}</h3>
                    <p className={`text-2xl font-bold mt-1 ${color}`}>{value}</p>
                </div>
                <span className="text-2xl">{icon}</span>
            </div>
        </div>
    )
}

import { useState, useEffect, useRef, useCallback, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useReactToPrint } from 'react-to-print'
import api from '../services/api'
import { useSettings } from '../contexts/SettingsContext'
import ReportTruthBadge from '../components/ReportTruthBadge'
import { format } from 'date-fns'
import { createPdfDocument, loadExcelModule } from '../utils/reportExports'
import AsyncEntitySelect from '../components/AsyncEntitySelect'

export default function SalesReturnReport() {
    const { t } = useTranslation()
    const componentRef = useRef()
    const { formatCurrency } = useSettings()

    const [loading, setLoading] = useState(false)
    const [data, setData] = useState(null)
    const [filters, setFilters] = useState({
        fromDate: new Date().toISOString().split('T')[0],
        toDate: new Date().toISOString().split('T')[0],
        warehouseId: '',
        categoryId: '',
        salesRepresentativeId: '',
        salesRepName: '',
        paymentMethod: '',
        customerId: '',
        customerName: '',
        productId: '',
        productName: ''
    })

    const [warehouses, setWarehouses] = useState([])
    const [categories, setCategories] = useState([])

    // PERFORMANCE: Async loaders instead of loading all with pageSize=1000
    const loadSalesRepsAsync = useCallback(async ({ search, page, pageSize = 20 }) => {
        const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
        if (search) params.set('search', search)
        const response = await api.get(`/SalesRepresentatives?${params}`)
        return { items: response.data.items || [], hasMore: page < (response.data.totalPages || 1) }
    }, [])

    const loadCustomersAsync = useCallback(async ({ search, page, pageSize = 20 }) => {
        const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
        if (search) params.set('search', search)
        const response = await api.get(`/Customers?${params}`)
        return { items: response.data.items || [], hasMore: page < (response.data.totalPages || 1) }
    }, [])

    const loadProductsAsync = useCallback(async ({ search, page, pageSize = 20 }) => {
        const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
        if (search) params.set('search', search)
        const response = await api.get(`/Products?${params}`)
        return { items: response.data.items || [], hasMore: page < (response.data.totalPages || 1) }
    }, [])

    // PERFORMANCE: Memoized selected entities for display
    const selectedSalesRep = useMemo(() =>
        filters.salesRepresentativeId ? { id: filters.salesRepresentativeId, name: filters.salesRepName || '' } : null,
        [filters.salesRepresentativeId, filters.salesRepName]
    )
    const selectedCustomer = useMemo(() =>
        filters.customerId ? { id: filters.customerId, name: filters.customerName || '' } : null,
        [filters.customerId, filters.customerName]
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
            const [wRes, cRes] = await Promise.all([
                api.get('/warehouses'),
                api.get('/categories')
            ])
            setWarehouses(wRes.data)
            setCategories(cRes.data)
        } catch (error) {
            console.error('Failed to load metadata', error)
        }
    }

    const fetchReport = async () => {
        setLoading(true)
        try {
            const params = {
                fromDate: filters.fromDate,
                toDate: filters.toDate,
                warehouseId: filters.warehouseId || undefined,
                categoryId: filters.categoryId || undefined,
                salesRepresentativeId: filters.salesRepresentativeId || undefined,
                paymentMethod: filters.paymentMethod || undefined,
                customerId: filters.customerId || undefined,
                productId: filters.productId || undefined
            }
            const response = await api.get('/reports/sales-returns', { params })
            setData(response.data)
        } catch (error) {
            console.error('Failed to load report', error)
        } finally {
            setLoading(false)
        }
    }

    const handlePrint = useReactToPrint({
        content: () => componentRef.current,
        documentTitle: t('salesReturnReport.title')
    })

    const handleExportExcel = async () => {
        if (!data) return
        const XLSX = await loadExcelModule()

        const wb = XLSX.utils.book_new()

        // Summary Sheet
        const summaryRows = [
            [t('salesReturnReport.totalReturnsAmount'), data.summary.totalReturnsAmount],
            [t('salesReturnReport.totalCostReversed'), data.summary.totalCostReversed],
            [t('salesReturnReport.netFinancialImpact'), data.summary.netFinancialImpact],
            [t('salesReturnReport.totalQuantityReturned'), data.summary.totalQuantityReturned],
            [t('salesReturnReport.mostReturnedProduct'), data.summary.mostReturnedProduct],
            [t('salesReturnReport.customerWithMostReturns'), data.summary.customerWithMostReturns]
        ]
        const wsSummary = XLSX.utils.aoa_to_sheet(summaryRows)
        XLSX.utils.book_append_sheet(wb, wsSummary, "Summary")

        // Items Sheet
        const itemsHeader = [
            t('salesReturnReport.returnNo'),
            t('salesReturnReport.originalInvoiceNo'),
            t('salesReturnReport.returnDate'),
            t('salesReturnReport.customer'),
            t('salesReturnReport.product'),
            t('salesReturnReport.unit'),
            t('salesReturnReport.qtyReturned'),
            t('salesReturnReport.grossReturn'),
            t('salesReturnReport.discount'),
            t('salesReturnReport.netReturn'),
            t('salesReturnReport.costReversed'),
            t('salesReturnReport.profitImpact'),
            t('salesReturnReport.paymentMethod'),
            t('salesReturnReport.user'),
            t('salesReturnReport.reason')
        ]

        const itemsData = data.items.map(item => [
            item.returnNumber,
            item.originalInvoiceNumber,
            format(new Date(item.returnDate), 'yyyy-MM-dd HH:mm'),
            item.customerName,
            item.productName,
            item.unit,
            item.quantityReturned,
            item.grossReturnAmount,
            item.discountReturned,
            item.netReturnAmount,
            item.costOfReturnedItems,
            item.profitImpact,
            item.paymentMethod,
            item.user,
            item.reason
        ])

        const wsItems = XLSX.utils.aoa_to_sheet([itemsHeader, ...itemsData])
        XLSX.utils.book_append_sheet(wb, wsItems, "Returns Detail")

        XLSX.writeFile(wb, "SalesReturnReport.xlsx")
    }

    const handleExportPDF = async () => {
        if (!data) return
        const doc = await createPdfDocument()
        doc.text(t('salesReturnReport.title'), 14, 15)
        doc.setFontSize(10)
        doc.text(`${filters.fromDate} - ${filters.toDate}`, 14, 22)

        // Summary
        doc.autoTable({
            startY: 30,
            head: [['Metric', 'Value']],
            body: [
                [t('salesReturnReport.totalReturnsAmount'), formatCurrency(data.summary.totalReturnsAmount)],
                [t('salesReturnReport.totalCostReversed'), formatCurrency(data.summary.totalCostReversed)],
                [t('salesReturnReport.netFinancialImpact'), formatCurrency(data.summary.netFinancialImpact)],
                [t('salesReturnReport.totalQuantityReturned'), data.summary.totalQuantityReturned],
                [t('salesReturnReport.mostReturnedProduct'), data.summary.mostReturnedProduct],
                [t('salesReturnReport.customerWithMostReturns'), data.summary.customerWithMostReturns]
            ],
            theme: 'striped',
            headStyles: { fillColor: [79, 70, 229] }
        })

        // Details
        doc.addPage()
        doc.text(t('salesReturnReport.subtitle'), 14, 15)

        const tableColumn = [
            t('salesReturnReport.returnNo'),
            t('salesReturnReport.date'),
            t('salesReturnReport.product'),
            t('salesReturnReport.qtyReturned'),
            t('salesReturnReport.netReturn')
        ];
        const tableRows = [];

        data.items.forEach(item => {
            const rowData = [
                item.returnNumber,
                format(new Date(item.returnDate), 'yyyy-MM-dd'),
                item.productName,
                item.quantityReturned,
                formatCurrency(item.netReturnAmount)
            ];
            tableRows.push(rowData);
        });

        doc.autoTable({
            head: [tableColumn],
            body: tableRows,
            startY: 25,
            theme: 'grid',
            styles: { fontSize: 8 },
            headStyles: { fillColor: [79, 70, 229] }
        });

        doc.save("SalesReturnReport.pdf");
    }

    return (
        <div className="space-y-6">
            {/* Phase C4: UI Truth Label */}
            <ReportTruthBadge type="operational" />

            <div className="flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-800">{t('salesReturnReport.title')}</h1>
                    <p className="text-gray-500">{t('salesReturnReport.subtitle')}</p>
                </div>
                <div className="flex gap-2">
                    <button
                        onClick={handleExportExcel}
                        disabled={!data || loading}
                        className="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50"
                    >
                        {t('salesReturnReport.exportExcel')}
                    </button>
                    {/* Reuse print component for simple print, or PDF for complex */}
                    <button
                        onClick={handlePrint}
                        disabled={!data || loading}
                        className="px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:opacity-50"
                    >
                        {t('common.print')}
                    </button>
                    {/* PDF Export Button */}
                    <button
                        onClick={handleExportPDF}
                        disabled={!data || loading}
                        className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:opacity-50"
                    >
                        {t('salesReturnReport.exportPDF')}
                    </button>
                </div>
            </div>

            {/* Filters */}
            <div className="bg-white p-4 rounded-xl shadow-sm border border-gray-100 grid grid-cols-1 md:grid-cols-4 lg:grid-cols-4 gap-4">
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
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('salesReturnReport.customer')}</label>
                    <AsyncEntitySelect
                        value={selectedCustomer}
                        onChange={(c) => setFilters(prev => ({ 
                            ...prev, 
                            customerId: c?.id || '',
                            customerName: c?.name || ''
                        }))}
                        loadOptions={loadCustomersAsync}
                        placeholder={t('common.all')}
                        className="w-full"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('salesReturnReport.product')}</label>
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
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('sales.representatives')}</label>
                    <AsyncEntitySelect
                        value={selectedSalesRep}
                        onChange={(s) => setFilters(prev => ({ 
                            ...prev, 
                            salesRepresentativeId: s?.id || '',
                            salesRepName: s?.name || ''
                        }))}
                        loadOptions={loadSalesRepsAsync}
                        placeholder={t('common.all')}
                        className="w-full"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('salesReturnReport.paymentMethod')}</label>
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
            </div>
            <div className="flex justify-end">
                <button
                    onClick={fetchReport}
                    disabled={loading}
                    className="px-6 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
                >
                    {loading ? t('common.processing') : t('salesReturnReport.generate')}
                </button>
            </div>

            {/* Report Content */}
            <div ref={componentRef} className="space-y-6 print:p-8">
                {/* Print Header */}
                <div className="hidden print:block text-center mb-8">
                    <h1 className="text-2xl font-bold">{t('salesReturnReport.title')}</h1>
                    <p className="text-gray-500">{filters.fromDate} - {filters.toDate}</p>
                </div>

                {data && (
                    <>
                        {/* KPI Cards */}
                        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                            <KpiCard title={t('salesReturnReport.totalReturnsAmount')} value={formatCurrency(data.summary.totalReturnsAmount)} color="text-indigo-600" />
                            <KpiCard title={t('salesReturnReport.totalCostReversed')} value={formatCurrency(data.summary.totalCostReversed)} color="text-gray-500" />
                            <KpiCard title={t('salesReturnReport.netFinancialImpact')} value={formatCurrency(data.summary.netFinancialImpact)} color="text-red-600" />
                            <KpiCard title={t('salesReturnReport.totalQuantityReturned')} value={data.summary.totalQuantityReturned} color="text-orange-500" />

                            <div className="bg-white p-4 rounded-xl shadow-sm border border-gray-100 col-span-1 md:col-span-2">
                                <h3 className="text-sm font-medium text-gray-500">{t('salesReturnReport.mostReturnedProduct')}</h3>
                                <p className="text-xl font-bold text-gray-800 mt-1">{data.summary.mostReturnedProduct}</p>
                            </div>
                            <div className="bg-white p-4 rounded-xl shadow-sm border border-gray-100 col-span-1 md:col-span-2">
                                <h3 className="text-sm font-medium text-gray-500">{t('salesReturnReport.customerWithMostReturns')}</h3>
                                <p className="text-xl font-bold text-gray-800 mt-1">{data.summary.customerWithMostReturns}</p>
                            </div>
                        </div>

                        {/* Table */}
                        <div className="bg-white rounded-xl shadow-sm border border-gray-100 overflow-hidden">
                            <div className="overflow-x-auto">
                                <table className="w-full text-sm text-left">
                                    <thead className="text-xs text-gray-700 uppercase bg-gray-50">
                                        <tr>
                                            <th className="px-4 py-3">{t('salesReturnReport.returnNo')}</th>
                                            <th className="px-4 py-3">{t('salesReturnReport.originalInvoiceNo')}</th>
                                            <th className="px-4 py-3">{t('salesReturnReport.returnDate')}</th>
                                            <th className="px-4 py-3">{t('salesReturnReport.customer')}</th>
                                            <th className="px-4 py-3">{t('salesReturnReport.product')}</th>
                                            <th className="px-4 py-3">{t('salesReturnReport.unit')}</th>
                                            <th className="px-4 py-3 text-right">{t('salesReturnReport.qtyReturned')}</th>
                                            <th className="px-4 py-3 text-right">{t('salesReturnReport.grossReturn')}</th>
                                            <th className="px-4 py-3 text-right">{t('salesReturnReport.discount')}</th>
                                            <th className="px-4 py-3 text-right">{t('salesReturnReport.netReturn')}</th>
                                            <th className="px-4 py-3 text-right">{t('salesReturnReport.costReversed')}</th>
                                            <th className="px-4 py-3 text-right">{t('salesReturnReport.profitImpact')}</th>
                                            <th className="px-4 py-3">{t('salesReturnReport.paymentMethod')}</th>
                                            <th className="px-4 py-3">{t('salesReturnReport.reason')}</th>
                                        </tr>
                                    </thead>
                                    <tbody className="divide-y divide-gray-200">
                                        {data.items.map((item, index) => (
                                            <tr key={index} className="hover:bg-gray-50">
                                                <td className="px-4 py-3 font-medium">{item.returnNumber}</td>
                                                <td className="px-4 py-3">{item.originalInvoiceNumber}</td>
                                                <td className="px-4 py-3">{format(new Date(item.returnDate), 'MMM dd, yyyy')}</td>
                                                <td className="px-4 py-3">{item.customerName}</td>
                                                <td className="px-4 py-3">
                                                    <div>{item.productName}</div>
                                                    <div className="text-xs text-gray-500">{item.productCode}</div>
                                                </td>
                                                <td className="px-4 py-3">{item.unit}</td>
                                                <td className="px-4 py-3 text-right font-medium">{item.quantityReturned}</td>
                                                <td className="px-4 py-3 text-right">{formatCurrency(item.grossReturnAmount)}</td>
                                                <td className="px-4 py-3 text-right text-red-500">{item.discountReturned > 0 ? `-${formatCurrency(item.discountReturned)}` : '-'}</td>
                                                <td className="px-4 py-3 text-right font-bold">{formatCurrency(item.netReturnAmount)}</td>
                                                <td className="px-4 py-3 text-right text-gray-500">{formatCurrency(item.costOfReturnedItems)}</td>
                                                <td className="px-4 py-3 text-right font-medium text-red-600">{formatCurrency(item.profitImpact)}</td>
                                                <td className="px-4 py-3">
                                                    <span className={`px-2 py-1 rounded-full text-xs font-medium ${item.paymentMethod === 'Cash' ? 'bg-green-100 text-green-800' :
                                                        item.paymentMethod === 'Credit' ? 'bg-orange-100 text-orange-800' :
                                                            'bg-blue-100 text-blue-800'
                                                        }`}>
                                                        {item.paymentMethod}
                                                    </span>
                                                </td>
                                                <td className="px-4 py-3 text-xs text-gray-500 max-w-xs truncate" title={item.reason}>{item.reason}</td>
                                            </tr>
                                        ))}
                                        {data.items.length === 0 && (
                                            <tr>
                                                <td colSpan="14" className="px-4 py-8 text-center text-gray-500">
                                                    {t('common.noData')}
                                                </td>
                                            </tr>
                                        )}
                                    </tbody>
                                    <tfoot className="bg-gray-50 font-bold">
                                        <tr>
                                            <td colSpan="6" className="px-4 py-3 text-right">{t('common.total')}</td>
                                            <td className="px-4 py-3 text-right">{data.summary.totalQuantityReturned}</td>
                                            <td colSpan="2"></td>
                                            <td className="px-4 py-3 text-right">{formatCurrency(data.summary.totalReturnsAmount)}</td>
                                            <td className="px-4 py-3 text-right">{formatCurrency(data.summary.totalCostReversed)}</td>
                                            <td className="px-4 py-3 text-right text-red-600">{formatCurrency(data.summary.netFinancialImpact)}</td>
                                            <td colSpan="2"></td>
                                        </tr>
                                    </tfoot>
                                </table>
                            </div>
                        </div>
                    </>
                )}

                {/* Empty State */}
                {!data && !loading && (
                    <div className="text-center py-12 bg-white rounded-xl shadow-sm border border-gray-100">
                        <span className="text-4xl">📊</span>
                        <h3 className="mt-2 text-sm font-medium text-gray-900">{t('salesAnalysis.startPrompt')}</h3>
                    </div>
                )}
            </div>
        </div>
    )
}

function KpiCard({ title, value, color }) {
    return (
        <div className="bg-white p-4 rounded-xl shadow-sm border border-gray-100">
            <h3 className="text-sm font-medium text-gray-500">{title}</h3>
            <p className={`text-2xl font-bold mt-1 ${color}`}>{value}</p>
        </div>
    )
}

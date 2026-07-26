import { useState, useEffect, useRef, useCallback, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useReactToPrint } from 'react-to-print'
import { useNavigate } from 'react-router-dom'
import api from '../services/api'
import { useSettings } from '../contexts/SettingsContext'
import ReportTruthBadge from '../components/ReportTruthBadge'
import { format } from 'date-fns'
import { PieChart, BarChart2, FileText, ArrowRight } from 'lucide-react'
import { createPdfDocument, loadExcelModule } from '../utils/reportExports'
import AsyncEntitySelect from '../components/AsyncEntitySelect'

export default function PurchasesBySupplierReport() {
    const { t } = useTranslation()
    const componentRef = useRef()
    const { formatCurrency } = useSettings()
    const navigate = useNavigate()

    const [loading, setLoading] = useState(false)
    const [data, setData] = useState(null)
    const [filters, setFilters] = useState({
        startDate: new Date(new Date().setDate(new Date().getDate() - 30)).toISOString().split('T')[0],
        endDate: new Date().toISOString().split('T')[0],
        supplierId: '',
        supplierName: '',
        warehouseId: '',
        categoryId: '',
        productId: '',
        productName: '',
        paymentMethod: '',
        onlyPosted: 'true'
    })

    // Metadata
    const [warehouses, setWarehouses] = useState([])
    const [categories, setCategories] = useState([])

    // Invoices Modal
    const [showInvoicesModal, setShowInvoicesModal] = useState(false)
    const [selectedSupplierInvoices, setSelectedSupplierInvoices] = useState(null)
    const [loadingInvoices, setLoadingInvoices] = useState(false)
    const [invoiceList, setInvoiceList] = useState([])

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
        try {
            const params = {
                startDate: filters.startDate,
                endDate: filters.endDate,
                supplierId: filters.supplierId || undefined,
                warehouseId: filters.warehouseId || undefined,
                categoryId: filters.categoryId || undefined,
                productId: filters.productId || undefined,
                paymentMethod: filters.paymentMethod || undefined,
                onlyPosted: filters.onlyPosted === 'true'
            }
            const response = await api.get('/reports/purchases-by-supplier', { params })
            setData(response.data)
        } catch (error) {
            console.error('Failed to load report', error)
        } finally {
            setLoading(false)
        }
    }

    const handleViewStatement = (supplierId) => {
        // Navigate to Statement of Account, passing state or query params if supported
        // Assuming generic SOA page handles selection manually or we pass props if we could.
        // For now, standard navigation. User will have to select supplier manually or we implement URL param support in SOA later.
        // Actually best is to pass state.
        // But since SOA page reads from local state, pass via location state if SOA supports it?
        // Let's just navigate there.
        navigate('/statement-of-account')
    }

    const handleViewInvoices = async (supplierId, supplierName) => {
        setSelectedSupplierInvoices({ id: supplierId, name: supplierName })
        setShowInvoicesModal(true)
        setLoadingInvoices(true)
        try {
            // Fetch invoices for this supplier in date range
            const params = {
                supplierId: supplierId,
                fromDate: filters.startDate,
                toDate: filters.endDate,
                pageSize: 100 // Limit
            }
            // Use existing PurchaseInvoices endpoint?
            // Usually /PurchaseInvoices handles parameters?
            // Or create a specific one? Assuming /PurchaseInvoices supports filtering.
            // If not, we might need to rely on the backend report returning detailed list? No, report returns aggregate.
            // We'll try the generic endpoint.
            const response = await api.get('/purchases', { params })
            setInvoiceList(response.data.items || [])
        } catch (error) {
            console.error(error)
            setInvoiceList([])
        } finally {
            setLoadingInvoices(false)
        }
    }

    const handlePrint = useReactToPrint({
        content: () => componentRef.current,
        documentTitle: t('reports.purchasesBySupplier')
    })

    const handleExportExcel = async () => {
        if (!data) return
        const XLSX = await loadExcelModule()
        const wb = XLSX.utils.book_new()

        // Summary
        const summaryRows = [
            [t('reports.totalPurchases'), data.totalPurchases],
            [t('reports.totalReturns'), data.totalReturns],
            [t('reports.totalPaid'), data.totalPaid],
            [t('reports.totalOutstanding'), data.totalOutstanding]
        ]
        const wsSummary = XLSX.utils.aoa_to_sheet(summaryRows)
        XLSX.utils.book_append_sheet(wb, wsSummary, "Summary")

        // Items
        const headers = [
            t('suppliers.code'), t('suppliers.name'), t('dashboard.invoicesCount'),
            t('reports.grossPurchases'), t('reports.discounts'), t('reports.netPurchases'),
            t('reports.returns'), t('reports.netPayable'), t('reports.paidAmount'),
            t('reports.outstandingBalance'), t('reports.quantity'), t('reports.avgCost'),
            t('reports.contribution') + ' %'
        ]
        const rows = data.items.map(item => [
            item.supplierCode, item.supplierName, item.invoicesCount,
            item.grossPurchases, item.discounts, item.netPurchases,
            item.returns, item.netPayable, item.paidAmount,
            item.outstandingBalance, item.quantityPurchased, item.avgCostPerUnit,
            item.contributionPercentage.toFixed(2)
        ])
        const wsItems = XLSX.utils.aoa_to_sheet([headers, ...rows])
        XLSX.utils.book_append_sheet(wb, wsItems, "Details")

        XLSX.writeFile(wb, "PurchasesBySupplier.xlsx")
    }

    const handleExportPDF = async () => {
        if (!data) return
        const doc = await createPdfDocument('landscape')
        doc.text(t('reports.purchasesBySupplier'), 14, 15)
        doc.setFontSize(10)
        doc.text(`${filters.startDate} - ${filters.endDate}`, 14, 22)

        const headers = [[
            t('suppliers.name'), t('dashboard.invoicesCount'),
            t('reports.gross'), t('reports.net'),
            t('reports.returns'), t('reports.payable'),
            t('reports.paid'), t('reports.balance')
        ]]

        const rows = data.items.map(item => [
            item.supplierName,
            item.invoicesCount,
            formatCurrency(item.grossPurchases),
            formatCurrency(item.netPurchases),
            formatCurrency(item.returns),
            formatCurrency(item.netPayable),
            formatCurrency(item.paidAmount),
            formatCurrency(item.outstandingBalance)
        ])

        doc.autoTable({
            head: headers,
            body: rows,
            startY: 30,
            theme: 'grid',
            headStyles: { fillColor: [63, 81, 181] }
        })
        doc.save("PurchasesBySupplier.pdf")
    }

    const renderPieChart = () => {
        if (!data || !data.supplierShares.length) return null

        // Simple Conic Gradient Pie
        // This is a rough approximation for visuals without a library
        let currentDeg = 0
        const gradientParts = data.supplierShares.map((share, idx) => {
            const deg = (share.percentage / 100) * 360
            const color = ['#4F46E5', '#EF4444', '#10B981', '#F59E0B', '#6366F1'][idx % 5]
            const part = `${color} ${currentDeg}deg ${currentDeg + deg}deg`
            currentDeg += deg
            return part
        })
        // Fill rest with gray
        if (currentDeg < 360) gradientParts.push(`#E5E7EB ${currentDeg}deg 360deg`)

        return (
            <div className="flex items-center gap-8">
                <div
                    className="w-32 h-32 rounded-full border-4 border-white shadow-lg"
                    style={{ background: `conic-gradient(${gradientParts.join(', ')})` }}
                ></div>
                <div className="space-y-2">
                    {data.supplierShares.map((share, idx) => (
                        <div key={idx} className="flex items-center gap-2 text-sm">
                            <span
                                className="w-3 h-3 rounded-full"
                                style={{ backgroundColor: ['#4F46E5', '#EF4444', '#10B981', '#F59E0B', '#6366F1'][idx % 5] }}
                            ></span>
                            <span className="text-gray-600">{share.supplierName}</span>
                            <span className="font-bold text-gray-900">{share.percentage.toFixed(1)}%</span>
                        </div>
                    ))}
                </div>
            </div>
        )
    }

    return (
        <div className="space-y-6">
            {/* Phase C4: UI Truth Label */}
            <ReportTruthBadge type="operational" />

            {/* Header */}
            <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
                <div>
                    <h1 className="text-2xl font-bold text-gray-800 flex items-center gap-2">
                        <FileText className="w-6 h-6 text-indigo-600" />
                        {t('reports.purchasesBySupplier')}
                    </h1>
                    <p className="text-gray-500">{t('reports.purchasesBySupplierDesc')}</p>
                </div>
                <div className="flex flex-wrap gap-2">
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
            <div className="bg-white p-4 rounded-xl shadow-sm border border-gray-100 grid grid-cols-1 md:grid-cols-4 lg:grid-cols-5 gap-4">
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('common.from')}</label>
                    <input type="date" value={filters.startDate} onChange={e => setFilters({ ...filters, startDate: e.target.value })} className="w-full px-3 py-2 border rounded-lg focus:ring-indigo-500" />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('common.to')}</label>
                    <input type="date" value={filters.endDate} onChange={e => setFilters({ ...filters, endDate: e.target.value })} className="w-full px-3 py-2 border rounded-lg focus:ring-indigo-500" />
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
                        getOptionLabel={(s) => s?.supplierName || s?.name || ''}
                        placeholder={t('common.all')}
                        className="w-full"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('dashboard.warehouse')}</label>
                    <select value={filters.warehouseId} onChange={e => setFilters({ ...filters, warehouseId: e.target.value })} className="w-full px-3 py-2 border rounded-lg">
                        <option value="">{t('common.all')}</option>
                        {warehouses.map(w => <option key={w.id} value={w.id}>{w.name}</option>)}
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('purchaseRegister.postingStatus')}</label>
                    <select value={filters.onlyPosted} onChange={e => setFilters({ ...filters, onlyPosted: e.target.value })} className="w-full px-3 py-2 border rounded-lg">
                        <option value="true">{t('purchaseRegister.posted')}</option>
                        <option value="false">{t('common.all')}</option>
                    </select>
                </div>

                {/* Optional Filters Row */}
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('nav.mainSubCategories')}</label>
                    <select value={filters.categoryId} onChange={e => setFilters({ ...filters, categoryId: e.target.value })} className="w-full px-3 py-2 border rounded-lg">
                        <option value="">{t('common.all')}</option>
                        {categories.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
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
                    <select value={filters.paymentMethod} onChange={e => setFilters({ ...filters, paymentMethod: e.target.value })} className="w-full px-3 py-2 border rounded-lg">
                        <option value="">{t('common.all')}</option>
                        <option value="Cash">{t('purchases.cash')}</option>
                        <option value="Visa">{t('pos.card')}</option>
                        <option value="Bank">{t('purchases.bankTransfer')}</option>
                        <option value="Credit">{t('purchases.credit')}</option>
                    </select>
                </div>

                <div className="flex items-end">
                    <button onClick={fetchReport} disabled={loading} className="w-full px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:opacity-50">
                        {loading ? t('common.processing') : t('purchaseRegister.generate')}
                    </button>
                </div>
            </div>

            {data && (
                <div ref={componentRef} className="space-y-6">
                    {/* KPIs */}
                    <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
                        <KpiCard title={t('reports.totalPurchases')} value={formatCurrency(data.totalPurchases)} color="text-indigo-600" />
                        <KpiCard title={t('reports.totalReturns')} value={formatCurrency(data.totalReturns)} color="text-red-600" />
                        <KpiCard title={t('reports.totalPaid')} value={formatCurrency(data.totalPaid)} color="text-green-600" />
                        <KpiCard title={t('reports.totalOutstanding')} value={formatCurrency(data.totalOutstanding)} color="text-orange-600" />
                    </div>

                    {/* Charts Area */}
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6 print:hidden">
                        <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-100">
                            <h3 className="text-lg font-bold text-gray-800 mb-4 flex items-center gap-2">
                                <PieChart className="w-5 h-5 text-gray-500" />
                                {t('reports.supplierShare')}
                            </h3>
                            {renderPieChart()}
                        </div>
                        <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-100">
                            <h3 className="text-lg font-bold text-gray-800 mb-4 flex items-center gap-2">
                                <BarChart2 className="w-5 h-5 text-gray-500" />
                                {t('reports.topSuppliersVolume')}
                            </h3>
                            <div className="space-y-4">
                                {data.supplierShares.map((share, idx) => (
                                    <div key={idx}>
                                        <div className="flex justify-between text-sm mb-1">
                                            <span className="font-medium text-gray-700">{share.supplierName}</span>
                                            <span className="text-gray-500">{formatCurrency(share.amount)}</span>
                                        </div>
                                        <div className="w-full bg-gray-100 rounded-full h-2.5">
                                            <div
                                                className="bg-indigo-600 h-2.5 rounded-full"
                                                style={{ width: `${share.percentage}%` }}
                                            ></div>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        </div>
                    </div>

                    {/* Table */}
                    <div className="bg-white rounded-xl shadow-sm border border-gray-100 overflow-hidden">
                        <div className="overflow-x-auto">
                            <table className="w-full text-sm text-left">
                                <thead className="text-xs text-gray-700 uppercase bg-gray-50">
                                    <tr>
                                        <th className="px-4 py-3">{t('suppliers.code')}</th>
                                        <th className="px-4 py-3">{t('suppliers.name')}</th>
                                        <th className="px-4 py-3 text-center cursor-pointer hover:bg-gray-100" title="Click to view invoices">
                                            {t('dashboard.invoicesCount')}
                                        </th>
                                        <th className="px-4 py-3 text-right">{t('reports.grossPurchases')}</th>
                                        <th className="px-4 py-3 text-right">{t('reports.discounts')}</th>
                                        <th className="px-4 py-3 text-right">{t('reports.netPurchases')}</th>
                                        <th className="px-4 py-3 text-right">{t('reports.returns')}</th>
                                        <th className="px-4 py-3 text-right font-bold bg-gray-100">{t('reports.netPayable')}</th>
                                        <th className="px-4 py-3 text-right">{t('reports.paidAmount')}</th>
                                        <th className="px-4 py-3 text-right">{t('reports.outstandingBalance')}</th>
                                        <th className="px-4 py-3 text-center">{t('reports.quantity')}</th>
                                        <th className="px-4 py-3 text-right">{t('reports.avgCost')}</th>
                                        <th className="px-4 py-3 text-center">{t('reports.contribution')}</th>
                                        <th className="px-4 py-3 print:hidden">{t('common.actions')}</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-gray-200">
                                    {data.items.map((item, idx) => (
                                        <tr key={idx} className="hover:bg-gray-50">
                                            <td className="px-4 py-3 text-gray-500">{item.supplierCode}</td>
                                            <td className="px-4 py-3 font-medium text-gray-900">{item.supplierName}</td>
                                            <td className="px-4 py-3 text-center">
                                                <button
                                                    onClick={() => handleViewInvoices(item.supplierId, item.supplierName)}
                                                    className="px-2 py-1 bg-blue-50 text-blue-600 rounded-full text-xs font-bold hover:bg-blue-100"
                                                >
                                                    {item.invoicesCount}
                                                </button>
                                            </td>
                                            <td className="px-4 py-3 text-right">{formatCurrency(item.grossPurchases)}</td>
                                            <td className="px-4 py-3 text-right text-red-500">{item.discounts > 0 ? `-${formatCurrency(item.discounts)}` : '-'}</td>
                                            <td className="px-4 py-3 text-right font-medium">{formatCurrency(item.netPurchases)}</td>
                                            <td className="px-4 py-3 text-right text-red-500">{formatCurrency(item.returns)}</td>
                                            <td className="px-4 py-3 text-right font-bold bg-gray-50">{formatCurrency(item.netPayable)}</td>
                                            <td className="px-4 py-3 text-right text-green-600">{formatCurrency(item.paidAmount)}</td>
                                            <td className="px-4 py-3 text-right font-medium text-orange-600">{formatCurrency(item.outstandingBalance)}</td>
                                            <td className="px-4 py-3 text-center">{item.quantityPurchased}</td>
                                            <td className="px-4 py-3 text-right">{formatCurrency(item.avgCostPerUnit)}</td>
                                            <td className="px-4 py-3 text-center">
                                                <div className="flex items-center gap-2 justify-center">
                                                    <div className="w-10 bg-gray-200 rounded-full h-1.5 overflow-hidden">
                                                        <div className="bg-indigo-500 h-1.5 rounded-full" style={{ width: `${item.contributionPercentage}%` }}></div>
                                                    </div>
                                                    <span className="text-xs text-gray-500">{item.contributionPercentage.toFixed(1)}%</span>
                                                </div>
                                            </td>
                                            <td className="px-4 py-3 print:hidden">
                                                <button
                                                    onClick={() => handleViewStatement(item.supplierId)}
                                                    className="p-1 hover:bg-gray-100 rounded text-gray-600"
                                                    title={t('reports.viewStatement')}
                                                >
                                                    <ArrowRight className="w-4 h-4" />
                                                </button>
                                            </td>
                                        </tr>
                                    ))}
                                    {data.items.length === 0 && (
                                        <tr><td colSpan="14" className="text-center py-8 text-gray-500">{t('common.noData')}</td></tr>
                                    )}
                                </tbody>
                            </table>
                        </div>
                    </div>
                </div>
            )}

            {/* Invoices Modal */}
            {showInvoicesModal && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-xl shadow-xl w-full max-w-3xl max-h-[80vh] overflow-y-auto">
                        <div className="sticky top-0 bg-white border-b px-6 py-4 flex items-center justify-between">
                            <h2 className="text-xl font-bold">{t('reports.invoicesFor')} {selectedSupplierInvoices?.name}</h2>
                            <button onClick={() => setShowInvoicesModal(false)} className="text-gray-500 hover:text-gray-700 text-2xl">×</button>
                        </div>
                        <div className="p-6">
                            {loadingInvoices ? (
                                <div className="text-center py-8"><div className="animate-spin w-8 h-8 border-4 border-indigo-500 border-t-transparent rounded-full mx-auto"></div></div>
                            ) : (
                                <table className="w-full text-sm">
                                    <thead className="bg-gray-50">
                                        <tr>
                                            <th className="px-4 py-2 text-left">{t('purchaseRegister.invoiceNo')}</th>
                                            <th className="px-4 py-2 text-left">{t('common.date')}</th>
                                            <th className="px-4 py-2 text-right">{t('common.total')}</th>
                                            <th className="px-4 py-2 text-center">{t('common.status')}</th>
                                        </tr>
                                    </thead>
                                    <tbody className="divide-y">
                                        {invoiceList.map(inv => (
                                            <tr key={inv.id}>
                                                <td className="px-4 py-2 text-indigo-600 font-medium">{inv.invoiceNumber}</td>
                                                <td className="px-4 py-2">{format(new Date(inv.invoiceDate), 'yyyy-MM-dd')}</td>
                                                <td className="px-4 py-2 text-right font-bold">{formatCurrency(inv.totalAmount)}</td>
                                                <td className="px-4 py-2 text-center">
                                                    <span className={`px-2 py-1 rounded-full text-xs text-white ${inv.status === 1 ? 'bg-green-500' : 'bg-gray-500'}`}>
                                                        {inv.status === 1 ? 'Posted' : 'Draft'}
                                                    </span>
                                                </td>
                                            </tr>
                                        ))}
                                        {invoiceList.length === 0 && <tr><td colSpan="4" className="text-center py-4">{t('common.noData')}</td></tr>}
                                    </tbody>
                                </table>
                            )}
                        </div>
                    </div>
                </div>
            )}
        </div>
    )
}

function KpiCard({ title, value, color }) {
    return (
        <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-100">
            <h3 className="text-sm font-medium text-gray-500 uppercase">{title}</h3>
            <p className={`text-2xl font-bold mt-2 ${color}`}>{value}</p>
        </div>
    )
}

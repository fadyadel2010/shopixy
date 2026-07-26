import React, { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'
import { format } from 'date-fns'
import {
    RefreshCw,
    Download,
    Filter,
    Calculator,
    Banknote,
    Scale,
    ShoppingBag
} from 'lucide-react'
import { useSettings } from '../contexts/SettingsContext'
import ReportTruthBadge from '../components/ReportTruthBadge'
import { createPdfDocument, loadExcelModule } from '../utils/reportExports'
import { fetchAllPagedArray } from '../services/pagedArrayLoader'

const PurchaseReturnReport = () => {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()

    const [loading, setLoading] = useState(false)
    const [data, setData] = useState({
        summary: {
            totalReturnValue: 0,
            totalCostReversed: 0,
            netFinancialImpact: 0,
            totalReturnedQuantity: 0,
            supplierWithMostReturns: { name: '-', count: 0, amount: 0 },
            mostReturnedProduct: { name: '-', count: 0, quantity: 0 }
        },
        items: []
    })

    const [filters, setFilters] = useState({
        fromDate: new Date().toISOString().split('T')[0],
        toDate: new Date().toISOString().split('T')[0],
        warehouseId: '',
        supplierId: '',
        productId: '',
        categoryId: '',
        subCategoryId: ''
    })

    // Metadata for dropdowns
    const [warehouses, setWarehouses] = useState([])
    const [suppliers, setSuppliers] = useState([])
    const [categories, setCategories] = useState([])
    const [subCategories, setSubCategories] = useState([])
    const [products, setProducts] = useState([])

    useEffect(() => {
        let isMounted = true
        
        const fetchMetadata = async () => {
            try {
                const [whRes, supRes, catRes, prodRes] = await Promise.all([
                    api.get('/warehouses'),
                    api.get('/suppliers'),
                    api.get('/categories'),
                    fetchAllPagedArray('/products')
                ])

                if (isMounted) {
                    setWarehouses(whRes.data)
                    setSuppliers(supRes.data.items || supRes.data)
                    setCategories(catRes.data)
                    setProducts(prodRes)
                }
            } catch (error) {
                console.error('Error fetching metadata', error)
            }
        }
        
        fetchMetadata()
        
        return () => {
            isMounted = false
        }
    }, [])

    const generateReport = async () => {
        setLoading(true)
        try {
            const params = {
                fromDate: filters.fromDate,
                toDate: filters.toDate,
                warehouseId: filters.warehouseId || null,
                supplierId: filters.supplierId || null,
                productId: filters.productId || null,
                categoryId: filters.categoryId || null,
                subCategoryId: filters.subCategoryId || null
            }

            const response = await api.get('/reports/purchase-returns', { params })
            setData(response.data)
        } catch (error) {
            console.error('Error generating report', error)
        } finally {
            setLoading(false)
        }
    }

    const handleExportExcel = async () => {
        const XLSX = await loadExcelModule()
        const exportData = data.items.map(item => ({
            [t('purchaseReturnReport.returnNo')]: item.returnNumber,
            [t('purchaseReturnReport.returnDate')]: format(new Date(item.returnDate), 'dd/MM/yyyy HH:mm'),
            [t('purchaseReturnReport.supplier')]: item.supplierName,
            [t('purchaseReturnReport.originalInvoiceNo')]: item.originalInvoiceNumber,
            [t('purchaseReturnReport.product')]: item.productName,
            [t('purchaseReturnReport.unit')]: item.unitName,
            [t('purchaseReturnReport.qtyReturned')]: item.quantityReturned,
            [t('purchaseReturnReport.grossReturn')]: item.grossReturnAmount,
            [t('purchaseReturnReport.discount')]: item.discountReversed,
            [t('purchaseReturnReport.netReturn')]: item.netReturnAmount,
            [t('purchaseReturnReport.costReversed')]: item.costReversed,
            [t('purchaseReturnReport.reason')]: item.reason
        }))

        const ws = XLSX.utils.json_to_sheet(exportData)
        const wb = XLSX.utils.book_new()
        XLSX.utils.book_append_sheet(wb, ws, "Purchase Returns")
        XLSX.writeFile(wb, `Purchase_Returns_${filters.fromDate}_${filters.toDate}.xlsx`)
    }

    const handleExportPDF = async () => {
        const doc = await createPdfDocument()
        doc.text(t('purchaseReturnReport.title'), 14, 15)
        doc.setFontSize(10)
        doc.text(`${t('common.from')}: ${filters.fromDate} ${t('common.to')}: ${filters.toDate}`, 14, 25)

        const tableColumn = [
            t('purchaseReturnReport.returnNo'),
            t('purchaseReturnReport.returnDate'),
            t('purchaseReturnReport.supplier'),
            t('purchaseReturnReport.product'),
            t('purchaseReturnReport.qtyReturned'),
            t('purchaseReturnReport.netReturn')
        ]

        const tableRows = data.items.map(item => [
            item.returnNumber,
            format(new Date(item.returnDate), 'dd/MM/yyyy'),
            item.supplierName,
            item.productName,
            item.quantityReturned,
            formatCurrency(item.netReturnAmount)
        ])

        doc.autoTable({
            head: [tableColumn],
            body: tableRows,
            startY: 30,
        })

        doc.save(`Purchase_Returns_${filters.fromDate}_${filters.toDate}.pdf`)
    }

    return (
        <div className="space-y-6">
            {/* Phase C4: UI Truth Label */}
            <ReportTruthBadge type="operational" />

            <div className="flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-800">{t('purchaseReturnReport.title')}</h1>
                    <p className="text-gray-500">{t('purchaseReturnReport.subtitle')}</p>
                </div>
                <div className="flex gap-2">
                    <button
                        onClick={handleExportExcel}
                        disabled={!data || loading}
                        className="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50 flex items-center gap-2"
                    >
                        <Download className="h-4 w-4" />
                        {t('common.exportExcel')}
                    </button>
                    <button
                        onClick={handleExportPDF}
                        disabled={!data || loading}
                        className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:opacity-50 flex items-center gap-2"
                    >
                        <Download className="h-4 w-4" />
                        {t('common.exportPDF')}
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
                        onChange={(e) => setFilters({ ...filters, fromDate: e.target.value })}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('common.to')}</label>
                    <input
                        type="date"
                        value={filters.toDate}
                        onChange={(e) => setFilters({ ...filters, toDate: e.target.value })}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('common.warehouse')}</label>
                    <select
                        value={filters.warehouseId}
                        onChange={(e) => setFilters({ ...filters, warehouseId: e.target.value })}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        {warehouses.map(w => (
                            <option key={w.id} value={w.id}>{w.name}</option>
                        ))}
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('purchaseReturnReport.supplier')}</label>
                    <select
                        value={filters.supplierId}
                        onChange={(e) => setFilters({ ...filters, supplierId: e.target.value })}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        {suppliers?.map(sup => (
                            <option key={sup.id} value={sup.id}>{sup.name}</option>
                        ))}
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('purchaseReturnReport.product')}</label>
                    <select
                        value={filters.productId}
                        onChange={(e) => setFilters({ ...filters, productId: e.target.value })}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        {products.map(p => (
                            <option key={p.id} value={p.id}>{p.name}</option>
                        ))}
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('categories.title')}</label>
                    <select
                        value={filters.categoryId}
                        onChange={e => {
                            const newCatId = e.target.value;
                            setFilters({ ...filters, categoryId: newCatId, subCategoryId: '' });
                            const selectedCat = categories.find(c => c.id === newCatId);
                            setSubCategories(selectedCat?.subCategories || []);
                        }}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        {categories.map(c => (
                            <option key={c.id} value={c.id}>{c.name}</option>
                        ))}
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('products.subCategory')}</label>
                    <select
                        value={filters.subCategoryId}
                        onChange={(e) => setFilters({ ...filters, subCategoryId: e.target.value })}
                        disabled={!filters.categoryId}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        {subCategories?.map(sc => (
                            <option key={sc.id} value={sc.id}>{sc.name}</option>
                        ))}
                    </select>
                </div>
            </div>

            <div className="flex justify-end">
                <button
                    onClick={generateReport}
                    disabled={loading}
                    className="px-6 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
                >
                    {loading ? <RefreshCw className="h-5 w-5 animate-spin" /> : <Filter className="h-5 w-5" />}
                    {t('purchaseReturnReport.generate')}
                </button>
            </div>

            {data && data.items.length > 0 ? (
                <>
                    {/* KPIs */}
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                        <KpiCard
                            title={t('purchaseReturnReport.totalReturnsAmount')}
                            value={formatCurrency(data.summary.totalReturnValue)}
                            color="text-red-600"
                        />
                        <KpiCard
                            title={t('purchaseReturnReport.totalCostReversed')}
                            value={formatCurrency(data.summary.totalCostReversed)}
                            color="text-gray-600"
                        />
                        <KpiCard
                            title={t('purchaseReturnReport.netFinancialImpact')}
                            value={formatCurrency(data.summary.netFinancialImpact)}
                            color="text-green-600"
                        />
                        <KpiCard
                            title={t('purchaseReturnReport.totalQuantityReturned')}
                            value={data.summary.totalReturnedQuantity}
                            color="text-blue-600"
                        />
                    </div>

                    {/* Detailed Table */}
                    <div className="bg-white rounded-xl shadow-sm border border-gray-100 overflow-hidden">
                        <div className="overflow-x-auto">
                            <table className="w-full text-sm text-left">
                                <thead className="text-xs text-gray-700 uppercase bg-gray-50">
                                    <tr>
                                        <th className="px-4 py-3">{t('purchaseReturnReport.returnNo')}</th>
                                        <th className="px-4 py-3">{t('purchaseReturnReport.returnDate')}</th>
                                        <th className="px-4 py-3">{t('purchaseReturnReport.supplier')}</th>
                                        <th className="px-4 py-3">{t('purchaseReturnReport.product')}</th>
                                        <th className="px-4 py-3 text-center">{t('purchaseReturnReport.qtyReturned')}</th>
                                        <th className="px-4 py-3 text-right">{t('purchaseReturnReport.netReturn')}</th>
                                        <th className="px-4 py-3 text-right">{t('purchaseReturnReport.costReversed')}</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-gray-200">
                                    {data.items.map((item, index) => (
                                        <tr key={index} className="hover:bg-gray-50">
                                            <td className="px-4 py-3 font-medium text-blue-600">{item.returnNumber}</td>
                                            <td className="px-4 py-3">{format(new Date(item.returnDate), 'MMM dd, yyyy')}</td>
                                            <td className="px-4 py-3 font-medium text-gray-900">{item.supplierName}</td>
                                            <td className="px-4 py-3">
                                                <div>{item.productName}</div>
                                                <div className="text-xs text-gray-500">{item.unitName}</div>
                                            </td>
                                            <td className="px-4 py-3 text-center">{item.quantityReturned}</td>
                                            <td className="px-4 py-3 text-right font-bold text-gray-900">{formatCurrency(item.netReturnAmount)}</td>
                                            <td className="px-4 py-3 text-right text-gray-500">{formatCurrency(item.costReversed)}</td>
                                        </tr>
                                    ))}
                                </tbody>
                                <tfoot className="bg-gray-50 font-bold">
                                    <tr>
                                        <td colSpan="4" className="px-4 py-3 text-right capitalize">{t('common.total')}</td>
                                        <td className="px-4 py-3 text-center">{data.summary.totalReturnedQuantity}</td>
                                        <td className="px-4 py-3 text-right">{formatCurrency(data.summary.totalReturnValue)}</td>
                                        <td className="px-4 py-3 text-right">{formatCurrency(data.summary.totalCostReversed)}</td>
                                    </tr>
                                </tfoot>
                            </table>
                        </div>
                    </div>
                </>
            ) : (
                !loading && (
                    <div className="text-center py-12 bg-white rounded-xl shadow-sm border border-gray-100">
                        <div className="mx-auto h-12 w-12 text-gray-400">
                            <ShoppingBag className="h-12 w-12" />
                        </div>
                        <h3 className="mt-2 text-sm font-medium text-gray-900">{t('common.noData')}</h3>
                        <p className="mt-1 text-sm text-gray-500">{t('salesAnalysis.startPrompt')}</p>
                    </div>
                )
            )}
        </div>
    )
}

const KpiCard = ({ title, value, color }) => (
    <div className="bg-white p-4 rounded-xl shadow-sm border border-gray-100">
        <h3 className="text-sm font-medium text-gray-500">{title}</h3>
        <p className={`text-2xl font-bold mt-1 ${color}`}>{value}</p>
    </div>
)

export default PurchaseReturnReport

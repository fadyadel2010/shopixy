import { useState, useEffect, useRef, useCallback } from 'react'
import api from '../services/api'
import ProductStockModal from '../components/ProductStockModal'
import BarcodeDisplay from '../components/BarcodeDisplay'
import ValidationDeleteModal from '../components/ValidationDeleteModal'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import { useToast } from '../components/Toast'
import { useNavigate } from 'react-router-dom'
import { useRTL } from '../hooks/useRTL'
import { invalidatePosRuntimeCache } from '../services/posRuntimeCache'

// Storefront domain for deep links - Phase 7.10
const STOREFRONT_DOMAIN = import.meta.env.VITE_STOREFRONT_DOMAIN || 'localhost:5174'

export default function Products() {
    const { t } = useTranslation()
    const { isRTL, dir } = useRTL()
    const { formatCurrency } = useSettings()
    const { showSuccess, showError } = useToast()
    const navigate = useNavigate()
    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const actionAlignClass = isRTL ? 'text-left' : 'text-right'
    const stockIconSpacingClass = isRTL ? 'mr-1' : 'ml-1'
    const [products, setProducts] = useState([])
    const [categories, setCategories] = useState([])
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState('')
    const [search, setSearch] = useState('')
    const [committedSearch, setCommittedSearch] = useState('')
    const [categoryFilter, setCategoryFilter] = useState('')
    const [statusFilter, setStatusFilter] = useState('')
    const [page, setPage] = useState(1)
    const pageSize = 50
    const [totalCount, setTotalCount] = useState(0)
    const [totalPages, setTotalPages] = useState(0)
    const [stockModal, setStockModal] = useState(null)
    const [barcodeModal, setBarcodeModal] = useState(null)
    const [deleteModal, setDeleteModal] = useState(null) // { id, name }
    const categoriesRequestIdRef = useRef(0)
    const productsRequestIdRef = useRef(0)
    const hasStaleProductRows = loading && products.length > 0

    useEffect(() => {
        return () => {
            categoriesRequestIdRef.current += 1
            productsRequestIdRef.current += 1
        }
    }, [])

    const fetchCategories = useCallback(async () => {
        const requestId = ++categoriesRequestIdRef.current

        try {
            const response = await api.get('/categories')

            if (requestId !== categoriesRequestIdRef.current) {
                return
            }

            setCategories(response.data)
        } catch (err) {
            if (requestId === categoriesRequestIdRef.current) {
                console.error('Failed to load categories', err)
            }
        }
    }, [])

    const fetchProducts = useCallback(async () => {
        const requestId = ++productsRequestIdRef.current

        try {
            setLoading(true)
            const params = {}
            if (committedSearch) params.search = committedSearch
            if (categoryFilter) params.categoryId = categoryFilter
            if (statusFilter) params.isActive = statusFilter === 'active'
            params.page = page
            params.pageSize = pageSize

            const response = await api.get('/products', { params })

            if (requestId !== productsRequestIdRef.current) {
                return
            }

            const newTotalPages = Number(response.headers['x-total-pages'] ?? 0)
            
            // Clamp current page if it exceeds the new total pages BEFORE committing data
            if (page > newTotalPages && newTotalPages > 0) {
                setTotalPages(newTotalPages)
                setPage(newTotalPages)
                // Increment requestId so finally block won't clear loading - re-fetch will handle it
                productsRequestIdRef.current++
                return
            } else if (newTotalPages === 0 && page !== 1) {
                setTotalPages(newTotalPages)
                setPage(1)
                // Increment requestId so finally block won't clear loading - re-fetch will handle it
                productsRequestIdRef.current++
                return
            }
            
            // Only commit data if we're on a valid page
            setProducts(response.data)
            setTotalCount(Number(response.headers['x-total-count'] ?? response.data.length ?? 0))
            setTotalPages(newTotalPages)
            setError('')
        } catch (err) {
            if (requestId === productsRequestIdRef.current) {
                setError(t('products.failedToLoad'))
                console.error(err)
            }
        } finally {
            if (requestId === productsRequestIdRef.current) {
                setLoading(false)
            }
        }
    }, [committedSearch, categoryFilter, statusFilter, page, pageSize, t])

    useEffect(() => {
        void fetchCategories()
    }, [fetchCategories])

    useEffect(() => {
        void fetchProducts()
    }, [fetchProducts])

    const handleSearch = (e) => {
        e.preventDefault()

        if (search === committedSearch) {
            void fetchProducts()
            return
        }

        setPage(1)
        setCommittedSearch(search)
    }

    const handleCategoryFilterChange = (e) => {
        setPage(1)
        setCommittedSearch(search)
        setCategoryFilter(e.target.value)
    }

    const handleStatusFilterChange = (e) => {
        setPage(1)
        setCommittedSearch(search)
        setStatusFilter(e.target.value)
    }

    const handleAdd = () => {
        navigate('/products/new/edit')
    }

    const handleEdit = (productId) => {
        navigate(`/products/${productId}/edit`)
    }

    const handleDelete = (product) => {
        setDeleteModal({
            id: product.id,
            name: product.name
        })
    }

    const handleDeleteSuccess = () => {
        invalidatePosRuntimeCache({ reason: 'product-updated' })
        showSuccess(t('common.deletedSuccessfully'))
        void fetchProducts()
    }

    const handleDeactivateSuccess = () => {
        invalidatePosRuntimeCache({ reason: 'product-updated' })
        showSuccess(t('products.deactivated'))
        void fetchProducts()
    }

    const handleShowStock = (product) => {
        setStockModal({
            productId: product.id,
            productName: product.name
        })
    }

    const handleShowBarcode = (barcode) => {
        setBarcodeModal(barcode)
    }

    const getTotalStock = (product) => {
        return product.warehouseStocks?.reduce((sum, ws) => sum + ws.quantity, 0) || 0
    }

    const isLowStock = (product) => {
        const totalStock = getTotalStock(product)
        return totalStock <= product.minimumStockLevel
    }

    return (
        <div dir={dir}>
            <div className="flex justify-between items-center mb-6">
                <h2 className="text-2xl font-bold text-gray-900">{t('products.title')}</h2>
                <button onClick={handleAdd} className="btn-primary">+ {t('products.addProduct')}</button>
            </div>

            {/* Filters */}
            <div className="card mb-6">
                <form onSubmit={handleSearch} className="space-y-4">
                    <div className="flex gap-4">
                        <input
                            type="text"
                            placeholder={t('products.searchPlaceholder')}
                            value={search}
                            onChange={(e) => setSearch(e.target.value)}
                            className="input flex-1"
                        />
                        <button type="submit" className="btn-primary">{t('common.search')}</button>
                    </div>
                    <div className="grid grid-cols-2 gap-4">
                        <div>
                            <label className="label">{t('products.category')}</label>
                            <select
                                value={categoryFilter}
                                onChange={handleCategoryFilterChange}
                                className="input"
                            >
                                <option value="">{t('products.allCategories')}</option>
                                {categories.map(cat => (
                                    <option key={cat.id} value={cat.id}>
                                        {cat.parentCategoryName ? `${cat.parentCategoryName} > ` : ''}{cat.name}
                                    </option>
                                ))}
                            </select>
                        </div>
                        <div>
                            <label className="label">{t('products.status')}</label>
                            <select
                                value={statusFilter}
                                onChange={handleStatusFilterChange}
                                className="input"
                            >
                                <option value="">{t('products.allStatus')}</option>
                                <option value="active">{t('products.active')}</option>
                                <option value="inactive">{t('products.inactive')}</option>
                            </select>
                        </div>
                    </div>
                </form>
            </div>

            {error && (
                <div className="mb-4 p-3 bg-red-100 border border-red-400 text-red-700 rounded">
                    {error}
                </div>
            )}

            <div className="card">
                {hasStaleProductRows ? (
                    <div className="border-b border-gray-200 bg-gray-50 px-6 py-2 text-xs text-gray-500">
                        {t('common.loading')}
                    </div>
                ) : null}
                {loading && products.length === 0 ? (
                    <p className="text-center py-8 text-gray-500">{t('products.loading')}</p>
                ) : products.length === 0 ? (
                    <p className="text-center py-8 text-gray-500">{t('products.noProducts')}</p>
                ) : (
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                                <tr>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('products.productName')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('products.barcode')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('products.category')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('products.price')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('products.stock')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('products.status')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${actionAlignClass}`}>
                                        {t('products.actions')}
                                    </th>
                                </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                                {products.map((product) => (
                                    <tr
                                        key={product.id}
                                        aria-disabled={hasStaleProductRows}
                                        className={hasStaleProductRows ? 'pointer-events-none opacity-60' : 'cursor-pointer hover:bg-gray-50'}
                                        onClick={hasStaleProductRows ? undefined : () => handleEdit(product.id)}
                                    >
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <div className="flex items-center gap-3">
                                                {product.images && (
                                                    <img
                                                        src={product.images.split(',')[0]}
                                                        alt={product.name}
                                                        className="w-10 h-10 object-cover rounded"
                                                        onError={(e) => e.target.style.display = 'none'}
                                                    />
                                                )}
                                                <div>
                                                    <div className="text-sm font-medium text-gray-900">{product.name}</div>
                                                    <div className="text-sm text-gray-500">{product.sku}</div>
                                                </div>
                                            </div>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            {product.barcode ? (
                                                <button
                                                    type="button"
                                                    disabled={hasStaleProductRows}
                                                    onClick={(e) => {
                                                        e.stopPropagation()
                                                        handleShowBarcode(product.barcode)
                                                    }}
                                                    className="text-sm text-primary-600 hover:text-primary-900 disabled:cursor-not-allowed disabled:text-primary-300 font-mono"
                                                    title={t('products.viewBarcode')}
                                                >
                                                    {product.barcode}
                                                </button>
                                            ) : (
                                                <span className="text-sm text-gray-400">-</span>
                                            )}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            {product.category?.name || '-'}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                            {formatCurrency(product.salePrice)}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <button
                                                type="button"
                                                disabled={hasStaleProductRows}
                                                onClick={(e) => {
                                                    e.stopPropagation()
                                                    handleShowStock(product)
                                                }}
                                                className={`px-3 py-1 inline-flex text-xs leading-5 font-semibold rounded-full cursor-pointer hover:opacity-80 ${isLowStock(product)
                                                    ? 'bg-red-100 text-red-800'
                                                    : 'bg-green-100 text-green-800'
                                                    } disabled:cursor-not-allowed disabled:opacity-60`}
                                                title={t('products.viewStock')}
                                            >
                                                {getTotalStock(product)}
                                                {isLowStock(product) && (
                                                    <svg className={`w-3 h-3 inline ${stockIconSpacingClass}`} fill="currentColor" viewBox="0 0 20 20">
                                                        <path fillRule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
                                                    </svg>
                                                )}
                                            </button>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <span className={`px-2 inline-flex text-xs leading-5 font-semibold rounded-full ${product.isActive
                                                ? 'bg-green-100 text-green-800'
                                                : 'bg-gray-100 text-gray-800'
                                                }`}>
                                                {product.isActive ? t('products.active') : t('products.inactive')}
                                            </span>
                                        </td>
                                        <td className={`px-6 py-4 whitespace-nowrap text-sm font-medium ${actionAlignClass}`}>
                                            <div className={`flex items-center gap-2 ${isRTL ? 'justify-start' : 'justify-end'}`}>
                                                <button
                                                    type="button"
                                                    disabled={hasStaleProductRows}
                                                    onClick={(e) => {
                                                        e.stopPropagation()
                                                        handleEdit(product.id)
                                                    }}
                                                    className="text-primary-600 hover:text-primary-900 disabled:cursor-not-allowed disabled:text-primary-300"
                                                >
                                                    {t('common.edit')}
                                                </button>
                                                <button
                                                    type="button"
                                                    disabled={hasStaleProductRows}
                                                    onClick={(e) => {
                                                        e.stopPropagation()
                                                        handleDelete(product)
                                                    }}
                                                    className="text-red-600 hover:text-red-900 disabled:cursor-not-allowed disabled:text-red-300"
                                                >
                                                    {t('common.delete')}
                                                </button>
                                                {product.isOnlineVisible && product.slug && (
                                                    <a
                                                        href={`http://${STOREFRONT_DOMAIN}/product/${product.slug}`}
                                                        target="_blank"
                                                        rel="noopener noreferrer"
                                                        aria-disabled={hasStaleProductRows}
                                                        onClick={(e) => e.stopPropagation()}
                                                        tabIndex={hasStaleProductRows ? -1 : undefined}
                                                        className={`text-blue-600 hover:text-blue-900 flex items-center gap-1 ${hasStaleProductRows ? 'pointer-events-none text-blue-300' : ''}`}
                                                        title={t('products.viewOnStorefront')}
                                                    >
                                                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
                                                        </svg>
                                                    </a>
                                                )}
                                            </div>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>

            {!loading && totalPages > 1 ? (
                <div className="mt-4 flex items-center justify-between border-t border-gray-200 pt-4">
                    <div className="text-sm text-gray-600">
                        {totalCount > 0
                            ? `${(page - 1) * pageSize + 1}-${Math.min(page * pageSize, totalCount)} / ${totalCount}`
                            : '0 / 0'}
                    </div>
                    <div className="flex gap-2">
                        <button
                            type="button"
                            onClick={() => setPage((currentPage) => Math.max(1, currentPage - 1))}
                            disabled={page === 1}
                            className="px-4 py-2 border border-gray-300 rounded-lg text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                        >
                            {t('common.previous')}
                        </button>
                        <button
                            type="button"
                            onClick={() => setPage((currentPage) => Math.min(totalPages, currentPage + 1))}
                            disabled={page >= totalPages}
                            className="px-4 py-2 border border-gray-300 rounded-lg text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                        >
                            {t('common.next')}
                        </button>
                    </div>
                </div>
            ) : null}

            {/* Stock Modal */}
            {stockModal && (
                <ProductStockModal
                    productId={stockModal.productId}
                    productName={stockModal.productName}
                    onClose={() => setStockModal(null)}
                />
            )}

            {/* Barcode Display Modal */}
            {barcodeModal && (
                <BarcodeDisplay
                    barcode={barcodeModal}
                    onClose={() => setBarcodeModal(null)}
                />
            )}

            {/* Validation Delete Modal */}
            {deleteModal && (
                <ValidationDeleteModal
                    isOpen={!!deleteModal}
                    onClose={() => setDeleteModal(null)}
                    entityType="product"
                    entityId={deleteModal.id}
                    entityName={deleteModal.name}
                    onDeleteSuccess={handleDeleteSuccess}
                    onDeactivateSuccess={handleDeactivateSuccess}
                />
            )}
        </div>
    )
}

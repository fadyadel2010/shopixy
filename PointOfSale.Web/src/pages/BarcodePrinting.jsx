import { useState, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useReactToPrint } from 'react-to-print'
import JsBarcode from 'jsbarcode'
import { Search, Printer, Trash2, Plus, Barcode as BarcodeIcon, CheckSquare } from 'lucide-react'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import api from '../services/api'
import { fetchAllPagedArray } from '../services/pagedArrayLoader'
import { useToast } from '../components/Toast'

// Tiny component to render a single barcode
const BarcodeItem = ({ value, price, name, currencySymbol }) => {
    const svgRef = useRef(null)

    useEffect(() => {
        if (svgRef.current && value) {
            try {
                JsBarcode(svgRef.current, value, {
                    format: "CODE128",
                    width: 1.5,
                    height: 40,
                    fontSize: 12,
                    displayValue: true,
                    margin: 0
                })
            } catch (e) {
                console.error("Barcode generation failed", e)
            }
        }
    }, [value])

    return (
        <div className="flex flex-col items-center justify-center p-2 border border-dashed border-gray-300 rounded mb-2 w-[180px] h-[120px] bg-white text-center page-break-inside-avoid">
            <div className="text-xs font-bold truncate w-full px-1 mb-1">{name}</div>
            <svg ref={svgRef} className="w-full h-auto max-h-[50px]"></svg>
            <div className="text-sm font-bold mt-1">{currencySymbol}{parseFloat(price).toFixed(2)}</div>
        </div>
    )
}

// Helper to get product price (handles multi-unit logic)
const getProductPrice = (product) => {
    // If we have product units, try to find the default one
    if (product.productUnits && product.productUnits.length > 0) {
        // Try to find default unit
        const defaultUnit = product.productUnits.find(u => u.isDefault);
        if (defaultUnit && defaultUnit.sellingPrice) {
            return defaultUnit.sellingPrice;
        }

        // If not, try base unit
        const baseUnit = product.productUnits.find(u => u.isBaseUnit);
        if (baseUnit && baseUnit.sellingPrice) {
            return baseUnit.sellingPrice;
        }
    }

    // Fallback to product direct price
    return product.salePrice;
}

// Component to be printed
const PrintableContent = ({ queue, currencySymbol }) => {
    return (
        <div className="p-4 bg-white">
            {/* Use a grid for printing */}
            <div className="grid grid-cols-3 gap-4 print:grid-cols-3">
                {queue.flatMap((item, itemIndex) =>
                    Array.from({ length: item.quantity }).map((_, copyIndex) => (
                        <div key={`${itemIndex}-${copyIndex}`} className="break-inside-avoid">
                            <BarcodeItem
                                value={item.product.barcode || item.product.sku || '000000'}
                                name={item.product.name}
                                price={getProductPrice(item.product)}
                                currencySymbol={currencySymbol}
                            />
                        </div>
                    ))
                )}
            </div>
        </div>
    )
}

export default function BarcodePrinting() {
    const { t } = useTranslation()
    const { confirm } = useConfirmDialog()
    const { showSuccess, showError } = useToast()
    const [categories, setCategories] = useState([])
    const [selectedMainCategory, setSelectedMainCategory] = useState('')
    const [selectedSubCategory, setSelectedSubCategory] = useState('')
    const [products, setProducts] = useState([])
    const [printQueue, setPrintQueue] = useState([])
    const [loading, setLoading] = useState(false)
    const [searchTerm] = useState('')
    const [settings, setSettings] = useState({ currencySymbol: '$' })
    const [selectedProducts, setSelectedProducts] = useState(new Set())

    // For printing
    const componentRef = useRef(null)
    const handlePrint = useReactToPrint({
        contentRef: componentRef,
        documentTitle: 'Barcode Labels',
    })

    const fetchSettings = async () => {
        try {
            const response = await api.get('/settings')
            if (response.data) {
                setSettings(prev => ({ ...prev, ...response.data }))
            }
        } catch (error) {
            console.error('Error fetching settings:', error)
        }
    }

    const fetchCategories = async () => {
        try {
            const response = await api.get('/categories/tree')
            setCategories(response.data || [])
        } catch (error) {
            console.error('Error fetching categories:', error)
            showError(t('common.error'))
        }

    }

    useEffect(() => {
        fetchCategories()
        fetchSettings()
    }, [])

    const fetchProducts = async () => {
        setLoading(true)
        try {
            const categoryId = selectedSubCategory || selectedMainCategory

            const params = new URLSearchParams()

            if (categoryId) {
                params.append('categoryId', categoryId)
            }
            if (searchTerm) {
                params.append('search', searchTerm)
            }
            params.append('isActive', 'true')

            const response = await fetchAllPagedArray('/products', Object.fromEntries(params.entries()))
            setProducts(response || [])
            if ((response?.length ?? 0) === 0) {
                showSuccess(t('barcodePrinting.productsFound') + ': 0')
            }
        } catch (error) {
            console.error('Error fetching products:', error)
            showError(t('products.failedToLoad'))
        } finally {
            setLoading(false)
        }
    }

    const getSubCategories = () => {
        if (!selectedMainCategory) return []
        const main = categories.find(c => c.id === selectedMainCategory)
        return main?.children || []
    }

    const handleAddToQueue = (product) => {
        setPrintQueue(prev => {
            const existing = prev.find(item => item.product.id === product.id)
            if (existing) {
                return prev.map(item =>
                    item.product.id === product.id
                        ? { ...item, quantity: item.quantity + 1 }
                        : item
                )
            }
            return [...prev, { product, quantity: 1 }]
        })
        showSuccess(t('barcodePrinting.addedToQueue'))
    }

    const removeFromQueue = (index) => {
        setPrintQueue(prev => prev.filter((_, i) => i !== index))
    }

    const updateQueueQuantity = (index, newQty) => {
        if (newQty < 1) return
        setPrintQueue(prev => prev.map((item, i) =>
            i === index ? { ...item, quantity: parseInt(newQty) } : item
        ))
    }

    const clearQueue = async () => {
        const confirmed = await confirm({
            confirmLabel: t('barcodePrinting.clearQueue'),
            description: t('barcodePrinting.confirmQueueClear'),
            title: t('common.warning'),
            variant: 'warning',
        })

        if (!confirmed) {
            return
        }

        setPrintQueue([])
        showSuccess(t('barcodePrinting.queueCleared'))
    }

    const formatPrice = (price) => {
        return `${settings.currencySymbol}${parseFloat(price).toFixed(2)}`
    }

    // Select all / deselect all
    const handleSelectAll = () => {
        if (selectedProducts.size === products.length) {
            setSelectedProducts(new Set())
        } else {
            setSelectedProducts(new Set(products.map(p => p.id)))
        }
    }

    // Toggle single product selection
    const toggleProductSelection = (productId) => {
        setSelectedProducts(prev => {
            const newSet = new Set(prev)
            if (newSet.has(productId)) {
                newSet.delete(productId)
            } else {
                newSet.add(productId)
            }
            return newSet
        })
    }

    // Add all selected products to queue
    const handleAddSelectedToQueue = () => {
        if (selectedProducts.size === 0) return

        const productsToAdd = products.filter(p => selectedProducts.has(p.id))
        setPrintQueue(prev => {
            let newQueue = [...prev]
            productsToAdd.forEach(product => {
                const existingIndex = newQueue.findIndex(item => item.product.id === product.id)
                if (existingIndex >= 0) {
                    newQueue[existingIndex] = {
                        ...newQueue[existingIndex],
                        quantity: newQueue[existingIndex].quantity + 1
                    }
                } else {
                    newQueue.push({ product, quantity: 1 })
                }
            })
            return newQueue
        })
        setSelectedProducts(new Set())
        showSuccess(`${productsToAdd.length} ${t('barcodePrinting.addedToQueue')}`)
    }

    return (
        <div className="h-[calc(100vh-100px)] flex flex-col gap-4">
            <h1 className="text-2xl font-bold text-gray-800 flex items-center gap-2">
                <BarcodeIcon className="w-8 h-8" />
                {t('barcodePrinting.title')}
            </h1>

            <div className="flex flex-1 gap-6 overflow-hidden">
                {/* Left Panel: Selection */}
                <div className="w-2/3 flex flex-col gap-4 bg-white p-4 rounded-lg shadow h-full overflow-hidden">
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                        {/* Main Category Filter */}
                        <div className="flex flex-col gap-1">
                            <label className="text-sm font-medium text-gray-700">{t('barcodePrinting.mainCategory')}</label>
                            <select
                                className="input py-2"
                                value={selectedMainCategory}
                                onChange={(e) => {
                                    setSelectedMainCategory(e.target.value)
                                    setSelectedSubCategory('')
                                }}
                            >
                                <option value="">{t('common.all')}</option>
                                {categories.map(cat => (
                                    <option key={cat.id} value={cat.id}>{cat.name}</option>
                                ))}
                            </select>
                        </div>

                        {/* Sub Category Filter */}
                        <div className="flex flex-col gap-1">
                            <label className="text-sm font-medium text-gray-700">{t('barcodePrinting.subCategory')}</label>
                            <select
                                className="input py-2"
                                value={selectedSubCategory}
                                onChange={(e) => setSelectedSubCategory(e.target.value)}
                                disabled={!selectedMainCategory}
                            >
                                <option value="">{t('barcodePrinting.allSubCategories')}</option>
                                {getSubCategories().map(sub => (
                                    <option key={sub.id} value={sub.id}>{sub.name}</option>
                                ))}
                            </select>
                        </div>

                        {/* Search & Action */}
                        <div className="flex items-end gap-2">
                            <button
                                onClick={fetchProducts}
                                className="btn-primary flex items-center gap-2 w-full justify-center"
                                disabled={loading}
                            >
                                {loading ? t('common.loading') : (
                                    <>
                                        <Search size={18} />
                                        {t('barcodePrinting.search')}
                                    </>
                                )}
                            </button>
                        </div>
                    </div>

                    {/* Product List */}
                    <div className="flex-1 overflow-auto border rounded-lg">
                        <table className="w-full text-left border-collapse">
                            <thead className="bg-gray-50 sticky top-0 z-10">
                                <tr>
                                    <th className="p-3 text-sm font-medium text-gray-600 border-b w-12">
                                        <input
                                            type="checkbox"
                                            checked={products.length > 0 && selectedProducts.size === products.length}
                                            onChange={handleSelectAll}
                                            className="w-4 h-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                                            disabled={products.length === 0}
                                        />
                                    </th>
                                    <th className="p-3 text-sm font-medium text-gray-600 border-b">{t('barcodePrinting.product')}</th>
                                    <th className="p-3 text-sm font-medium text-gray-600 border-b">{t('barcodePrinting.sku')}</th>
                                    <th className="p-3 text-sm font-medium text-gray-600 border-b">{t('barcodePrinting.price')}</th>
                                    <th className="p-3 text-sm font-medium text-gray-600 border-b w-24">{t('common.actions')}</th>
                                </tr>
                            </thead>
                            <tbody>
                                {products.length > 0 ? (
                                    products.map(product => (
                                        <tr key={product.id} className={`hover:bg-gray-50 border-b last:border-0 ${selectedProducts.has(product.id) ? 'bg-blue-50' : ''}`}>
                                            <td className="p-3">
                                                <input
                                                    type="checkbox"
                                                    checked={selectedProducts.has(product.id)}
                                                    onChange={() => toggleProductSelection(product.id)}
                                                    className="w-4 h-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                                                />
                                            </td>
                                            <td className="p-3">
                                                <div className="font-medium text-gray-900">{product.name}</div>
                                                <div className="text-xs text-gray-500">{product.barcode || t('barcodePrinting.noBarcode')}</div>
                                            </td>
                                            <td className="p-3 text-sm text-gray-600">{product.sku}</td>
                                            <td className="p-3 text-sm font-medium text-gray-900">{formatPrice(getProductPrice(product))}</td>
                                            <td className="p-3">
                                                <button
                                                    onClick={() => handleAddToQueue(product)}
                                                    className="p-2 text-blue-600 hover:bg-blue-50 rounded-full transition-colors"
                                                    title={t('barcodePrinting.addToPrintQueue')}
                                                >
                                                    <Plus size={20} />
                                                </button>
                                            </td>
                                        </tr>
                                    ))
                                ) : (
                                    <tr>
                                        <td colSpan="5" className="p-8 text-center text-gray-500">
                                            {loading ? t('common.loading') : t('products.noProducts')}
                                        </td>
                                    </tr>
                                )}
                            </tbody>
                        </table>
                    </div>

                    {/* Add Selected Button */}
                    {selectedProducts.size > 0 && (
                        <button
                            onClick={handleAddSelectedToQueue}
                            className="btn-primary flex items-center justify-center gap-2 py-2"
                        >
                            <CheckSquare size={18} />
                            {t('barcodePrinting.addToPrintQueue')} ({selectedProducts.size})
                        </button>
                    )}
                </div>

                {/* Right Panel: Print Queue */}
                <div className="w-1/3 flex flex-col bg-white rounded-lg shadow h-full overflow-hidden border-l-4 border-l-indigo-500">
                    <div className="p-4 border-b bg-gray-50 flex justify-between items-center">
                        <h2 className="font-bold text-lg text-gray-800 flex items-center gap-2">
                            <Printer size={20} className="text-indigo-600" />
                            {t('barcodePrinting.printQueue')}
                        </h2>
                        {printQueue.length > 0 && (
                            <button
                                onClick={clearQueue}
                                className="text-red-600 text-sm hover:underline"
                            >
                                {t('barcodePrinting.clearQueue')}
                            </button>
                        )}
                    </div>

                    <div className="flex-1 overflow-auto p-4 space-y-3">
                        {printQueue.length > 0 ? (
                            printQueue.map((item, index) => (
                                <div key={index} className="flex flex-col gap-2 p-3 bg-gray-50 rounded border border-gray-200">
                                    <div className="flex justify-between items-start">
                                        <div className="font-medium truncate pr-2">{item.product.name}</div>
                                        <button
                                            onClick={() => removeFromQueue(index)}
                                            className="text-gray-400 hover:text-red-500"
                                        >
                                            <Trash2 size={16} />
                                        </button>
                                    </div>
                                    <div className="flex items-center justify-between text-sm">
                                        <span className="text-gray-500">{item.product.barcode || item.product.sku}</span>
                                        <div className="flex items-center gap-2">
                                            <span className="text-xs text-gray-600">{t('barcodePrinting.quantity')}:</span>
                                            <input
                                                type="number"
                                                min="1"
                                                value={item.quantity}
                                                onChange={(e) => updateQueueQuantity(index, e.target.value)}
                                                className="w-16 p-1 text-center border rounded focus:ring-1 focus:ring-blue-500"
                                            />
                                        </div>
                                    </div>
                                </div>
                            ))
                        ) : (
                            <div className="h-full flex flex-col items-center justify-center text-gray-400 text-sm p-8 text-center">
                                <Printer size={48} className="mb-2 opacity-20" />
                                <p>{t('barcodePrinting.noProductsSelected')}</p>
                            </div>
                        )}
                    </div>

                    <div className="p-4 border-t bg-gray-50">
                        <div className="mb-4 flex justify-between text-sm font-medium">
                            <span className="text-gray-600">{t('common.total')} {t('inventory.products')}:</span>
                            <span>{printQueue.length}</span>
                        </div>
                        <div className="mb-4 flex justify-between text-sm font-medium">
                            <span className="text-gray-600">{t('common.total')} {t('barcodePrinting.labels')}:</span>
                            <span>{printQueue.reduce((acc, item) => acc + item.quantity, 0)}</span>
                        </div>
                        <button
                            onClick={handlePrint}
                            disabled={printQueue.length === 0}
                            className="w-full btn-primary py-3 flex items-center justify-center gap-2 font-bold shadow-lg disabled:opacity-50 disabled:cursor-not-allowed"
                        >
                            <Printer size={20} />
                            {t('barcodePrinting.print')}
                        </button>
                    </div>
                </div>
            </div>

            {/* Hidden component for printing - Using absolute positioning to ensure it's in the DOM for ref to attach */}
            <div style={{ position: 'absolute', top: '-10000px', left: '-10000px', height: 0, overflow: 'hidden' }}>
                <div ref={componentRef}>
                    <PrintableContent
                        queue={printQueue}
                        currencySymbol={settings.currencySymbol}
                    />
                </div>
            </div>
        </div>
    )
}

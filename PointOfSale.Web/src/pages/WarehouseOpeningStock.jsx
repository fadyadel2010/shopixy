import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import api from '../services/api'
import { useToast } from '../components/Toast'

/**
 * PHASE 11.5 - Warehouse Opening Stock (VARIANT-CENTRIC)
 * 
 * Stock is managed at the VARIANT level, not the product level.
 * Each variant has its own stock per warehouse.
 */
const WarehouseOpeningStock = () => {
    const { t } = useTranslation()
    const { confirm } = useConfirmDialog()
    const { showSuccess, showError } = useToast()

    const [warehouses, setWarehouses] = useState([])
    const [selectedWarehouse, setSelectedWarehouse] = useState('')

    // Mode Selection
    const [mode, setMode] = useState('bulk') // 'single' or 'bulk' - Default to bulk for variants

    // PHASE 11.5: Variant-based items (not product-based)
    const [variants, setVariants] = useState([]) // All variants for selection
    const [selectedVariant, setSelectedVariant] = useState('')
    const [quantity, setQuantity] = useState('')
    const [notes, setNotes] = useState('')
    const [items, setItems] = useState([]) // Items to save
    const [variantSearch, setVariantSearch] = useState('')

    // Bulk Mode State - Now variant-based
    const [bulkItems, setBulkItems] = useState([])
    const [bulkSearch, setBulkSearch] = useState('')

    const [loading, setLoading] = useState(false)
    const [saving, setSaving] = useState(false)

    const handleModeSwitch = async (nextMode) => {
        if (items.length > 0) {
            const confirmed = await confirm({
                confirmLabel: t('common.confirm'),
                description: 'Switching modes will clear your current list. Continue?',
                title: t('common.warning'),
                variant: 'warning',
            })

            if (!confirmed) {
                return
            }
        }

        setMode(nextMode)
        setItems([])
    }

    useEffect(() => {
        loadWarehouses()
    }, [])

    // PHASE 11.5: Load variants when warehouse is selected
    useEffect(() => {
        if (selectedWarehouse) {
            loadWarehouseVariants(selectedWarehouse)
        }
    }, [selectedWarehouse])

    const loadWarehouses = async () => {
        try {
            const response = await api.get('/warehouses')
            setWarehouses(response.data)
        } catch (error) {
            console.error('Failed to load warehouses', error)
            showError(t('openingBalance.failedToLoadWarehouses'))
        }
    }

    // PHASE 11.5: Load VARIANTS with their opening stock status
    const loadWarehouseVariants = async (warehouseId) => {
        setLoading(true)
        try {
            const response = await api.get(`/warehouses/opening-stock?warehouseId=${warehouseId}`)
            // Map response to variant-based bulk items
            const mappedItems = response.data.map(item => ({
                variantId: item.variantId,
                productId: item.productId,
                productName: item.productName,
                variantName: item.variantName,
                sku: item.sku,
                isDefaultVariant: item.isDefaultVariant,
                quantity: item.openingQuantity || '',
                hasOpeningStock: item.hasOpeningStock,
                notes: ''
            }))
            setBulkItems(mappedItems)
            setVariants(mappedItems) // For single mode dropdown
        } catch (error) {
            console.error('Failed to load warehouse variants', error)
            showError(t('openingBalance.failedToLoadProducts'))
        } finally {
            setLoading(false)
        }
    }

    // --- Single Mode Handlers (now variant-based) ---

    const handleVariantSelect = (variantId) => {
        setSelectedVariant(variantId)
    }

    // PHASE 11.5: Add variant item (not product)
    const handleAddItem = () => {
        if (!selectedWarehouse) {
            showError(t('openingBalance.pleaseSelectWarehouse'))
            return
        }

        if (!selectedVariant) {
            showError(t('openingBalance.pleaseSelectVariant') || 'Please select a variant')
            return
        }

        if (!quantity || parseFloat(quantity) <= 0) {
            showError(t('openingBalance.pleaseEnterValidQuantity'))
            return
        }

        const variant = variants.find(v => v.variantId === selectedVariant)
        if (!variant) return

        // Check if variant already in list
        if (items.some(item => item.variantId === selectedVariant)) {
            showError(t('openingBalance.variantAlreadyAdded') || 'Variant already added')
            return
        }

        const newItem = {
            variantId: selectedVariant,
            productId: variant.productId,
            variantName: variant.variantName,
            sku: variant.sku,
            quantity: parseFloat(quantity),
            costPrice: null,
            notes: notes
        }

        setItems([...items, newItem])

        // Reset form
        setSelectedVariant('')
        setQuantity('')
        setNotes('')
        setVariantSearch('')
    }

    const handleRemoveItem = (variantId) => {
        setItems(items.filter(item => item.variantId !== variantId))
    }

    // PHASE 11.5: Submit uses variantId
    const handleSingleSubmit = async () => {
        if (!selectedWarehouse) {
            showError(t('openingBalance.pleaseSelectWarehouse'))
            return
        }

        if (items.length === 0) {
            showError(t('openingBalance.noItemsToSave'))
            return
        }

        setSaving(true)
        try {
            const requestData = {
                warehouseId: selectedWarehouse,
                items: items.map(item => ({
                    variantId: item.variantId,  // PHASE 11.5: Use variantId
                    quantity: item.quantity,
                    costPrice: item.costPrice,
                    notes: item.notes
                }))
            }

            await api.post('/warehouses/opening-stock', requestData)
            showSuccess(t('openingBalance.stockSaved', { count: items.length }))

            // Reset
            setItems([])
            loadWarehouseVariants(selectedWarehouse)
        } catch (error) {
            showError(error.response?.data?.message || t('openingBalance.failed'))
        } finally {
            setSaving(false)
        }
    }

    // --- Bulk Mode Handlers (Variant-based) ---

    const handleBulkChange = (variantId, field, value) => {
        setBulkItems(prev => prev.map(item =>
            item.variantId === variantId ? { ...item, [field]: value } : item
        ))
    }

    const handleBulkSubmit = async () => {
        if (!selectedWarehouse) {
            showError(t('openingBalance.pleaseSelectWarehouse'))
            return
        }

        // PHASE 11.5: Filter variants that have quantity > 0 and don't have opening stock yet
        const itemsToSave = bulkItems.filter(item =>
            item.quantity && parseFloat(item.quantity) > 0 && !item.hasOpeningStock
        )

        if (itemsToSave.length === 0) {
            showError(t('openingBalance.noItemsToSave'))
            return
        }

        setSaving(true)
        try {
            const requestData = {
                warehouseId: selectedWarehouse,
                items: itemsToSave.map(item => ({
                    variantId: item.variantId,  // PHASE 11.5: Use variantId
                    quantity: parseFloat(item.quantity),
                    costPrice: null,
                    notes: item.notes
                }))
            }

            await api.post('/warehouses/opening-stock', requestData)
            showSuccess(t('openingBalance.stockSaved', { count: itemsToSave.length }))

            // Reload to update status
            loadWarehouseVariants(selectedWarehouse)
        } catch (error) {
            showError(error.response?.data?.message || t('openingBalance.failed'))
        } finally {
            setSaving(false)
        }
    }

    // --- Filtering (Variant-based) ---

    const filteredVariants = variants.filter(v => {
        const matchesSearch = v.variantName.toLowerCase().includes(variantSearch.toLowerCase()) ||
            (v.sku && v.sku.toLowerCase().includes(variantSearch.toLowerCase()))

        // Filter out variants that already have opening stock
        return matchesSearch && !v.hasOpeningStock
    })

    const filteredBulkItems = bulkItems.filter(v =>
        v.variantName.toLowerCase().includes(bulkSearch.toLowerCase()) ||
        (v.sku && v.sku.toLowerCase().includes(bulkSearch.toLowerCase()))
    )


    return (
        <div className="p-6">
            <h1 className="text-2xl font-bold text-gray-900 mb-6">
                {t('openingBalance.warehouseStockTitle')}
            </h1>

            <div className="bg-white rounded-lg shadow-md p-6">
                <div className="mb-4 bg-blue-50 border-l-4 border-blue-400 p-4">
                    <p className="text-sm text-blue-700">
                        <strong>{t('openingBalance.instructions')}</strong> {mode === 'single' ? t('openingBalance.instructionsTextWarehouse') : t('openingBalance.bulkInstructions')}
                    </p>
                </div>

                {/* Warehouse Selection */}
                <div className="mb-6 grid grid-cols-1 md:grid-cols-2 gap-4 items-end">
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('openingBalance.warehouse')} <span className="text-red-500">*</span>
                        </label>
                        <select
                            value={selectedWarehouse}
                            onChange={(e) => setSelectedWarehouse(e.target.value)}
                            className="block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-blue-500 focus:border-blue-500 sm:text-sm"
                            disabled={items.length > 0 && mode === 'single'}
                        >
                            <option value="">{t('openingBalance.selectWarehouse')}</option>
                            {warehouses.map(warehouse => (
                                <option key={warehouse.id} value={warehouse.id}>
                                    {warehouse.name}
                                </option>
                            ))}
                        </select>
                        {items.length > 0 && mode === 'single' && (
                            <p className="mt-1 text-xs text-gray-500">
                                {t('openingBalance.warehouseLocked')}
                            </p>
                        )}
                    </div>

                    {/* Mode Toggle */}
                    {selectedWarehouse && (
                        <div className="flex bg-gray-100 p-1 rounded-lg w-fit">
                            <button
                                onClick={() => void handleModeSwitch('single')}
                                className={`px-4 py-2 text-sm font-medium rounded-md transition-colors ${mode === 'single'
                                    ? 'bg-white text-blue-600 shadow-sm'
                                    : 'text-gray-500 hover:text-gray-700'
                                    }`}
                            >
                                {t('openingBalance.singleEntry')}
                            </button>
                            <button
                                onClick={() => void handleModeSwitch('bulk')}
                                className={`px-4 py-2 text-sm font-medium rounded-md transition-colors ${mode === 'bulk'
                                    ? 'bg-white text-blue-600 shadow-sm'
                                    : 'text-gray-500 hover:text-gray-700'
                                    }`}
                            >
                                {t('openingBalance.bulkEntry')}
                            </button>
                        </div>
                    )}
                </div>

                {/* SINGLE MODE */}
                {mode === 'single' && selectedWarehouse && (
                    <>
                        <div className="border-t border-gray-200 pt-6 mb-6">
                            <h2 className="text-lg font-semibold text-gray-900 mb-4">
                                {t('openingBalance.addVariant') || 'Add Variant Stock'}
                            </h2>

                            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-4">
                                {/* PHASE 11.5: Variant Selection */}
                                <div className="lg:col-span-2">
                                    <label className="block text-sm font-medium text-gray-700 mb-1">
                                        {t('openingBalance.variant') || 'Variant'} <span className="text-red-500">*</span>
                                    </label>
                                    <input
                                        type="text"
                                        value={variantSearch}
                                        onChange={(e) => setVariantSearch(e.target.value)}
                                        placeholder={t('openingBalance.searchVariant') || 'Search variants...'}
                                        className="block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-blue-500 focus:border-blue-500 sm:text-sm mb-2"
                                    />
                                    <select
                                        value={selectedVariant}
                                        onChange={(e) => handleVariantSelect(e.target.value)}
                                        className="block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-blue-500 focus:border-blue-500 sm:text-sm"
                                        size={5}
                                    >
                                        <option value="">{t('openingBalance.selectVariant') || 'Select Variant'}</option>
                                        {filteredVariants.slice(0, 50).map(variant => (
                                            <option key={variant.variantId} value={variant.variantId}>
                                                {variant.variantName} ({variant.sku || '-'})
                                            </option>
                                        ))}
                                        {filteredVariants.length === 0 && (
                                            <option disabled>No variants found</option>
                                        )}
                                    </select>
                                </div>

                                {/* Quantity */}
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-1">
                                        {t('openingBalance.quantity')} <span className="text-red-500">*</span>
                                    </label>
                                    <input
                                        type="number"
                                        step="0.01"
                                        min="0"
                                        value={quantity}
                                        onChange={(e) => setQuantity(e.target.value)}
                                        className="block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-blue-500 focus:border-blue-500 sm:text-sm"
                                        placeholder="0.00"
                                    />
                                </div>



                                {/* Add Button */}
                                <div className="flex items-end">
                                    <button
                                        type="button"
                                        onClick={handleAddItem}
                                        className="w-full inline-flex justify-center items-center px-4 py-2 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-green-600 hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500"
                                    >
                                        ➕ {t('openingBalance.add')}
                                    </button>
                                </div>
                            </div>

                            {/* Notes (full width) */}
                            <div className="mt-4">
                                <label className="block text-sm font-medium text-gray-700 mb-1">
                                    {t('openingBalance.notes')}
                                </label>
                                <input
                                    type="text"
                                    value={notes}
                                    onChange={(e) => setNotes(e.target.value)}
                                    className="block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-blue-500 focus:border-blue-500 sm:text-sm"
                                    placeholder={t('openingBalance.optionalNotes')}
                                />
                            </div>
                        </div>

                        {/* Items Table - PHASE 11.5: Variant-based */}
                        {items.length > 0 && (
                            <div className="border-t border-gray-200 pt-6">
                                <h2 className="text-lg font-semibold text-gray-900 mb-4">
                                    {t('openingBalance.itemsToSave')} ({items.length})
                                </h2>

                                <div className="overflow-x-auto">
                                    <table className="min-w-full divide-y divide-gray-200">
                                        <thead className="bg-gray-50">
                                            <tr>
                                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                    {t('openingBalance.sku') || 'SKU'}
                                                </th>
                                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                    {t('openingBalance.variantName') || 'Variant'}
                                                </th>
                                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                    {t('openingBalance.quantity')}
                                                </th>
                                                <th className="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                    {t('openingBalance.actions')}
                                                </th>
                                            </tr>
                                        </thead>
                                        <tbody className="bg-white divide-y divide-gray-200">
                                            {items.map((item) => (
                                                <tr key={item.variantId}>
                                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                        {item.sku || '-'}
                                                    </td>
                                                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                                        {item.variantName}
                                                    </td>
                                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-right">
                                                        {item.quantity.toLocaleString()}
                                                    </td>
                                                    <td className="px-6 py-4 whitespace-nowrap text-center text-sm">
                                                        <button
                                                            type="button"
                                                            onClick={() => handleRemoveItem(item.variantId)}
                                                            className="text-red-600 hover:text-red-900 font-medium"
                                                        >
                                                            🗑️ {t('openingBalance.delete')}
                                                        </button>
                                                    </td>
                                                </tr>
                                            ))}
                                        </tbody>
                                        <tfoot className="bg-gray-50">

                                        </tfoot>
                                    </table>
                                </div>

                                {/* Save Button */}
                                <div className="mt-6 flex justify-end">
                                    <button
                                        type="button"
                                        onClick={handleSingleSubmit}
                                        disabled={saving}
                                        className={`inline-flex justify-center py-2 px-6 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 ${saving ? 'opacity-50 cursor-not-allowed' : ''
                                            }`}
                                    >
                                        {saving ? t('openingBalance.savingStock') : t('openingBalance.saveOpeningStock')}
                                    </button>
                                </div>
                            </div>
                        )}
                    </>
                )}

                {/* BULK MODE */}
                {mode === 'bulk' && selectedWarehouse && (
                    <div className="mt-6">
                        {loading ? (
                            <div className="text-center py-8">{t('openingBalance.loadingVariants') || 'Loading variants...'}</div>
                        ) : (
                            <>
                                <div className="mb-4">
                                    <input
                                        type="text"
                                        value={bulkSearch}
                                        onChange={(e) => setBulkSearch(e.target.value)}
                                        placeholder={t('openingBalance.searchVariant') || 'Search variants...'}
                                        className="block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-blue-500 focus:border-blue-500 sm:text-sm"
                                    />
                                </div>

                                <div className="overflow-x-auto max-h-[600px]">
                                    <table className="min-w-full divide-y divide-gray-200 relative">
                                        <thead className="bg-gray-50 sticky top-0 z-10">
                                            <tr>
                                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                    {t('openingBalance.sku') || 'SKU'}
                                                </th>
                                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                    {t('openingBalance.variantName') || 'Variant'}
                                                </th>
                                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider w-32">
                                                    {t('openingBalance.quantity')}
                                                </th>
                                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                    {t('openingBalance.notes')}
                                                </th>
                                            </tr>
                                        </thead>
                                        <tbody className="bg-white divide-y divide-gray-200">
                                            {filteredBulkItems.map((item) => (
                                                <tr key={item.variantId} className={item.hasOpeningStock ? 'bg-gray-50' : ''}>
                                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                        {item.sku || '-'}
                                                    </td>
                                                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                                        {item.variantName}
                                                        {item.hasOpeningStock && (
                                                            <span className="ml-2 inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-green-100 text-green-800">
                                                                ✓ Set
                                                            </span>
                                                        )}
                                                    </td>
                                                    <td className="px-6 py-2 whitespace-nowrap text-sm text-gray-900">
                                                        <input
                                                            type="number"
                                                            step="0.01"
                                                            min="0"
                                                            value={item.quantity}
                                                            onChange={(e) => handleBulkChange(item.variantId, 'quantity', e.target.value)}
                                                            disabled={item.hasOpeningStock}
                                                            className="block w-full px-2 py-1 border border-gray-300 rounded-md focus:ring-blue-500 focus:border-blue-500 sm:text-sm text-right disabled:bg-gray-100"
                                                            placeholder="0"
                                                        />
                                                    </td>
                                                    <td className="px-6 py-2 whitespace-nowrap text-sm text-gray-900">
                                                        <input
                                                            type="text"
                                                            value={item.notes}
                                                            onChange={(e) => handleBulkChange(item.variantId, 'notes', e.target.value)}
                                                            disabled={item.hasOpeningStock}
                                                            className="block w-full px-2 py-1 border border-gray-300 rounded-md focus:ring-blue-500 focus:border-blue-500 sm:text-sm disabled:bg-gray-100"
                                                            placeholder="..."
                                                        />
                                                    </td>
                                                </tr>
                                            ))}
                                            {filteredBulkItems.length === 0 && (
                                                <tr>
                                                    <td colSpan="4" className="px-6 py-4 text-center text-sm text-gray-500">
                                                        No variants found
                                                    </td>
                                                </tr>
                                            )}
                                        </tbody>
                                    </table>
                                </div>

                                <div className="mt-6 flex justify-end bg-gray-50 p-4 rounded-b-lg border-t border-gray-200 sticky bottom-0">
                                    <button
                                        type="button"
                                        onClick={handleBulkSubmit}
                                        disabled={saving}
                                        className={`inline-flex justify-center py-2 px-6 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 ${saving ? 'opacity-50 cursor-not-allowed' : ''
                                            }`}
                                    >
                                        {saving ? t('openingBalance.savingStock') : t('openingBalance.saveOpeningStock')}
                                    </button>
                                </div>
                            </>
                        )}
                    </div>
                )}

                {items.length === 0 && selectedWarehouse && mode === 'single' && (
                    <div className="text-center py-8 text-gray-500 border-t border-gray-200">
                        {t('openingBalance.noItemsAdded')}
                    </div>
                )}
            </div>
        </div>
    )
}

export default WarehouseOpeningStock

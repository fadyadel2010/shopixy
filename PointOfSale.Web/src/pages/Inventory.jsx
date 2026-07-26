import { useState, useEffect, useMemo, useCallback, useRef } from 'react'
import api, { isRequestCanceled } from '../services/api'
import { useToast } from '../components/Toast'
import { useTranslation } from 'react-i18next'
import { useRTL } from '../hooks/useRTL'
import { useDebounce } from '../hooks/useDebounce'
import { toSafeNumber } from '../utils/financialMath'
import { getWarehouseOpeningVariants, getWarehouseStockLevels } from '../services/posDataService'
import { invalidatePosRuntimeCache, subscribeToPosRuntimeInvalidation } from '../services/posRuntimeCache'

/**
 * PHASE 11.5: INVENTORY MANAGEMENT (VARIANT-CENTRIC)
 *
 * Inventory is managed at the VARIANT level, not the product level.
 * Each variant has its own stock per warehouse.
 *
 * This screen MUST operate on VariantId, not ProductId.
 * Products never hold stock directly.
 */
export default function Inventory() {
    const { t } = useTranslation()
    const { isRTL, dir } = useRTL()
    const [stockLevels, setStockLevels] = useState([])
    const [warehouses, setWarehouses] = useState([])
    const [variants, setVariants] = useState([])
    const [loading, setLoading] = useState(true)
    const [isBootstrapped, setIsBootstrapped] = useState(false)
    const [tableError, setTableError] = useState('')
    const [variantsLoading, setVariantsLoading] = useState(false)
    const [variantsError, setVariantsError] = useState('')
    const [selectedWarehouse, setSelectedWarehouse] = useState('')
    const [searchTerm, setSearchTerm] = useState('')
    const debouncedSearchTerm = useDebounce(searchTerm, 300)

    const [showAdjustModal, setShowAdjustModal] = useState(false)
    const [showTransferModal, setShowTransferModal] = useState(false)

    const [adjustForm, setAdjustForm] = useState({
        warehouseId: '',
        variantId: '',
        quantityChange: 0,
        type: 'Adjustment',
        reason: '',
    })

    const [transferForm, setTransferForm] = useState({
        sourceWarehouseId: '',
        targetWarehouseId: '',
        variantId: '',
        quantity: 0,
        notes: '',
    })

    const { showSuccess, showError } = useToast()
    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const selectSpacingClass = isRTL ? 'mr-2' : 'ml-2'

    const stockRequestIdRef = useRef(0)
    const variantsRequestIdRef = useRef(0)
    const stockRequestControllerRef = useRef(null)
    const variantsRequestControllerRef = useRef(null)

    const parseQuantityInput = (value) => {
        if (typeof value === 'string') {
            return value.trim()
        }
        return ''
    }

    const loadVariantsForWarehouse = useCallback(async (warehouseId, { force = false } = {}) => {
        if (!warehouseId) {
            variantsRequestControllerRef.current?.abort()
            variantsRequestIdRef.current += 1
            setVariants([])
            setVariantsLoading(false)
            setVariantsError('')
            return
        }

        variantsRequestControllerRef.current?.abort()
        const controller = new AbortController()
        variantsRequestControllerRef.current = controller

        variantsRequestIdRef.current += 1
        const requestId = variantsRequestIdRef.current

        try {
            setVariantsLoading(true)
            setVariantsError('')

            const nextVariants = await getWarehouseOpeningVariants({
                warehouseId,
                signal: controller.signal,
                force,
            })

            if (requestId !== variantsRequestIdRef.current || controller.signal.aborted) {
                return
            }

            setVariants(nextVariants)
        } catch (error) {
            if (requestId !== variantsRequestIdRef.current || controller.signal.aborted || isRequestCanceled(error)) {
                return
            }

            console.error('Error fetching variants:', error)
            setVariants([])
            setVariantsError(error.response?.data?.message || t('inventoryManagement.failedToLoadStock'))
        } finally {
            if (requestId === variantsRequestIdRef.current && variantsRequestControllerRef.current === controller) {
                variantsRequestControllerRef.current = null
                setVariantsLoading(false)
            }
        }
    }, [t])

    const fetchStockLevels = useCallback(async (warehouseId, { force = false } = {}) => {
        if (!warehouseId) {
            stockRequestControllerRef.current?.abort()
            stockRequestIdRef.current += 1
            setStockLevels([])
            setLoading(false)
            setTableError('')
            return
        }

        stockRequestControllerRef.current?.abort()
        const controller = new AbortController()
        stockRequestControllerRef.current = controller

        stockRequestIdRef.current += 1
        const requestId = stockRequestIdRef.current

        try {
            setLoading(true)
            setTableError('')

            const nextStockLevels = await getWarehouseStockLevels({
                warehouseId,
                signal: controller.signal,
                force,
            })

            if (requestId !== stockRequestIdRef.current || controller.signal.aborted) {
                return
            }

            setStockLevels(nextStockLevels)
        } catch (error) {
            if (requestId !== stockRequestIdRef.current || controller.signal.aborted || isRequestCanceled(error)) {
                return
            }

            console.error('Error fetching stock levels:', error)
            setStockLevels([])
            setTableError(error.response?.data?.message || t('inventoryManagement.failedToLoadStock'))
            showError(t('inventoryManagement.failedToLoadStock'))
        } finally {
            if (requestId === stockRequestIdRef.current && stockRequestControllerRef.current === controller) {
                stockRequestControllerRef.current = null
                setLoading(false)
            }
        }
    }, [showError, t])

    const fetchInitialData = useCallback(async () => {
        try {
            setLoading(true)
            setTableError('')

            const warehousesRes = await api.get('/warehouses')
            const nextWarehouses = Array.isArray(warehousesRes.data) ? warehousesRes.data : []

            setWarehouses(nextWarehouses)
            setSelectedWarehouse((currentWarehouseId) => {
                if (currentWarehouseId && nextWarehouses.some(warehouse => warehouse.id === currentWarehouseId)) {
                    return currentWarehouseId
                }

                return nextWarehouses[0]?.id || ''
            })
            setIsBootstrapped(true)
        } catch (error) {
            console.error('Error fetching initial data:', error)
            setWarehouses([])
            setStockLevels([])
            setTableError(t('inventoryManagement.failedToLoadStock'))
            showError(t('inventoryManagement.failedToLoadStock'))
            setLoading(false)
            setIsBootstrapped(true)
        }
    }, [showError, t])

    useEffect(() => {
        void fetchInitialData()

        return () => {
            stockRequestControllerRef.current?.abort()
            variantsRequestControllerRef.current?.abort()
        }
    }, [fetchInitialData])

    useEffect(() => {
        if (!isBootstrapped) {
            return
        }

        if (warehouses.length === 0) {
            setStockLevels([])
            setVariants([])
            setLoading(false)
            return
        }

        if (!selectedWarehouse) {
            setSelectedWarehouse(warehouses[0].id)
            return
        }

        void fetchStockLevels(selectedWarehouse)
    }, [fetchStockLevels, isBootstrapped, selectedWarehouse, warehouses])

    useEffect(() => subscribeToPosRuntimeInvalidation(() => {
        if (!selectedWarehouse) {
            return
        }

        void fetchStockLevels(selectedWarehouse, { force: true })
    }), [fetchStockLevels, selectedWarehouse])

    const filteredStockLevels = useMemo(() => {
        if (!debouncedSearchTerm) return stockLevels

        const search = debouncedSearchTerm.toLowerCase()
        return stockLevels.filter(stock =>
            stock.variantName?.toLowerCase().includes(search)
            || stock.productName?.toLowerCase().includes(search)
            || stock.variantSku?.toLowerCase().includes(search),
        )
    }, [debouncedSearchTerm, stockLevels])

    const handleAdjustSubmit = useCallback(async (event) => {
        event.preventDefault()
        const quantityChange = toSafeNumber(adjustForm.quantityChange, Number.NaN)

        if (!Number.isFinite(quantityChange)) {
            showError(t('treasury.invalidAmount'))
            return
        }

        try {
            await api.post('/warehouses/adjust-stock', {
                ...adjustForm,
                quantityChange,
                productVariantId: adjustForm.variantId,
            })

            showSuccess(t('inventoryManagement.stockAdjusted'))
            setShowAdjustModal(false)
            invalidatePosRuntimeCache({ reason: 'stock-adjusted' })
        } catch (error) {
            console.error('Error adjusting stock:', error)
            const errorMessage = error?.response?.data?.message || error?.response?.data || error?.message || t('inventoryManagement.failedToAdjust')
            const displayMessage = typeof errorMessage === 'string' ? errorMessage : t('inventoryManagement.failedToAdjust')
            showError(displayMessage)
        }
    }, [adjustForm, showError, showSuccess, t])

    const handleTransferSubmit = useCallback(async (event) => {
        event.preventDefault()
        const quantity = toSafeNumber(transferForm.quantity, Number.NaN)

        if (!Number.isFinite(quantity) || quantity <= 0) {
            showError(t('treasury.invalidAmount'))
            return
        }

        // Look up the selected variant to get its productId
        const selectedVariant = variants.find(v => v.variantId === transferForm.variantId)
        if (!selectedVariant) {
            showError(t('inventoryManagement.variantNotFound'))
            return
        }

        try {
            await api.post('/warehouses/transfer-stock', {
                sourceWarehouseId: transferForm.sourceWarehouseId,
                targetWarehouseId: transferForm.targetWarehouseId,
                productId: selectedVariant.productId,
                productVariantId: transferForm.variantId,
                quantity,
                notes: transferForm.notes,
            })

            showSuccess(t('inventoryManagement.stockTransferred'))
            setShowTransferModal(false)
            invalidatePosRuntimeCache({ reason: 'stock-transferred' })
        } catch (error) {
            console.error('Error transferring stock:', error)
            const errorMessage = error?.response?.data?.message || error?.response?.data || error?.message || t('inventoryManagement.failedToTransfer')
            const displayMessage = typeof errorMessage === 'string' ? errorMessage : t('inventoryManagement.failedToTransfer')
            showError(displayMessage)
        }
    }, [showError, showSuccess, t, transferForm])

    return (
        <div dir={dir} className="h-full flex flex-col">
            <div className="mb-6 flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-800">{t('inventoryManagement.title')}</h1>
                    <p className="text-sm text-gray-500 mt-1">
                        {t('inventoryManagement.variantLevelHint')}
                    </p>
                </div>
                <div className="flex gap-3">
                    <button
                        onClick={() => {
                            setAdjustForm({
                                warehouseId: selectedWarehouse,
                                variantId: '',
                                quantityChange: 0,
                                type: 'Adjustment',
                                reason: '',
                            })
                            setVariantsError('')
                            void loadVariantsForWarehouse(selectedWarehouse, { force: true })
                            setShowAdjustModal(true)
                        }}
                        className="btn-secondary"
                    >
                        {t('inventoryManagement.adjustStock')}
                    </button>
                    <button
                        onClick={() => {
                            setTransferForm({
                                sourceWarehouseId: selectedWarehouse,
                                targetWarehouseId: '',
                                variantId: '',
                                quantity: 0,
                                notes: '',
                            })
                            setVariantsError('')
                            void loadVariantsForWarehouse(selectedWarehouse, { force: true })
                            setShowTransferModal(true)
                        }}
                        className="btn-primary"
                    >
                        {t('inventoryManagement.transferStock')}
                    </button>
                </div>
            </div>

            <div className="bg-white p-4 rounded-lg shadow mb-6">
                <div className="flex gap-4 items-center flex-wrap">
                    <div>
                        <label className="font-medium text-gray-700">{t('inventoryManagement.warehouse')}:</label>
                        <select
                            value={selectedWarehouse}
                            onChange={(event) => setSelectedWarehouse(event.target.value)}
                            className={`input max-w-xs ${selectSpacingClass}`}
                        >
                            {warehouses.map(w => (
                                <option key={w.id} value={w.id}>{w.name}</option>
                            ))}
                        </select>
                    </div>
                    <div className="flex-1 max-w-md">
                        <input
                            type="text"
                            value={searchTerm}
                            onChange={(event) => setSearchTerm(event.target.value)}
                            placeholder={t('inventoryManagement.searchPlaceholder')}
                            className="input w-full"
                        />
                    </div>
                </div>
            </div>

            <div className="bg-white rounded-lg shadow flex-1 overflow-hidden flex flex-col">
                <div className="overflow-x-auto flex-1">
                    <table className="w-full">
                        <thead className="bg-gray-50 border-b border-gray-200 sticky top-0">
                            <tr>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    {t('inventoryManagement.variant')}
                                </th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    {t('inventoryManagement.product')}
                                </th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    {t('inventoryManagement.sku')}
                                </th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('inventoryManagement.quantity')}
                                </th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('inventoryManagement.reserved')}
                                </th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('inventoryManagement.available')}
                                </th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {loading ? (
                                <tr>
                                    <td colSpan="6" className="px-6 py-6 text-center text-gray-500">
                                        {t('common.loading')}
                                    </td>
                                </tr>
                            ) : tableError ? (
                                <tr>
                                    <td colSpan="6" className="px-6 py-6">
                                        <div className={`flex flex-col items-center justify-center gap-3 ${isRTL ? 'text-right' : 'text-left'}`}>
                                            <div className="text-sm text-red-600">{tableError}</div>
                                            <button
                                                type="button"
                                                onClick={() => void fetchStockLevels(selectedWarehouse, { force: true })}
                                                className="btn-secondary"
                                            >
                                                {t('common.retry')}
                                            </button>
                                        </div>
                                    </td>
                                </tr>
                            ) : filteredStockLevels.length === 0 ? (
                                <tr>
                                    <td colSpan="6" className="px-6 py-4 text-center text-gray-500">
                                        {t('inventoryManagement.noStockRecords')}
                                    </td>
                                </tr>
                            ) : (
                                filteredStockLevels.map((stock) => (
                                    <tr key={stock.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <div className="text-sm font-medium text-gray-900">
                                                {stock.variantName}
                                            </div>
                                            {!stock.isDefaultVariant && (
                                                <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-blue-100 text-blue-800 mt-1">
                                                    {t('inventoryManagement.variantBadge')}
                                                </span>
                                            )}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            {stock.productName}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 font-mono">
                                            {stock.variantSku || '-'}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-bold text-gray-900">
                                            {stock.quantity}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-right text-sm text-gray-500">
                                            {stock.reservedQuantity}
                                        </td>
                                        <td className={`px-6 py-4 whitespace-nowrap text-right text-sm font-medium ${stock.availableQuantity > 0 ? 'text-green-600' : 'text-red-600'}`}>
                                            {stock.availableQuantity}
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>
            </div>

            {showAdjustModal && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4 p-6">
                        <h2 className="text-xl font-bold mb-4">{t('inventoryManagement.adjustStock')}</h2>
                        <p className="text-sm text-gray-500 mb-4">
                            {t('inventoryManagement.adjustVariantHelp')}
                        </p>
                        <form onSubmit={handleAdjustSubmit} className="space-y-4">
                            <div>
                                <label className="label">{t('inventoryManagement.warehouse')}</label>
                                <select
                                    value={adjustForm.warehouseId}
                                    onChange={(event) => {
                                        const nextWarehouseId = event.target.value
                                        setAdjustForm({ ...adjustForm, warehouseId: nextWarehouseId, variantId: '' })
                                        setVariantsError('')
                                        void loadVariantsForWarehouse(nextWarehouseId, { force: true })
                                    }}
                                    className="input w-full"
                                    required
                                >
                                    <option value="">{t('inventoryManagement.selectWarehouse')}</option>
                                    {warehouses.map(w => (
                                        <option key={w.id} value={w.id}>{w.name}</option>
                                    ))}
                                </select>
                            </div>
                            <div>
                                <label className="label">{t('inventoryManagement.variant')} *</label>
                                <select
                                    value={adjustForm.variantId}
                                    onChange={(event) => setAdjustForm({ ...adjustForm, variantId: event.target.value })}
                                    className="input w-full"
                                    required
                                    disabled={variantsLoading}
                                >
                                    <option value="">{t('inventoryManagement.selectVariant')}</option>
                                    {variants.map(v => (
                                        <option key={v.variantId} value={v.variantId}>
                                            {v.variantName} {v.sku ? `(${v.sku})` : ''}
                                        </option>
                                    ))}
                                </select>
                                {variantsLoading ? (
                                    <p className="text-xs text-gray-500 mt-1">{t('common.loading')}</p>
                                ) : null}
                                {!variantsLoading && variantsError ? (
                                    <div className="mt-2 flex items-center justify-between gap-3">
                                        <p className="text-xs text-red-600">{variantsError}</p>
                                        <button
                                            type="button"
                                            onClick={() => void loadVariantsForWarehouse(adjustForm.warehouseId, { force: true })}
                                            className="text-xs font-medium text-primary-700 hover:text-primary-800"
                                        >
                                            {t('common.retry')}
                                        </button>
                                    </div>
                                ) : null}
                            </div>
                            <div>
                                <label className="label">{t('inventoryManagement.quantityChange')}</label>
                                <input
                                    type="number"
                                    value={adjustForm.quantityChange}
                                    onChange={(event) => setAdjustForm({ ...adjustForm, quantityChange: parseQuantityInput(event.target.value) })}
                                    className="input w-full"
                                    required
                                />
                                <p className="text-xs text-gray-500 mt-1">{t('inventoryManagement.quantityChangeHelp')}</p>
                            </div>
                            <div>
                                <label className="label">{t('inventoryManagement.reason')}</label>
                                <input
                                    type="text"
                                    value={adjustForm.reason}
                                    onChange={(event) => setAdjustForm({ ...adjustForm, reason: event.target.value })}
                                    className="input w-full"
                                    required
                                />
                            </div>
                            <div className="flex justify-end gap-3 mt-6">
                                <button
                                    type="button"
                                    onClick={() => setShowAdjustModal(false)}
                                    className="btn-secondary"
                                >
                                    {t('warehouses.cancel')}
                                </button>
                                <button type="submit" className="btn-primary">
                                    {t('inventoryManagement.submitAdjustment')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {showTransferModal && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4 p-6">
                        <h2 className="text-xl font-bold mb-4">{t('inventoryManagement.transferStock')}</h2>
                        <p className="text-sm text-gray-500 mb-4">
                            {t('inventoryManagement.transferVariantHelp')}
                        </p>
                        <form onSubmit={handleTransferSubmit} className="space-y-4">
                            <div>
                                <label className="label">{t('inventoryManagement.sourceWarehouse')}</label>
                                <select
                                    value={transferForm.sourceWarehouseId}
                                    onChange={(event) => {
                                        const nextWarehouseId = event.target.value
                                        setTransferForm({
                                            ...transferForm,
                                            sourceWarehouseId: nextWarehouseId,
                                            variantId: '',
                                            targetWarehouseId: '',
                                        })
                                        setVariantsError('')
                                        void loadVariantsForWarehouse(nextWarehouseId, { force: true })
                                    }}
                                    className="input w-full"
                                    required
                                >
                                    <option value="">{t('inventoryManagement.selectSource')}</option>
                                    {warehouses.map(w => (
                                        <option key={w.id} value={w.id}>{w.name}</option>
                                    ))}
                                </select>
                            </div>
                            <div>
                                <label className="label">{t('inventoryManagement.targetWarehouse')}</label>
                                <select
                                    value={transferForm.targetWarehouseId}
                                    onChange={(event) => setTransferForm({ ...transferForm, targetWarehouseId: event.target.value })}
                                    className="input w-full"
                                    required
                                >
                                    <option value="">{t('inventoryManagement.selectTarget')}</option>
                                    {warehouses.filter(w => w.id !== transferForm.sourceWarehouseId).map(w => (
                                        <option key={w.id} value={w.id}>{w.name}</option>
                                    ))}
                                </select>
                            </div>
                            <div>
                                <label className="label">{t('inventoryManagement.variant')} *</label>
                                <select
                                    value={transferForm.variantId}
                                    onChange={(event) => setTransferForm({ ...transferForm, variantId: event.target.value })}
                                    className="input w-full"
                                    required
                                    disabled={variantsLoading}
                                >
                                    <option value="">{t('inventoryManagement.selectVariant')}</option>
                                    {variants.filter(v => v.hasOpeningStock).map(v => (
                                        <option key={v.variantId} value={v.variantId}>
                                            {v.variantName} {v.sku ? `(${v.sku})` : ''}
                                        </option>
                                    ))}
                                </select>
                                {variantsLoading ? (
                                    <p className="text-xs text-gray-500 mt-1">{t('common.loading')}</p>
                                ) : null}
                                {!variantsLoading && variantsError ? (
                                    <div className="mt-2 flex items-center justify-between gap-3">
                                        <p className="text-xs text-red-600">{variantsError}</p>
                                        <button
                                            type="button"
                                            onClick={() => void loadVariantsForWarehouse(transferForm.sourceWarehouseId, { force: true })}
                                            className="text-xs font-medium text-primary-700 hover:text-primary-800"
                                        >
                                            {t('common.retry')}
                                        </button>
                                    </div>
                                ) : null}
                            </div>
                            <div>
                                <label className="label">{t('inventoryManagement.quantity')}</label>
                                <input
                                    type="number"
                                    min="0.01"
                                    step="0.01"
                                    value={transferForm.quantity}
                                    onChange={(event) => setTransferForm({ ...transferForm, quantity: parseQuantityInput(event.target.value) })}
                                    className="input w-full"
                                    required
                                />
                            </div>
                            <div>
                                <label className="label">{t('inventoryManagement.notes')}</label>
                                <input
                                    type="text"
                                    value={transferForm.notes}
                                    onChange={(event) => setTransferForm({ ...transferForm, notes: event.target.value })}
                                    className="input w-full"
                                />
                            </div>
                            <div className="flex justify-end gap-3 mt-6">
                                <button
                                    type="button"
                                    onClick={() => setShowTransferModal(false)}
                                    className="btn-secondary"
                                >
                                    {t('warehouses.cancel')}
                                </button>
                                <button type="submit" className="btn-primary">
                                    {t('inventoryManagement.transfer')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    )
}

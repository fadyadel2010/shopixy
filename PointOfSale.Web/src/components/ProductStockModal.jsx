import { useState, useEffect, useCallback, useRef } from 'react'
import api from '../services/api'
import { useTranslation } from 'react-i18next'
import { invalidatePosRuntimeCache } from '../services/posRuntimeCache'

export default function ProductStockModal({ productId, productName, canAdjust = false, onClose }) {
    const { t } = useTranslation()
    const [loading, setLoading] = useState(true)
    const [stockLevels, setStockLevels] = useState([])
    const [adjusting, setAdjusting] = useState(false)
    const [error, setError] = useState('')
    const [adjustmentData, setAdjustmentData] = useState({
        warehouseId: '',
        quantityChange: '',
        reason: ''
    })
    const [showAdjustForm, setShowAdjustForm] = useState(false)
    const scope = useRef(null)
    const latestRead = useRef(0)
    const pendingAdjustment = useRef(false)

    const fetchStockLevels = useCallback(async () => {
        const target = scope.current
        const requestId = ++latestRead.current
        const isCurrent = () => target !== null && scope.current === target && latestRead.current === requestId
        try {
            setLoading(true)
            setError('')
            const response = await api.get(`/products/${productId}/stock`)
            if (!isCurrent()) return
            setStockLevels(response.data)
        } catch (err) {
            if (!isCurrent()) return
            setError(t('inventoryManagement.failedToLoadStock'))
            console.error('Failed to load product stock:', { status: err?.response?.status })
        } finally {
            if (isCurrent()) setLoading(false)
        }
    }, [productId, t])

    useEffect(() => {
        scope.current = {}
        pendingAdjustment.current = false
        setAdjusting(false)
        setShowAdjustForm(false)
        setAdjustmentData({ warehouseId: '', quantityChange: '', reason: '' })
        setStockLevels([])
        return () => { scope.current = null; latestRead.current += 1 }
    }, [productId])

    useEffect(() => {
        fetchStockLevels()
        return () => { latestRead.current += 1 }
    }, [fetchStockLevels])

    const handleAdjust = async (e) => {
        e.preventDefault()
        if (pendingAdjustment.current) return
        const target = scope.current
        pendingAdjustment.current = true
        setAdjusting(true)
        setError('')

        try {
            await api.post(`/products/${productId}/adjust-stock`, {
                warehouseId: adjustmentData.warehouseId,
                quantityChange: parseFloat(adjustmentData.quantityChange),
                reason: adjustmentData.reason
            })

            // A committed adjustment invalidates shared stock even if its modal closed.
            invalidatePosRuntimeCache({ reason: 'stock-adjusted' })
            if (scope.current !== target) return
            // Reset only this product's form and refresh its data.
            setAdjustmentData({ warehouseId: '', quantityChange: '', reason: '' })
            setShowAdjustForm(false)
            await fetchStockLevels()
        } catch (err) {
            if (scope.current !== target) return
            setError(err.response?.data?.message || t('inventoryManagement.failedToAdjust'))
        } finally {
            if (scope.current === target) { pendingAdjustment.current = false; setAdjusting(false) }
        }
    }

    const getTotalStock = () => {
        return stockLevels.reduce((sum, level) => sum + level.quantity, 0)
    }

    const getTotalAvailable = () => {
        return stockLevels.reduce((sum, level) => sum + level.availableQuantity, 0)
    }

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4">
            <div className="bg-white rounded-lg shadow-xl max-w-3xl w-full max-h-[90vh] overflow-y-auto">
                <div className="p-6">
                    <div className="flex justify-between items-start mb-6">
                        <div>
                            <h3 className="text-2xl font-bold text-gray-900">{productName}</h3>
                            <p className="text-sm text-gray-500 mt-1">{t('products.stockLevelsByWarehouse')}</p>
                        </div>
                        <button
                            onClick={onClose}
                            className="text-gray-400 hover:text-gray-600"
                        >
                            <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    </div>

                    {error && (
                        <div className="mb-4 p-3 bg-red-100 border border-red-400 text-red-700 rounded">
                            {error}
                        </div>
                    )}

                    {loading ? (
                        <div className="flex items-center justify-center py-12">
                            <div className="animate-spin h-8 w-8 border-4 border-primary-600 border-t-transparent rounded-full"></div>
                        </div>
                    ) : (
                        <>
                            {/* Summary Cards */}
                            <div className="grid grid-cols-2 gap-4 mb-6">
                                <div className="card bg-blue-50 border-blue-200">
                                    <div className="text-sm text-blue-600 font-medium">{t('products.totalStock')}</div>
                                    <div className="text-3xl font-bold text-blue-900 mt-1">{getTotalStock()}</div>
                                </div>
                                <div className="card bg-green-50 border-green-200">
                                    <div className="text-sm text-green-600 font-medium">{t('inventoryManagement.available')}</div>
                                    <div className="text-3xl font-bold text-green-900 mt-1">{getTotalAvailable()}</div>
                                </div>
                            </div>

                            {/* Stock Levels Table */}
                            <div className="card mb-4">
                                <h4 className="font-semibold mb-4">{t('products.warehouseStockLevels')}</h4>
                                {stockLevels.length === 0 ? (
                                    <p className="text-center py-8 text-gray-500">{t('inventoryManagement.noStockRecords')}</p>
                                ) : (
                                    <div className="overflow-x-auto">
                                        <table className="min-w-full divide-y divide-gray-200">
                                            <thead className="bg-gray-50">
                                                <tr>
                                                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('inventoryManagement.warehouse')}</th>
                                                    <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase">{t('products.totalQty')}</th>
                                                    <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase">{t('inventoryManagement.reserved')}</th>
                                                    <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase">{t('inventoryManagement.available')}</th>
                                                </tr>
                                            </thead>
                                            <tbody className="bg-white divide-y divide-gray-200">
                                                {stockLevels.map((level) => (
                                                    <tr key={level.warehouseId}>
                                                        <td className="px-4 py-3 text-sm font-medium text-gray-900">
                                                            {level.warehouseName}
                                                        </td>
                                                        <td className="px-4 py-3 text-sm text-right text-gray-900">
                                                            {level.quantity}
                                                        </td>
                                                        <td className="px-4 py-3 text-sm text-right text-gray-500">
                                                            {level.reservedQuantity}
                                                        </td>
                                                        <td className="px-4 py-3 text-sm text-right">
                                                            <span className={`font-semibold ${level.availableQuantity > 0 ? 'text-green-600' : 'text-red-600'}`}>
                                                                {level.availableQuantity}
                                                            </span>
                                                        </td>
                                                    </tr>
                                                ))}
                                            </tbody>
                                        </table>
                                    </div>
                                )}
                            </div>

                            {/* Adjust Stock Section */}
                            {canAdjust && (!showAdjustForm ? (
                                <button
                                    onClick={() => setShowAdjustForm(true)}
                                    className="btn-primary w-full"
                                >
                                    + {t('inventoryManagement.adjustStock')}
                                </button>
                            ) : (
                                <div className="card bg-gray-50">
                                    <h4 className="font-semibold mb-4">{t('inventoryManagement.adjustStock')}</h4>
                                    <form onSubmit={handleAdjust} className="space-y-4">
                                        <div>
                                            <label className="label">{t('inventoryManagement.warehouse')} *</label>
                                            <select
                                                value={adjustmentData.warehouseId}
                                                onChange={(e) => setAdjustmentData(prev => ({ ...prev, warehouseId: e.target.value }))}
                                                className="input"
                                                required
                                            >
                                                <option value="">{t('inventoryManagement.selectWarehouse')}</option>
                                                {stockLevels.map(level => (
                                                    <option key={level.warehouseId} value={level.warehouseId}>
                                                        {level.warehouseName}
                                                    </option>
                                                ))}
                                            </select>
                                        </div>
                                        <div>
                                            <label className="label">{t('inventoryManagement.quantityChange')} *</label>
                                            <input
                                                type="number"
                                                step="0.01"
                                                value={adjustmentData.quantityChange}
                                                onChange={(e) => setAdjustmentData(prev => ({ ...prev, quantityChange: e.target.value }))}
                                                className="input"
                                                placeholder={t('products.negativeForDecrease')}
                                                required
                                            />
                                            <p className="text-xs text-gray-500 mt-1">
                                                {t('inventoryManagement.quantityChangeHelp')}
                                            </p>
                                        </div>
                                        <div>
                                            <label className="label">{t('inventoryManagement.reason')} *</label>
                                            <input
                                                type="text"
                                                value={adjustmentData.reason}
                                                onChange={(e) => setAdjustmentData(prev => ({ ...prev, reason: e.target.value }))}
                                                className="input"
                                                placeholder={t('products.reasonPlaceholder')}
                                                required
                                            />
                                        </div>
                                        <div className="flex gap-3">
                                            <button
                                                type="button"
                                                onClick={() => {
                                                    setShowAdjustForm(false)
                                                    setAdjustmentData({ warehouseId: '', quantityChange: '', reason: '' })
                                                }}
                                                className="flex-1 btn-secondary"
                                                disabled={adjusting}
                                            >
                                                {t('common.cancel')}
                                            </button>
                                            <button
                                                type="submit"
                                                className="flex-1 btn-primary"
                                                disabled={adjusting}
                                            >
                                                {adjusting ? t('products.adjusting') : t('inventoryManagement.adjustStock')}
                                            </button>
                                        </div>
                                    </form>
                                </div>
                            ))}
                        </>
                    )}
                </div>
            </div>
        </div>
    )
}

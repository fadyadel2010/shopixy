import { useState, useEffect, useRef } from 'react'
import api from '../services/api'
import { useToast } from '../components/Toast'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import AsyncEntitySelect from '../components/AsyncEntitySelect'
import { useRTL } from '../hooks/useRTL'
import { searchSuppliers } from '../services/entityLookup'
import { buildIdempotencyHeaders, stableStringify } from '../utils/requestSafety'
import { formatDate } from '../utils/dateFormat'
import { roundCurrency } from '../utils/financialMath'

const buildRequestId = (prefix) => `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`

const calculateLineAmounts = (item) => {
    const subtotal = roundCurrency(item.quantity * item.unitPrice)
    const tax = roundCurrency(subtotal * item.taxRate / 100)

    return {
        subtotal,
        tax,
        total: roundCurrency(subtotal + tax),
    }
}

export default function PurchaseReturns() {
    const { t } = useTranslation()
    const { isRTL, dir, locale } = useRTL()
    const { formatCurrency } = useSettings()
    const { showSuccess, showError } = useToast()

    // View State
    const [view, setView] = useState('list') // 'list' or 'create'
    const [returns, setReturns] = useState([])
    const [loading, setLoading] = useState(false)
    const [pagination, setPagination] = useState({ page: 1, pageSize: 20, totalPages: 1 })

    // Form State
    const [warehouses, setWarehouses] = useState([])
    const [purchaseInvoices, setPurchaseInvoices] = useState([])

    const [selectedSupplier, setSelectedSupplier] = useState(null)
    const [warehouseId, setWarehouseId] = useState('')
    const [returnDate, setReturnDate] = useState(new Date().toISOString().split('T')[0])
    const [originalInvoiceId, setOriginalInvoiceId] = useState('')
    const [originalInvoice, setOriginalInvoice] = useState(null)
    const [items, setItems] = useState([])
    const [reason, setReason] = useState('')
    const [notes, setNotes] = useState('')
    const [refundMethod, setRefundMethod] = useState('none') // presentation-only destination note
    const [isSubmitting, setIsSubmitting] = useState(false)
    const supplierId = selectedSupplier?.id || ''
    const submissionLockRef = useRef(false)
    const lastSubmissionRef = useRef(null)
    const initialDataRequestIdRef = useRef(0)
    const purchaseInvoicesRequestIdRef = useRef(0)
    const invoiceRequestIdRef = useRef(0)

    useEffect(() => {
        if (view === 'list') {
            fetchReturns()
        } else {
            fetchInitialData()
        }
    }, [view, pagination.page])

    const fetchReturns = async () => {
        setLoading(true)
        try {
            const response = await api.get(`/purchasereturns?page=${pagination.page}&pageSize=${pagination.pageSize}`)
            setReturns(response.data.items)
            setPagination(prev => ({
                ...prev,
                totalPages: response.data.totalPages
            }))
        } catch (error) {
            console.error('Error fetching purchase returns:', error)
            showError(t('purchases.failedToLoadReturns'))
        } finally {
            setLoading(false)
        }
    }

    const fetchInitialData = async () => {
        const requestId = initialDataRequestIdRef.current + 1
        initialDataRequestIdRef.current = requestId

        try {
            const warehousesRes = await api.get('/warehouses')
            const warehousesData = warehousesRes.data.items || warehousesRes.data || []

            console.log('Warehouses loaded:', warehousesData)

            if (requestId !== initialDataRequestIdRef.current) {
                return
            }

            setWarehouses(warehousesData)

            if (warehousesData.length > 0) {
                setWarehouseId(warehousesData[0].id)
            }
        } catch (error) {
            console.error('Error loading initial data:', error)
            showError(t('purchases.failedToLoad'))
        }
    }

    const fetchPurchaseInvoices = async (supplierId) => {
        const requestId = purchaseInvoicesRequestIdRef.current + 1
        purchaseInvoicesRequestIdRef.current = requestId

        try {
            const response = await api.get(`/purchases?supplierId=${supplierId}&pageSize=50`)
            if (requestId !== purchaseInvoicesRequestIdRef.current) {
                return
            }

            const invoicesData = response.data.items || response.data || []
            console.log('Purchase invoices loaded for supplier:', supplierId, invoicesData)
            setPurchaseInvoices(invoicesData)
        } catch (error) {
            if (requestId !== purchaseInvoicesRequestIdRef.current) {
                return
            }

            console.error('Error fetching purchase invoices:', error)
            setPurchaseInvoices([])
        }
    }

    const handleSupplierChange = (supplier) => {
        const nextSupplierId = supplier?.id || ''

        purchaseInvoicesRequestIdRef.current += 1
        invoiceRequestIdRef.current += 1
        setSelectedSupplier(supplier)
        setOriginalInvoiceId('')
        setOriginalInvoice(null)
        setItems([])
        if (nextSupplierId) {
            fetchPurchaseInvoices(nextSupplierId)
        } else {
            setPurchaseInvoices([])
        }
    }

    const handleInvoiceSelect = async (invoiceId) => {
        const requestId = invoiceRequestIdRef.current + 1
        invoiceRequestIdRef.current = requestId
        setOriginalInvoiceId(invoiceId)
        if (!invoiceId) {
            setOriginalInvoice(null)
            setItems([])
            return
        }

        try {
            const response = await api.get(`/purchasereturns/invoice/${invoiceId}`)
            if (requestId !== invoiceRequestIdRef.current) {
                return
            }

            setOriginalInvoice(response.data)

            // Pre-populate items from original invoice with variant info
            const returnItems = response.data.items.map(item => {
                // Build variant display name with attributes
                let variantDisplayName = item.product.name;
                if (item.productVariant) {
                    const attrs = item.productVariant.attributes;
                    if (attrs && typeof attrs === 'object' && Object.keys(attrs).length > 0) {
                        const attrValues = Object.values(attrs).join(' / ');
                        variantDisplayName = `${item.product.name} - ${attrValues}`;
                    } else if (item.productVariant.sku) {
                        variantDisplayName = `${item.product.name} (${item.productVariant.sku})`;
                    }
                }

                return {
                    productId: item.productId,
                    productVariantId: item.productVariantId,
                    name: item.product.name,
                    variantDisplayName,
                    variantSku: item.productVariant?.sku || null,
                    variantAttributes: item.productVariant?.attributes || null,
                    unitId: item.unitId,
                    unitName: item.unit?.name || t('common.piece'),
                    originalQuantity: item.quantity,
                    quantity: 0, // User will specify return quantity
                    unitPrice: item.unitPrice,
                    taxRate: item.taxRate,
                    reason: ''
                };
            });
            setItems(returnItems)
        } catch (error) {
            if (requestId !== invoiceRequestIdRef.current) {
                return
            }

            console.error('Error fetching invoice:', error)
            showError(t('purchases.failedToLoadInvoice'))
        }
    }

    const updateItem = (index, field, value) => {
        const newItems = [...items]
        if (field === 'quantity') {
            newItems[index] = { ...newItems[index], [field]: roundCurrency(value) }
        } else {
            newItems[index] = { ...newItems[index], [field]: value }
        }
        setItems(newItems)
    }

    const calculateSubtotal = () => {
        return roundCurrency(items.reduce((sum, item) => sum + calculateLineAmounts(item).subtotal, 0))
    }

    const calculateTax = () => {
        return roundCurrency(items.reduce((sum, item) => sum + calculateLineAmounts(item).tax, 0))
    }

    const calculateTotal = () => {
        return roundCurrency(calculateSubtotal() + calculateTax())
    }

    const handleSubmit = async (e) => {
        e.preventDefault()

        if (submissionLockRef.current) {
            return
        }

        if (!supplierId) {
            showError(t('purchases.selectSupplierError'))
            return
        }

        const returnItems = items.filter(item => item.quantity > 0)
        if (returnItems.length === 0) {
            showError(t('purchases.specifyQuantitiesError'))
            return
        }

        // Validate return quantities
        for (const item of returnItems) {
            if (item.quantity > item.originalQuantity) {
                showError(`${t('purchases.cannotReturnMoreError')} ${item.name}`)
                return
            }
        }

        setLoading(true)
        try {
            const payload = {
                supplierId,
                warehouseId,
                returnDate,
                originalInvoiceId: originalInvoiceId || null,
                items: returnItems.map(i => ({
                    productId: i.productId,
                    productVariantId: i.productVariantId,
                    unitId: i.unitId, // ✅ CRITICAL: Send unitId for correct conversion
                    quantity: i.quantity,
                    reason: i.reason
                })),
                reason,
                notes,
            }

            const fingerprint = stableStringify(payload)
            const idempotencyKey = lastSubmissionRef.current?.fingerprint === fingerprint
                ? lastSubmissionRef.current.key
                : buildRequestId('purchase-return-create')
            const purchaseAttemptId = buildRequestId('purchase-return-attempt')

            lastSubmissionRef.current = { key: idempotencyKey, fingerprint }
            submissionLockRef.current = true
            setIsSubmitting(true)

            await api.post('/purchasereturns', payload, {
                headers: buildIdempotencyHeaders(idempotencyKey, purchaseAttemptId),
            })
            showSuccess(t('purchases.returnCreated'))
            setView('list')
            resetForm()
        } catch (error) {
            console.error('Error creating purchase return:', error)
            const errorMsg = error.response?.data?.message || t('purchases.failedToCreateReturn')
            showError(errorMsg)
        } finally {
            submissionLockRef.current = false
            setIsSubmitting(false)
            setLoading(false)
        }
    }

    const isBusy = loading || isSubmitting
    const resetForm = () => {
        setSelectedSupplier(null)
        setWarehouseId(warehouses.length > 0 ? warehouses[0].id : '')
        setReturnDate(new Date().toISOString().split('T')[0])
        setOriginalInvoiceId('')
        setOriginalInvoice(null)
        setItems([])
        setReason('')
        setNotes('')
        setRefundMethod('none')
        setPurchaseInvoices([])
        submissionLockRef.current = false
        lastSubmissionRef.current = null
    }

    if (view === 'list') {
        return (
            <div className="h-full flex flex-col gap-6" dir={dir}>
                <div className={`flex items-center justify-between gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                    <h1 className="text-2xl font-bold text-gray-800">{t('purchases.purchaseReturns')}</h1>
                    <button
                        onClick={() => setView('create')}
                        disabled={isBusy}
                        className="btn-primary"
                    >
                        {t('purchases.newPurchaseReturn')}
                    </button>
                </div>

                <div className="bg-white rounded-lg shadow overflow-hidden flex-1">
                    <div className="overflow-x-auto">
                        <table className="w-full">
                                <thead className="bg-gray-50 border-b border-gray-200">
                                    <tr>
                                        <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.returnNumber')}</th>
                                        <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.date')}</th>
                                        <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.supplier')}</th>
                                        <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.originalInvoice')}</th>
                                        <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.reason')}</th>
                                        <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-left' : 'text-right'}`}>{t('purchases.total')}</th>
                                    </tr>
                                </thead>
                            <tbody className="divide-y divide-gray-200">
                                {loading ? (
                                    <tr>
                                        <td colSpan="6" className="px-6 py-8 text-center text-gray-500">
                                            {t('common.loading')}
                                        </td>
                                    </tr>
                                ) : returns.length === 0 ? (
                                    <tr>
                                        <td colSpan="6" className="px-6 py-8 text-center text-gray-500">
                                            {t('purchases.noPurchaseReturns')}
                                        </td>
                                    </tr>
                                ) : (
                                    returns.map(ret => (
                                        <tr key={ret.id} className="hover:bg-gray-50">
                                            <td className={`px-6 py-4 text-sm font-medium text-gray-900 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                {ret.returnNumber}
                                            </td>
                                            <td className={`px-6 py-4 text-sm text-gray-500 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                {formatDate(ret.returnDate, locale)}
                                            </td>
                                            <td className={`px-6 py-4 text-sm text-gray-900 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                {ret.supplier?.name || t('common.none')}
                                            </td>
                                            <td className={`px-6 py-4 text-sm text-gray-500 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                {ret.originalInvoiceNumber || t('common.none')}
                                            </td>
                                            <td className={`px-6 py-4 text-sm text-gray-500 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                {ret.reason || t('common.none')}
                                            </td>
                                            <td className={`px-6 py-4 text-sm font-medium text-gray-900 ${isRTL ? 'text-left' : 'text-right'}`}>
                                                {formatCurrency(ret.totalAmount)}
                                            </td>
                                        </tr>
                                    ))
                                )}
                            </tbody>
                        </table>
                    </div>

                    {pagination.totalPages > 1 && (
                        <div className={`px-6 py-4 border-t border-gray-200 flex justify-between items-center ${isRTL ? 'flex-row-reverse' : ''}`}>
                            <button
                                onClick={() => setPagination(prev => ({ ...prev, page: prev.page - 1 }))}
                                disabled={pagination.page === 1}
                                className="btn-secondary disabled:opacity-50"
                            >
                                {t('purchases.previous')}
                            </button>
                            <span className="text-sm text-gray-600">
                                {t('purchases.page')} {pagination.page} {t('purchases.of')} {pagination.totalPages}
                            </span>
                            <button
                                onClick={() => setPagination(prev => ({ ...prev, page: prev.page + 1 }))}
                                disabled={pagination.page === pagination.totalPages}
                                className="btn-secondary disabled:opacity-50"
                            >
                                {t('purchases.next')}
                            </button>
                        </div>
                    )}
                </div>
            </div>
        )
    }

    // Create View
    return (
        <div className="h-full flex flex-col gap-6" dir={dir}>
            <div className={`flex items-center justify-between gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                <h1 className="text-2xl font-bold text-gray-800">{t('purchases.newPurchaseReturnTitle')}</h1>
                <button
                    onClick={() => {
                        setView('list')
                        resetForm()
                    }}
                    disabled={isBusy}
                    className="btn-secondary"
                >
                    {t('purchases.backToList')}
                </button>
            </div>

            <form onSubmit={handleSubmit} className="grid grid-cols-1 lg:grid-cols-3 gap-6 h-full overflow-hidden">
                {/* Left Panel: Form & Items */}
                <div className={`lg:col-span-2 flex flex-col gap-6 overflow-y-auto ${isRTL ? 'lg:pl-2' : 'lg:pr-2'}`}>
                    {/* Header Info */}
                    <div className="bg-white p-4 rounded-lg shadow grid grid-cols-2 gap-4">
                        <div>
                            <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.supplier')}</label>
                            <AsyncEntitySelect
                                value={selectedSupplier}
                                onChange={handleSupplierChange}
                                loadOptions={searchSuppliers}
                                placeholder={t('purchases.selectSupplier')}
                                emptyText={t('suppliers.noSuppliers')}
                                disabled={isBusy}
                                renderOption={(supplier) => (
                                    <div>
                                        <div className="font-medium text-gray-900">{supplier.name}</div>
                                        <div className="text-sm text-gray-500">{supplier.phone || supplier.email || t('common.none')}</div>
                                    </div>
                                )}
                            />
                        </div>
                        <div>
                            <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.warehouse')}</label>
                            <select
                                value={warehouseId}
                                onChange={(e) => setWarehouseId(e.target.value)}
                                className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                                disabled={isBusy}
                            >
                                {warehouses?.map(w => (
                                    <option key={w.id} value={w.id}>{w.name}</option>
                                ))}
                            </select>
                        </div>
                        <div>
                            <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.returnDate')}</label>
                            <input
                                type="date"
                                value={returnDate}
                                onChange={(e) => setReturnDate(e.target.value)}
                                className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                                disabled={isBusy}
                            />
                        </div>
                        <div>
                            <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.originalPurchaseInvoice')}</label>
                            <select
                                value={originalInvoiceId}
                                onChange={(e) => handleInvoiceSelect(e.target.value)}
                                className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                                disabled={!supplierId || isBusy}
                            >
                                <option value="">{t('purchases.selectInvoiceOptional')}</option>
                                {purchaseInvoices?.map(inv => (
                                    <option key={inv.id} value={inv.id}>
                                        {inv.invoiceNumber} - {formatCurrency(inv.totalAmount)}
                                    </option>
                                ))}
                            </select>
                        </div>
                        <div className="col-span-2">
                            <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.reasonForReturn')}</label>
                            <input
                                type="text"
                                value={reason}
                                onChange={(e) => setReason(e.target.value)}
                                className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                                placeholder={t('purchases.reasonPlaceholder')}
                                disabled={isBusy}
                            />
                        </div>
                        <div className="col-span-2">
                            <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('suppliers.notes')}</label>
                            <textarea
                                value={notes}
                                onChange={(e) => setNotes(e.target.value)}
                                className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                                rows="2"
                                placeholder={t('purchases.notesPlaceholder')}
                                disabled={isBusy}
                            />
                        </div>
                    </div>

                    {/* Items Table */}
                    <div className="bg-white rounded-lg shadow flex-1 overflow-hidden flex flex-col">
                        <div className="overflow-x-auto">
                            <table className="w-full">
                                <thead className="bg-gray-50 border-b border-gray-200">
                                    <tr>
                                        <th className={`px-4 py-2 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.product')}</th>
                                        <th className="px-4 py-2 text-center text-xs font-medium text-gray-500 uppercase w-24">{t('purchases.purchased')}</th>
                                        <th className="px-4 py-2 text-center text-xs font-medium text-gray-500 uppercase w-24">{t('purchases.returnQty')}</th>
                                        <th className="px-4 py-2 text-center text-xs font-medium text-gray-500 uppercase w-32">{t('purchases.unitPrice')}</th>
                                        <th className="px-4 py-2 text-center text-xs font-medium text-gray-500 uppercase w-20">{t('purchases.taxPercent')}</th>
                                        <th className={`px-4 py-2 text-xs font-medium text-gray-500 uppercase w-32 ${isRTL ? 'text-left' : 'text-right'}`}>{t('purchases.total')}</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-gray-200">
                                    {items.length === 0 ? (
                                        <tr>
                                            <td colSpan="6" className="px-4 py-8 text-center text-gray-500">
                                                {originalInvoiceId ? t('purchases.noItemsInInvoice') : t('purchases.selectInvoiceToLoad')}
                                            </td>
                                        </tr>
                                    ) : (
                                        items.map((item, index) => (
                                            <tr key={index}>
                                                <td className={`px-4 py-2 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                    <div className="text-sm font-medium">{item.variantDisplayName || item.name}</div>
                                                    {item.variantSku && (
                                                        <div className="text-xs text-gray-500">SKU: {item.variantSku}</div>
                                                    )}
                                                    {item.unitName && (
                                                        <div className="text-xs text-blue-600">{item.unitName}</div>
                                                    )}
                                                </td>
                                                <td className="px-4 py-2 text-center text-sm text-gray-600">
                                                    {item.originalQuantity}
                                                </td>
                                                <td className="px-4 py-2">
                                                    <input
                                                        type="number"
                                                        value={item.quantity}
                                                        onChange={(e) => updateItem(index, 'quantity', e.target.value)}
                                                        className="input w-full text-center p-1"
                                                        min="0"
                                                        max={item.originalQuantity}
                                                        disabled={isBusy}
                                                    />
                                                </td>
                                                <td className="px-4 py-2">
                                                    <input
                                                        type="number"
                                                        value={item.unitPrice}
                                                        readOnly
                                                        className="input w-full bg-gray-50 text-center p-1 text-gray-600"
                                                        min="0"
                                                        step="0.01"
                                                        tabIndex={-1}
                                                    />
                                                </td>
                                                <td className="px-4 py-2">
                                                    <input
                                                        type="number"
                                                        value={item.taxRate}
                                                        readOnly
                                                        className="input w-full bg-gray-50 text-center p-1 text-gray-600"
                                                        min="0"
                                                        step="0.1"
                                                        tabIndex={-1}
                                                    />
                                                </td>
                                                <td className={`px-4 py-2 font-medium ${isRTL ? 'text-left' : 'text-right'}`}>
                                                    {formatCurrency(calculateLineAmounts(item).total)}
                                                </td>
                                            </tr>
                                        ))
                                    )}
                                </tbody>
                            </table>
                        </div>
                    </div>
                </div>

                {/* Right Panel: Totals & Refund */}
                <div className="flex flex-col gap-6">
                    <div className="bg-white p-6 rounded-lg shadow h-full flex flex-col">
                        <h2 className={`text-lg font-bold mb-4 ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.returnSummary')}</h2>

                        <div className="space-y-3 mb-6 flex-1">
                            <div className={`flex justify-between text-gray-600 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                <span>{t('purchases.subtotal')}</span>
                                <span>{formatCurrency(calculateSubtotal())}</span>
                            </div>
                            <div className={`flex justify-between text-gray-600 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                <span>{t('purchases.tax')}</span>
                                <span>{formatCurrency(calculateTax())}</span>
                            </div>
                            <div className={`border-t border-gray-200 pt-3 flex justify-between text-xl font-bold text-gray-900 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                <span>{t('purchases.totalReturn')}</span>
                                <span>{formatCurrency(calculateTotal())}</span>
                            </div>
                        </div>

                        <div className="space-y-4 border-t border-gray-200 pt-6">
                            <div className="space-y-4">
                                <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.refundMethod')}</label>
                                <div className="flex flex-col gap-2">
                                    <label className={`flex items-center gap-2 cursor-pointer ${isRTL ? 'flex-row-reverse justify-end' : ''}`}>
                                        <input
                                            type="radio"
                                            name="refundMethod"
                                            value="none"
                                            checked={refundMethod === 'none'}
                                                onChange={() => setRefundMethod('none')}
                                            className="text-blue-600 focus:ring-blue-500"
                                            disabled={isBusy}
                                        />
                                        <span className="text-sm font-medium">{t('purchases.creditSupplierAccount')}</span>
                                    </label>
                                    <label className={`flex items-center gap-2 cursor-pointer ${isRTL ? 'flex-row-reverse justify-end' : ''}`}>
                                        <input
                                            type="radio"
                                            name="refundMethod"
                                            value="cash"
                                            checked={refundMethod === 'cash'}
                                            onChange={() => setRefundMethod('cash')}
                                            className="text-blue-600 focus:ring-blue-500"
                                            disabled={isBusy}
                                        />
                                        <span className="text-sm font-medium">{t('purchases.refundToCash')}</span>
                                    </label>
                                    <label className={`flex items-center gap-2 cursor-pointer ${isRTL ? 'flex-row-reverse justify-end' : ''}`}>
                                        <input
                                            type="radio"
                                            name="refundMethod"
                                            value="visa"
                                            checked={refundMethod === 'visa'}
                                            onChange={() => setRefundMethod('visa')}
                                            className="text-blue-600 focus:ring-blue-500"
                                            disabled={isBusy}
                                        />
                                        <span className="text-sm font-medium">{t('purchases.refundToVisa')}</span>
                                    </label>
                                </div>
                            </div>

                            <button
                                type="submit"
                                disabled={isBusy}
                                className="btn-primary w-full py-3 text-lg disabled:opacity-50 disabled:cursor-not-allowed"
                            >
                                {isSubmitting ? t('purchases.processing') : t('purchases.createPurchaseReturn')}
                            </button>
                        </div>
                    </div>
                </div>
            </form>
        </div>
    )
}

import { useState, useEffect, useRef, useCallback } from 'react'
import api, { isRequestCanceled } from '../services/api'
import { useToast } from '../components/Toast'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import InvoiceSearchModal from '../components/InvoiceSearchModal'
import PurchasePrintModal from '../components/PurchasePrintModal'
import VariantSelectModal from '../components/VariantSelectModal'
import AsyncEntitySelect from '../components/AsyncEntitySelect'
import { useRTL } from '../hooks/useRTL'
import {
    roundCurrency,
    toSafeNumber,
    toCurrencyMinorUnits,
    fromCurrencyMinorUnits,
} from '../utils/financialMath'
import { useDebounce } from '../hooks/useDebounce'
import { searchSuppliers } from '../services/entityLookup'
import { getProductUnitsForProduct, loadProductUnitsMap } from '../services/productUnitsApi'
import { buildIdempotencyHeaders, stableStringify } from '../utils/requestSafety'

const buildRequestId = (prefix) => `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`

export default function PurchaseInvoice() {
    const { t } = useTranslation()
    const { isRTL, dir } = useRTL()
    const { formatCurrency, settings } = useSettings()
    const navigate = useNavigate()
    const { showSuccess, showError } = useToast()

    const [warehouses, setWarehouses] = useState([])
    const [loading, setLoading] = useState(false)

    // Form State
    const [editingId, setEditingId] = useState(null)
    const [editingMutationVersion, setEditingMutationVersion] = useState(null)
    const [invoiceNumber, setInvoiceNumber] = useState(null)
    const [selectedSupplier, setSelectedSupplier] = useState(null)
    const [warehouseId, setWarehouseId] = useState('')
    const [invoiceDate, setInvoiceDate] = useState(new Date().toISOString().split('T')[0])
    const [items, setItems] = useState([])
    const [notes, setNotes] = useState('')
    const [paidAmount, setPaidAmount] = useState(0)
    const [paymentMethod, setPaymentMethod] = useState('Cash')
    const [initialPaymentMethod, setInitialPaymentMethod] = useState('Cash')
    const [dueDate, setDueDate] = useState('')

    // Search State (Product)
    const [searchTerm, setSearchTerm] = useState('')
    const [searchResults, setSearchResults] = useState([])
    const searchRef = useRef(null)
    const lifetimeControllerRef = useRef(new AbortController())
    const initialDataRequestControllerRef = useRef(null)
    const productSearchRequestControllerRef = useRef(null)
    const invoiceLoadRequestControllerRef = useRef(null)
    const printRequestControllerRef = useRef(null)
    const debouncedSearchTerm = useDebounce(searchTerm, 400)

    // Invoice Search Modal
    const [showSearchModal, setShowSearchModal] = useState(false)
    const [showPrintModal, setShowPrintModal] = useState(false)
    const [printData, setPrintData] = useState(null)

    // PHASE 11.6: Variant Selection Modal
    const [showVariantModal, setShowVariantModal] = useState(false)
    const [pendingProduct, setPendingProduct] = useState(null)
    const [isSubmitting, setIsSubmitting] = useState(false)
    const supplierId = selectedSupplier?.id || ''
    const submissionLockRef = useRef(false)
    const lastSubmissionRef = useRef(null)

    function startAbortableRequest(controllerRef) {
        controllerRef.current?.abort()
        const controller = new AbortController()
        controllerRef.current = controller
        return controller
    }

    const fetchInitialData = useCallback(async (signal) => {
        try {
            const warehousesRes = await api.get('/warehouses', { signal })

            if (signal.aborted) {
                return
            }

            setWarehouses(warehousesRes.data)

            if (warehousesRes.data.length > 0) {
                setWarehouseId(warehousesRes.data[0].id)
            }
        } catch (error) {
            if (signal.aborted || isRequestCanceled(error)) {
                return
            }

            console.error('Error loading initial data:', error)
            showError(t('purchases.failedToLoad'))
        }
    }, [showError, t])

    useEffect(() => {
        const controller = startAbortableRequest(initialDataRequestControllerRef)
        void fetchInitialData(controller.signal)

        return () => {
            initialDataRequestControllerRef.current?.abort()
            productSearchRequestControllerRef.current?.abort()
            invoiceLoadRequestControllerRef.current?.abort()
            printRequestControllerRef.current?.abort()
            lifetimeControllerRef.current.abort()
        }
    }, [fetchInitialData])

    useEffect(() => {
        productSearchRequestControllerRef.current?.abort()

        const normalizedSearchTerm = debouncedSearchTerm.trim()
        if (normalizedSearchTerm.length < 2) {
            setSearchResults([])
            return
        }

        const controller = new AbortController()
        productSearchRequestControllerRef.current = controller

        const loadSearchResults = async () => {
            try {
                const response = await api.get('/products/search', {
                    params: { q: normalizedSearchTerm },
                    signal: controller.signal,
                })

                if (controller.signal.aborted) {
                    return
                }

                setSearchResults(response.data)
            } catch (error) {
                if (controller.signal.aborted || isRequestCanceled(error)) {
                    return
                }

                console.error('Error searching products:', error)
            }
        }

        void loadSearchResults()

        return () => {
            controller.abort()
        }
    }, [debouncedSearchTerm])

    // PHASE 11.6: Handle product selection - check for variants
    const addItem = async (product) => {
        // Check if product has multiple variants
        if (product.hasVariants && (product.variants?.length || 0) > 1) {
            // Show variant selection modal
            setPendingProduct(product)
            setShowVariantModal(true)
            setSearchTerm('')
            setSearchResults([])
            return
        }

        // Simple product or product with only one variant
        let productToAdd = product
        let variantId = null

        if (product.variants?.length === 1) {
            const defaultVariant = product.variants[0]
            variantId = defaultVariant.id
            productToAdd = {
                ...product,
                canonicalUnitPrice: defaultVariant.price,
                canonicalCostPrice: defaultVariant.costPrice,
                sku: defaultVariant.sku
            }
        }

        await addItemWithVariant(productToAdd, variantId)
    }

    // PHASE 11.6: Handle variant selection from modal
    // Note: The modal returns a product object with variant info merged in
    const handleVariantSelect = async (productWithVariant) => {
        if (!pendingProduct || !productWithVariant) return

        // productWithVariant already has variantId, canonicalCostPrice, and variantAttributes.
        await addItemWithVariant(
            productWithVariant,
            productWithVariant.variantId,
            productWithVariant.variantAttributes
        )
        setShowVariantModal(false)
        setPendingProduct(null)
    }

    // Add item with variant information
    const addItemWithVariant = async (product, variantId, variantAttributes = []) => {
        // Check for existing item with same product AND variant
        const existingItem = items.find(i =>
            i.productId === product.id &&
            i.productVariantId === variantId
        )

        if (existingItem) {
            setItems(currentItems => currentItems.map(i =>
                (i.productId === product.id && i.productVariantId === variantId)
                    ? { ...i, quantity: toSafeNumber(i.quantity) + 1 }
                    : i
            ))
        } else {
            // Fetch product units
            let productUnits = []
            let defaultUnit = null
            const lifetimeSignal = lifetimeControllerRef.current.signal
            try {
                const response = await api.get(`/products/${product.id}/units`, { signal: lifetimeSignal })
                productUnits = response.data
                defaultUnit = productUnits.find(u => u.isDefault) || productUnits[0]
            } catch (error) {
                if (!lifetimeSignal.aborted && !isRequestCanceled(error)) {
                    console.error('Error fetching product units:', error)
                }
            }

            if (lifetimeSignal.aborted) {
                return
            }

            // Build display name
            const displayName = variantAttributes && variantAttributes.length > 0
                ? `${product.name} - ${variantAttributes.map(a => a.value).join(' / ')}`
                : product.name

            setItems(currentItems => [...currentItems, {
                productId: product.id,
                productVariantId: variantId,
                name: displayName,
                variantAttributes: variantAttributes,
                quantity: 1,
                unitId: defaultUnit?.unitId || null,
                unitName: defaultUnit?.unitName || product.unit || t('common.unit'),
                conversionFactor: defaultUnit?.conversionFactor || 1,
                unitCost: roundCurrency(product.canonicalCostPrice || defaultUnit?.purchasePrice || 0),
                taxRate: toSafeNumber(product.taxRate),
                productUnits: productUnits
            }])
        }
        setSearchTerm('')
        setSearchResults([])
        searchRef.current?.focus()
    }

    const updateItem = (index, field, value) => {
        setItems(prevItems => {
            const newItems = [...prevItems]
            const nextValue = field === 'quantity' && (value === '' || value == null)
                ? value
                : field === 'unitCost'
                    ? roundCurrency(value)
                    : toSafeNumber(value)
            newItems[index] = { ...newItems[index], [field]: nextValue }
            return newItems
        })
    }

    const handleUnitChange = (index, unitId) => {
        setItems(prevItems => {
            const item = prevItems[index]
            const selectedUnit = item.productUnits?.find(u => u.unitId === unitId)

            if (selectedUnit) {
                const newItems = [...prevItems]
                newItems[index] = {
                    ...newItems[index],
                    unitId: selectedUnit.unitId,
                    unitName: selectedUnit.unitName,
                    conversionFactor: selectedUnit.conversionFactor,
                    unitCost: roundCurrency(selectedUnit.purchasePrice || newItems[index].unitCost)
                }
                return newItems
            }
            return prevItems
        })
    }

    const removeItem = (index) => {
        setItems(prevItems => prevItems.filter((_, i) => i !== index))
    }

    const calculateLineAmounts = (item) => {
        const qty = Math.max(0, toSafeNumber(item?.quantity))
        const unitCost = Math.max(0, toSafeNumber(item?.unitCost))
        const taxRate = Math.max(0, toSafeNumber(item?.taxRate))
        const lineSubtotal = roundCurrency(qty * unitCost)
        const taxAmount = roundCurrency(lineSubtotal * (taxRate / 100))
        const lineSubtotalMinor = toCurrencyMinorUnits(lineSubtotal)
        const taxAmountMinor = toCurrencyMinorUnits(taxAmount)

        return {
            lineSubtotal,
            taxAmount,
            lineTotal: fromCurrencyMinorUnits(lineSubtotalMinor + taxAmountMinor),
            lineSubtotalMinor,
            taxAmountMinor,
        }
    }

    const calculateTotals = () => {
        const { subtotalMinor, taxMinor } = items.reduce(
            (totals, item) => {
                const { lineSubtotalMinor, taxAmountMinor } = calculateLineAmounts(item)
                return {
                    subtotalMinor: totals.subtotalMinor + lineSubtotalMinor,
                    taxMinor: totals.taxMinor + taxAmountMinor,
                }
            },
            { subtotalMinor: 0, taxMinor: 0 },
        )
        const totalMinor = subtotalMinor + taxMinor

        return {
            subtotal: fromCurrencyMinorUnits(subtotalMinor),
            tax: fromCurrencyMinorUnits(taxMinor),
            total: fromCurrencyMinorUnits(totalMinor),
        }
    }

    const totals = calculateTotals()

    const calculateBalanceDue = () => fromCurrencyMinorUnits(
        toCurrencyMinorUnits(totals.total) - toCurrencyMinorUnits(paidAmount),
    )

    const resetForm = () => {
        setEditingId(null)
        setEditingMutationVersion(null)
        setInvoiceNumber(null)
        setSelectedSupplier(null)
        if (warehouses.length > 0) setWarehouseId(warehouses[0].id)
        setInvoiceDate(new Date().toISOString().split('T')[0])
        setItems([])
        setNotes('')
        setPaidAmount(0)
        setPaymentMethod('Cash')
        setInitialPaymentMethod('Cash')
        setDueDate('')
        setSearchTerm('')
        setSearchResults([])
        submissionLockRef.current = false
        lastSubmissionRef.current = null
    }

    const handleSelectInvoice = async (invoice) => {
        setShowSearchModal(false)
        const controller = startAbortableRequest(invoiceLoadRequestControllerRef)
        setLoading(true)
        try {
            // Fetch full details if needed, or use passed object if comprehensive.
            // The search modal returns items list but maybe slightly different DTO. 
            // Better to fetch fresh by ID to be sure.
            const res = await api.get(`/purchases/${invoice.id}`, { signal: controller.signal })

            if (controller.signal.aborted) {
                return
            }

            const fullInvoice = res.data

            setEditingId(fullInvoice.id)
            setEditingMutationVersion(fullInvoice.mutationVersion)
            setInvoiceNumber(fullInvoice.invoiceNumber)
            setSelectedSupplier(fullInvoice.supplier?.id
                ? {
                    id: fullInvoice.supplier.id,
                    name: fullInvoice.supplier.name || t('purchases.unknownSupplier'),
                    phone: fullInvoice.supplier.phone || '',
                    email: fullInvoice.supplier.email || '',
                }
                : null)
            setWarehouseId(fullInvoice.warehouse?.id || fullInvoice.warehouseId || '')
            setInvoiceDate(fullInvoice.invoiceDate.split('T')[0])
            setNotes(fullInvoice.notes || '')
            const restoredInitialPaymentMethod = ['Cash', 'Card', 'BankTransfer'].includes(fullInvoice.initialPaymentMethod)
                ? fullInvoice.initialPaymentMethod
                : ['Cash', 'Card', 'BankTransfer'].includes(fullInvoice.paymentMethod)
                    ? fullInvoice.paymentMethod
                    : 'Cash'
            setPaymentMethod(fullInvoice.paymentMethod)
            setInitialPaymentMethod(restoredInitialPaymentMethod)
            setPaidAmount(roundCurrency(fullInvoice.paidAmount))
            if (fullInvoice.paymentMethod === 'Credit' && fullInvoice.dueDate) {
                setDueDate(fullInvoice.dueDate.split('T')[0])
            } else {
                setDueDate('')
            }

            // Best-effort fetch of product units - don't block invoice restoration on failure
            let unitsByProductId = {}
            try {
                unitsByProductId = await loadProductUnitsMap(
                    fullInvoice.items.map(item => item.productId),
                    { signal: controller.signal },
                )
            } catch (unitsError) {
                if (!controller.signal.aborted && !isRequestCanceled(unitsError)) {
                    console.warn('Failed to load product units, continuing in degraded mode:', unitsError)
                }
                // Continue with empty map - getProductUnitsForProduct handles this gracefully
            }

            if (controller.signal.aborted) {
                return
            }

            const mappedItems = fullInvoice.items.map((i) => {
                const productUnits = getProductUnitsForProduct(unitsByProductId, i.productId)
                const unitName = productUnits.find(unit => unit.unitId === i.unitId)?.unitName || i.unit?.name || t('common.unit')

                // PHASE 11.6: Build display name with variant info
                let displayName = i.product?.name || i.productName || t('purchases.unknownProduct')
                if (i.productVariant && i.productVariant.attributes) {
                    const variantLabel = i.productVariant.attributes.map(a => a.value).join(' / ')
                    displayName = `${displayName} - ${variantLabel}`
                }

                return {
                    productId: i.productId,
                    productVariantId: i.productVariantId,
                    name: displayName,
                    variantAttributes: i.productVariant?.attributes || [],
                    quantity: i.quantity,
                    unitId: i.unitId,
                    unitName: unitName,
                    conversionFactor: i.conversionFactor,
                    unitCost: roundCurrency(i.unitPrice), // API returns UnitPrice which for Purchase is Cost
                    taxRate: toSafeNumber(i.taxRate),
                    productUnits: productUnits
                }
            })

            if (controller.signal.aborted) {
                return
            }

            setItems(mappedItems)

        } catch (error) {
            if (controller.signal.aborted || isRequestCanceled(error)) {
                return
            }

            console.error("Error loading invoice:", error)
            showError(t('common.failedToLoadData'))
        } finally {
            if (!controller.signal.aborted) {
                setLoading(false)
            }
        }
    }

    const handlePrint = async (invoice) => {
        const controller = startAbortableRequest(printRequestControllerRef)
        try {
            const res = await api.get(`/purchases/${invoice.id}`, { signal: controller.signal })

            if (controller.signal.aborted) {
                return
            }

            setPrintData(res.data)
            setShowPrintModal(true)
            setShowSearchModal(false)
        } catch (error) {
            if (controller.signal.aborted || isRequestCanceled(error)) {
                return
            }

            console.error(error)
            showError(t('common.printFailed'))
        }
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
        if (items.length === 0) {
            showError(t('purchases.addOneItemError'))
            return
        }

        if (paymentMethod === 'Credit' && !dueDate) {
            showError(t('purchases.creditDueDateRequired'))
            return
        }

        const invalidQuantityItem = items.find((item) => {
            const quantityValue = item?.quantity
            const isEmptyQuantity = quantityValue == null
                || (typeof quantityValue === 'string' && !quantityValue.trim())

            return isEmptyQuantity || toSafeNumber(quantityValue) <= 0
        })

        if (invalidQuantityItem) {
            showError(`${t('purchases.specifyQuantitiesError')} ${invalidQuantityItem.name}`)
            return
        }

        setLoading(true)
        try {
            const payload = {
                supplierId,
                warehouseId,
                invoiceDate,
                items: items.map(i => ({
                    productId: i.productId,
                    productVariantId: i.productVariantId,
                    unitId: i.unitId,
                    quantity: toSafeNumber(i.quantity),
                    unitCost: roundCurrency(i.unitCost),
                    taxRate: toSafeNumber(i.taxRate)
                })),
                paymentMethod,
                initialPaymentMethod: paymentMethod === 'Credit' && paidAmount > 0 ? initialPaymentMethod : null,
                paidAmount: roundCurrency(paidAmount),
                expectedMutationVersion: editingId ? editingMutationVersion : null,
                dueDate: paymentMethod === 'Credit' ? dueDate : null,
                notes
            }

            const fingerprint = stableStringify({
                editingId: editingId || null,
                payload,
            })
            const idempotencyKey = lastSubmissionRef.current?.fingerprint === fingerprint
                ? lastSubmissionRef.current.key
                : buildRequestId(editingId ? 'purchase-update' : 'purchase-create')
            const purchaseAttemptId = buildRequestId(editingId ? 'purchase-update-attempt' : 'purchase-create-attempt')

            lastSubmissionRef.current = { key: idempotencyKey, fingerprint }
            submissionLockRef.current = true
            setIsSubmitting(true)

            if (editingId) {
                await api.put(`/purchases/${editingId}`, payload, {
                    headers: buildIdempotencyHeaders(idempotencyKey, purchaseAttemptId),
                })
                showSuccess(t('common.updatedSuccessfully'))
            } else {
                await api.post('/purchases', payload, {
                    headers: buildIdempotencyHeaders(idempotencyKey, purchaseAttemptId),
                })
                showSuccess(t('purchases.created'))
                navigate('/suppliers')
            }
            if (!editingId) resetForm() // Clear only if created new, else stay or what? usually clear.
            else {
                // If editing, maybe stay? Or clear? Let's clear to indicate done
                resetForm()
            }
        } catch (error) {
            console.error('Error saving purchase:', error)
            const errorMsg = error.response?.data?.error || error.response?.data?.message || t('purchases.failedToCreate')
            showError(errorMsg)
        } finally {
            submissionLockRef.current = false
            setIsSubmitting(false)
            setLoading(false)
        }
    }

    const isBusy = loading || isSubmitting
    const currencyIndicator = settings?.currencyCode || settings?.currencySymbol || ''

    return (
        <div className="h-full flex flex-col gap-6" dir={dir}>
            <div className={`flex items-center justify-between gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                <div className={`flex items-center gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                    <h1 className="text-2xl font-bold text-gray-800">
                        {editingId ? `${t('common.edit')} ${t('purchases.invoice')} #${invoiceNumber}` : t('purchases.newPurchaseInvoice')}
                    </h1>
                    {editingId && (
                        <span className="bg-yellow-100 text-yellow-800 text-xs px-2 py-1 rounded-full font-medium">
                            {t('common.editing')}
                        </span>
                    )}
                </div>
                <div className={`flex gap-2 ${isRTL ? 'flex-row-reverse' : ''}`}>
                    <button
                        type="button"
                        onClick={resetForm}
                        disabled={isBusy}
                        className="btn-secondary flex items-center gap-2"
                    >
                        <span>+</span> {t('common.new')}
                    </button>
                    <button
                        type="button"
                        onClick={() => setShowSearchModal(true)}
                        disabled={isBusy}
                        className="btn-secondary flex items-center gap-2"
                    >
                        <span>🔍</span> {t('common.search')}
                    </button>
                </div>
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
                                onChange={setSelectedSupplier}
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
                                {warehouses.map(w => (
                                    <option key={w.id} value={w.id}>{w.name}</option>
                                ))}
                            </select>
                        </div>
                        <div>
                            <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.date')}</label>
                            <input
                                type="date"
                                value={invoiceDate}
                                onChange={(e) => setInvoiceDate(e.target.value)}
                                className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                                disabled={isBusy}
                            />
                        </div>
                        <div>
                            <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.referenceNotes')}</label>
                            <input
                                type="text"
                                value={notes}
                                onChange={(e) => setNotes(e.target.value)}
                                className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                                placeholder={t('purchases.invoiceNumberPlaceholder')}
                                disabled={isBusy}
                            />
                        </div>
                    </div>

                    {/* Product Search */}
                    <div className="bg-white p-4 rounded-lg shadow relative">
                        <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.addProducts')}</label>
                        <input
                            ref={searchRef}
                            type="text"
                            value={searchTerm}
                            onChange={(e) => setSearchTerm(e.target.value)}
                            onKeyDown={(e) => {
                                if (e.key === 'Enter') {
                                    e.preventDefault()
                                }
                            }}
                            className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                            disabled={isBusy}
                            placeholder={t('purchases.scanPlaceholder')}
                        />
                        {searchResults.length > 0 && (
                            <div className="absolute top-full left-0 right-0 bg-white shadow-xl rounded-b-lg z-10 max-h-60 overflow-y-auto border border-gray-200">
                                {searchResults.map(product => (
                                    <div
                                        key={product.id}
                                        onClick={() => {
                                            if (!isBusy) {
                                                void addItem(product)
                                            }
                                        }}
                                        className={`p-3 hover:bg-gray-50 cursor-pointer border-b border-gray-100 flex justify-between items-center ${isRTL ? 'flex-row-reverse' : ''}`}
                                    >
                                        <div className={isRTL ? 'text-right' : 'text-left'}>
                                            <div className="font-medium">{product.name}</div>
                                            <div className="text-xs text-gray-500">{product.barcode}</div>
                                        </div>
                                        <div className={`text-sm font-medium text-gray-600 ${isRTL ? 'text-left' : 'text-right'}`}>
                                            {t('purchases.stock')}: {(() => {
                                                const warehouseStock = product.warehouseStocks?.find(ws => ws.warehouseId === warehouseId)
                                                return warehouseStock?.quantity || 0
                                            })()}
                                        </div>
                                    </div>
                                ))}
                            </div>
                        )}
                    </div>

                    {/* Items Table */}
                    <div className="bg-white rounded-lg shadow flex-1 overflow-hidden flex flex-col">
                        <div className="overflow-x-auto">
                            <table className="w-full">
                                <thead className="bg-gray-50 border-b border-gray-200">
                                    <tr>
                                        <th className={`px-4 py-2 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.product')}</th>
                                        <th className="px-4 py-2 text-center text-xs font-medium text-gray-500 uppercase w-32">{t('products.unit')}</th>
                                        <th className="px-4 py-2 text-center text-xs font-medium text-gray-500 uppercase w-24">{t('purchases.qty')}</th>
                                        <th className="px-4 py-2 text-center text-xs font-medium text-gray-500 uppercase w-32">{t('purchases.cost')}</th>
                                        <th className="px-4 py-2 text-center text-xs font-medium text-gray-500 uppercase w-20">{t('purchases.taxPercent')}</th>
                                        <th className={`px-4 py-2 text-xs font-medium text-gray-500 uppercase w-32 ${isRTL ? 'text-left' : 'text-right'}`}>{t('purchases.total')}</th>
                                        <th className="px-4 py-2 w-10"></th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-gray-200">
                                    {items.length === 0 ? (
                                        <tr>
                                            <td colSpan="7" className="px-4 py-8 text-center text-gray-500">
                                                {t('purchases.noItems')}
                                            </td>
                                        </tr>
                                    ) : (
                                        items.map((item, index) => (
                                            <tr key={index}>
                                                <td className={`px-4 py-2 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                    <div className="text-sm font-medium">{item.name}</div>
                                                </td>
                                                <td className="px-4 py-2">
                                                    {item.productUnits && item.productUnits.length > 0 ? (
                                                        <select
                                                            value={item.unitId || ''}
                                                            onChange={(e) => handleUnitChange(index, e.target.value)}
                                                            className={`input w-full text-sm p-1 ${isRTL ? 'text-right' : 'text-left'}`}
                                                            title={t('purchases.unitConversionTitle', {
                                                                unitName: item.unitName,
                                                                conversionFactor: item.conversionFactor,
                                                            })}
                                                            disabled={isBusy}
                                                        >
                                                            {item.productUnits.map(unit => (
                                                                <option key={unit.unitId} value={unit.unitId}>
                                                                    {unit.unitName}
                                                                </option>
                                                            ))}
                                                        </select>
                                                    ) : (
                                                        <span className="text-sm text-gray-500">{item.unitName}</span>
                                                    )}
                                                </td>
                                                <td className="px-4 py-2">
                                                    <input
                                                        type="number"
                                                        value={item.quantity ?? ''}
                                                        onChange={(e) => updateItem(index, 'quantity', e.target.value)}
                                                        className="input w-full text-center p-1"
                                                        min="1"
                                                        disabled={isBusy}
                                                    />
                                                </td>
                                                <td className="px-4 py-2">
                                                    <input
                                                        type="number"
                                                        value={item.unitCost}
                                                        onChange={(e) => updateItem(index, 'unitCost', e.target.value)}
                                                        className="input w-full text-center p-1"
                                                        min="0"
                                                        step="0.01"
                                                        disabled={isBusy}
                                                    />
                                                </td>
                                                <td className="px-4 py-2">
                                                    <input
                                                        type="number"
                                                        value={item.taxRate}
                                                        onChange={(e) => updateItem(index, 'taxRate', e.target.value)}
                                                        className="input w-full text-center p-1"
                                                        min="0"
                                                        step="0.1"
                                                        disabled={isBusy}
                                                    />
                                                </td>
                                                <td className={`px-4 py-2 font-medium ${isRTL ? 'text-left' : 'text-right'}`}>
                                                    {formatCurrency(calculateLineAmounts(item).lineTotal)}
                                                </td>
                                                <td className="px-4 py-2 text-center">
                                                    <button
                                                        type="button"
                                                        onClick={() => removeItem(index)}
                                                        disabled={isBusy}
                                                        className="text-red-500 hover:text-red-700"
                                                    >
                                                        ×
                                                    </button>
                                                </td>
                                            </tr>
                                        ))
                                    )}
                                </tbody>
                            </table>
                        </div>
                    </div>
                </div>

                {/* Right Panel: Totals & Payment */}
                <div className="flex flex-col gap-6">
                    <div className="bg-white p-6 rounded-lg shadow h-full flex flex-col">
                        <h2 className={`text-lg font-bold mb-4 ${isRTL ? 'text-right' : 'text-left'}`}>
                            {editingId ? t('common.edit') : t('purchases.summary')}
                        </h2>

                        <div className="space-y-3 mb-6 flex-1">
                            <div className={`flex justify-between text-gray-600 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                <span>{t('purchases.subtotal')}</span>
                                <span>{formatCurrency(totals.subtotal)}</span>
                            </div>
                            <div className={`flex justify-between text-gray-600 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                <span>{t('purchases.tax')}</span>
                                <span>{formatCurrency(totals.tax)}</span>
                            </div>
                            <div className={`border-t border-gray-200 pt-3 flex justify-between text-xl font-bold text-gray-900 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                <span>{t('purchases.total')}</span>
                                <span>{formatCurrency(totals.total)}</span>
                            </div>
                        </div>

                        <div className="space-y-4 border-t border-gray-200 pt-6">
                            <div>
                                <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{paymentMethod === 'Credit' ? t('purchases.initialPayment') : t('purchases.amountPaid')}</label>
                                <div className="relative">
                                    {currencyIndicator ? (
                                        <span className={`absolute top-2 text-gray-500 ${isRTL ? 'right-3' : 'left-3'}`}>{currencyIndicator}</span>
                                    ) : null}
                                    <input
                                        type="number"
                                        value={paidAmount}
                                        onChange={(e) => setPaidAmount(e.target.value)}
                                        onBlur={() => setPaidAmount(roundCurrency(paidAmount))}
                                        className={`input w-full text-lg font-bold ${currencyIndicator ? (isRTL ? 'pr-16 text-right' : 'pl-16 text-left') : (isRTL ? 'text-right' : 'text-left')}`}
                                        disabled={isBusy}
                                    />
                                </div>
                            </div>

                            <div>
                                <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.paymentMethod')}</label>
                                <select
                                    value={paymentMethod}
                                    onChange={(e) => {
                                        setPaymentMethod(e.target.value)
                                        if (e.target.value === 'Credit') {
                                            setPaidAmount(0)
                                        } else {
                                            setPaidAmount(totals.total)
                                        }
                                    }}
                                    className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                                    disabled={isBusy}
                                >
                                    <option value="Cash">{t('purchases.cash')}</option>
                                    <option value="Card">{t('purchases.card')}</option>
                                    <option value="BankTransfer">{t('purchases.bankTransfer')}</option>
                                    <option value="Credit">{t('purchases.credit')}</option>
                                </select>
                            </div>

                            {paymentMethod === 'Credit' && (
                                <>
                                    <div>
                                        <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.dueDate')} <span className="text-red-500">*</span></label>
                                        <input
                                            type="date"
                                            value={dueDate}
                                            onChange={(e) => setDueDate(e.target.value)}
                                            className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                                            required
                                            disabled={isBusy}
                                        />
                                    </div>

                                    {paidAmount > 0 && (
                                        <div>
                                            <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('purchases.payInitialAmountVia')}</label>
                                            <select
                                                value={initialPaymentMethod}
                                                onChange={(e) => setInitialPaymentMethod(e.target.value)}
                                                className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                                                disabled={isBusy}
                                            >
                                                <option value="Cash">{t('purchases.cash')}</option>
                                                <option value="Card">{t('purchases.card')}</option>
                                                <option value="BankTransfer">{t('purchases.bankTransfer')}</option>
                                            </select>
                                        </div>
                                    )}
                                </>
                            )}

                            <div className="pt-2">
                                <div className={`flex justify-between text-sm mb-2 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                    <span className="text-gray-600">{t('purchases.balanceDue')}</span>
                                    <span className={`font-bold ${calculateBalanceDue() > 0 ? 'text-red-600' : 'text-green-600'}`}>
                                        {formatCurrency(calculateBalanceDue())}
                                    </span>
                                </div>
                            </div>

                            <button
                                type="submit"
                                disabled={isBusy}
                                className={`w-full py-3 text-lg font-bold text-white rounded shadow transition-colors ${editingId ? 'bg-orange-600 hover:bg-orange-700' : 'bg-primary-600 hover:bg-primary-700'}`}
                            >
                                {isSubmitting ? t('purchases.processing') : (editingId ? t('common.update') : t('purchases.createPurchase'))}
                            </button>
                        </div>
                    </div>
                </div>
            </form>

            {showSearchModal && (
                <InvoiceSearchModal
                    type="purchases"
                    onClose={() => setShowSearchModal(false)}
                    onSelect={handleSelectInvoice}
                    onPrint={handlePrint}
                />
            )}

            {showPrintModal && printData && (
                <PurchasePrintModal
                    data={printData}
                    onClose={() => setShowPrintModal(false)}
                />
            )}

            {/* PHASE 11.6: Variant Selection Modal */}
            {showVariantModal && pendingProduct && (
                <VariantSelectModal
                    product={pendingProduct}
                    warehouseId={warehouseId}
                    onSelect={handleVariantSelect}
                    onClose={() => {
                        setShowVariantModal(false)
                        setPendingProduct(null)
                    }}
                    allowOutOfStock={true}
                    mode="purchase"
                />
            )}
        </div>
    )
}

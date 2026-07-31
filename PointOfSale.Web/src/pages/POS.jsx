import { useState, useEffect, useRef, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { usePOSCart } from '../hooks/usePOSCart'
import { useSettings } from '../contexts/SettingsContext'
import { useToast } from '../components/Toast'
import api, { isRequestCanceled } from '../services/api'
import BarcodeScanner from '../components/BarcodeScanner'
import ProductSearch from '../components/ProductSearch'
import CartItem from '../components/CartItem'
import PaymentModal from '../components/PaymentModal'
import ReceiptModal from '../components/ReceiptModal'
import CustomerSelectModal from '../components/CustomerSelectModal'
import QuickProductGrid from '../components/QuickProductGrid'
import HoldSalesModal from '../components/HoldSalesModal'
import InvoiceSearchModal from '../components/InvoiceSearchModal'
import VariantSelectModal from '../components/VariantSelectModal'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import {
    roundCurrency,
    calculateLineSubtotal,
    calculateRemainingSubtotal,
    parseStrictAmountInput,
} from '../utils/financialMath'
import {
    buildPosCartSnapshotFingerprint,
    clearPersistedPosCart,
    getPersistedPosCart,
    savePosCartSnapshot,
    shouldApplyExternalPosCartSnapshot,
    subscribeToPosCartChanges,
} from '../utils/posCartStorage'
import { loadPosItemStock, revalidatePosCartStock } from '../services/posStockService'
import { getProductUnitsForProduct, loadProductUnitsMap } from '../services/productUnitsApi'
import { invalidatePosRuntimeCache } from '../services/posRuntimeCache'

const buildPaymentAttemptId = () => `pos-payment-attempt-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
const buildPosCartId = () => `pos-cart-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`

export default function POS() {
    const { t, i18n } = useTranslation()
    const { confirm } = useConfirmDialog()
    const isRTL = i18n.language === 'ar'
    const { formatCurrency, settings, updateLocalSettings } = useSettings()
    const {
        items,
        customer,
        discountType,
        discountValue,
        addItem,
        removeItem,
        updateQuantity,
        updateItemDiscount,
        updateUnit,
        setCustomer,
        setDiscountType,
        setDiscountValue,
        calculateTotals,
        clearCart,
        holdSale,
        retrieveSale,
        undo,
        redo,
        canUndo,
        canRedo,
        syncStockLevels,
    } = usePOSCart()

    const { showSuccess, showError, showInfo, showWarning } = useToast()
    const [warehouses, setWarehouses] = useState([])
    const [selectedWarehouse, setSelectedWarehouse] = useState('')
    const [quickAccessRefreshKey, setQuickAccessRefreshKey] = useState(0)
    const [showPaymentModal, setShowPaymentModal] = useState(false)
    const [showReceiptModal, setShowReceiptModal] = useState(false)
    const [showCustomerModal, setShowCustomerModal] = useState(false)
    const [showHoldSalesModal, setShowHoldSalesModal] = useState(false)
    const [pendingExternalCart, setPendingExternalCart] = useState(null)
    const [receiptData, setReceiptData] = useState(null)

    // Editing State
    const [editingSaleId, setEditingSaleId] = useState(null)
    const [editingInvoiceNumber, setEditingInvoiceNumber] = useState(null)
    const [editingOriginalItems, setEditingOriginalItems] = useState([])
    const [editingMutationVersion, setEditingMutationVersion] = useState(null)
    const [showSearchModal, setShowSearchModal] = useState(false)

    // PHASE 11.6: Variant Selection State
    const [showVariantModal, setShowVariantModal] = useState(false)
    const [pendingProduct, setPendingProduct] = useState(null)
    const [isPreparingCheckout, setIsPreparingCheckout] = useState(false)
    const [isCartHydrated, setIsCartHydrated] = useState(false)

    const barcodeRef = useRef(null)
    const searchRef = useRef(null)
    const currentPaymentAttemptRef = useRef(null)
    const currentCartIdRef = useRef(null)
    const completedPaymentAttemptRef = useRef(null)
    const checkoutRequestControllerRef = useRef(null)
    const invoiceLoadRequestControllerRef = useRef(null)
    const productPrepControllerRef = useRef(null)
    const lastPersistedCartFingerprintRef = useRef(buildPosCartSnapshotFingerprint(null))
    const blockedExternalCartFingerprintRef = useRef(null)

    const totals = calculateTotals()
    const remainingSubtotal = calculateRemainingSubtotal(totals.subtotal, totals.itemDiscounts)

    useEffect(() => () => {
        checkoutRequestControllerRef.current?.abort()
        invoiceLoadRequestControllerRef.current?.abort()
        productPrepControllerRef.current?.abort()
    }, [])

    const getWarehouseProductStock = (product) => {
        const warehouseStock = product?.warehouseStocks?.find(ws => ws.warehouseId === selectedWarehouse)
        return Math.max(0, Number(warehouseStock?.availableQuantity ?? warehouseStock?.quantity ?? product?.stockQuantity ?? 0))
    }

    const getVariantWarehouseStock = (variant) => {
        return Math.max(0, Number(variant?.warehouseStock ?? 0))
    }

    const buildVariantDisplayName = (attributes = []) => (
        attributes
            .map(attribute => `${attribute.name}: ${attribute.value}`)
            .join(', ')
    )

    const prepareProductForCart = (product) => {
        if (!product) {
            return null
        }

        if (product.matchedVariantId && product.variants?.length) {
            const matchedVariant = product.variants.find(variant => variant.id === product.matchedVariantId)
            if (matchedVariant) {
                return {
                    ...product,
                    variantId: matchedVariant.id,
                    canonicalUnitPrice: matchedVariant.price,
                    sku: matchedVariant.sku,
                    barcode: matchedVariant.barcode || product.barcode,
                    stockQuantity: getVariantWarehouseStock(matchedVariant),
                    variantAttributes: matchedVariant.attributes || [],
                    variantDisplayName: buildVariantDisplayName(matchedVariant.attributes || []),
                }
            }
        }

        if (product.variants?.length === 1) {
            const defaultVariant = product.variants[0]
            return {
                ...product,
                variantId: defaultVariant.id,
                canonicalUnitPrice: defaultVariant.price,
                sku: defaultVariant.sku,
                barcode: defaultVariant.barcode || product.barcode,
                stockQuantity: getVariantWarehouseStock(defaultVariant),
                variantAttributes: defaultVariant.attributes || [],
                variantDisplayName: buildVariantDisplayName(defaultVariant.attributes || []),
            }
        }

        return {
            ...product,
            canonicalUnitPrice: product.salePrice,
            stockQuantity: getWarehouseProductStock(product),
        }
    }

    const clampDiscountValue = (value, nextDiscountType = discountType) => {
        // Allow common in-progress input patterns
        const stringValue = String(value).trim()
        
        // Allow empty string, single dot, trailing dot (e.g., "5."), or just a minus
        if (stringValue === '' || stringValue === '.' || stringValue === '-' || /^\d+\.$/.test(stringValue)) {
            return stringValue
        }
        
        const parsedValue = parseStrictAmountInput(value, { allowZero: true })
        if (!parsedValue.isValid) {
            // Return the raw input for other transitional states instead of reverting
            return value
        }

        const safeValue = Math.max(0, roundCurrency(parsedValue.value))

        if (nextDiscountType === 'percentage') {
            return Math.min(100, safeValue)
        }

        return Math.min(remainingSubtotal, safeValue)
    }

    const getSaleValidationError = useCallback(() => {
        const parsedDiscountValue = parseStrictAmountInput(discountValue, { allowZero: true })
        const safeDiscountValue = parsedDiscountValue.isValid
            ? Math.max(0, roundCurrency(parsedDiscountValue.value))
            : Number.NaN
        const safeTotal = roundCurrency(totals.total)

        if (!selectedWarehouse) {
            return t('pos.selectWarehouseRequired')
        }

        if (items.length === 0) {
            return t('pos.cartEmpty')
        }

        if (!Number.isFinite(Number(totals.total)) || safeTotal <= 0) {
            return t('pos.totalMustBePositive')
        }

        if (!Number.isFinite(safeDiscountValue)) {
            return t('pos.invalidInvoiceDiscount')
        }

        if (discountType === 'percentage' && (safeDiscountValue < 0 || safeDiscountValue > 100)) {
            return t('pos.invalidInvoiceDiscount')
        }

        if (discountType === 'fixed' && (safeDiscountValue < 0 || safeDiscountValue > remainingSubtotal)) {
            return t('pos.invalidInvoiceDiscount')
        }

        for (const item of items) {
            const quantity = Number(item.quantity)
            const unitPrice = Number(item.unitPrice)
            const discountAmount = Number(item.discountAmount || 0)
            const taxRate = Number(item.taxRate || 0)
            const conversionFactor = Number(item.conversionFactor || 1)
            const stockQuantity = Number(item.stockQuantity || 0)
            const lineSubtotal = calculateLineSubtotal(quantity, unitPrice)

            if (!item.productId || !Number.isFinite(quantity) || quantity <= 0) {
                return t('pos.invalidCartItem', { product: item.name })
            }

            if (!Number.isFinite(unitPrice) || unitPrice < 0) {
                return t('pos.invalidCartItemPrice', { product: item.name })
            }

            if (!Number.isFinite(discountAmount) || roundCurrency(discountAmount) < 0 || roundCurrency(discountAmount) > lineSubtotal) {
                return t('pos.invalidCartItemDiscount', { product: item.name })
            }

            if (!Number.isFinite(taxRate) || taxRate < 0) {
                return t('pos.invalidCartItemTax', { product: item.name })
            }

            if ((quantity * conversionFactor) > stockQuantity) {
                return t('pos.insufficientStock', { product: item.name, stock: stockQuantity })
            }
        }

        return null
    }, [discountType, discountValue, items, remainingSubtotal, selectedWarehouse, t, totals.total])

    const resetPOS = useCallback(() => {
        clearCart()
        currentCartIdRef.current = null
        setEditingSaleId(null)
        setEditingInvoiceNumber(null)
        setEditingOriginalItems([])
        setEditingMutationVersion(null)
    }, [clearCart])

    const applyPersistedCart = useCallback((snapshot) => {
        const normalizedFingerprint = buildPosCartSnapshotFingerprint(snapshot)

        if (!snapshot) {
            lastPersistedCartFingerprintRef.current = normalizedFingerprint
            currentCartIdRef.current = null
            resetPOS()
            return
        }

        currentCartIdRef.current = snapshot.cartId || buildPosCartId()
        retrieveSale({
            items: snapshot.items,
            customer: snapshot.customer,
            discountType: snapshot.discountType,
            discountValue: snapshot.discountValue,
        })
        setSelectedWarehouse(snapshot.warehouseId || '')
        setEditingSaleId(snapshot.editingSaleId || null)
        setEditingInvoiceNumber(snapshot.editingInvoiceNumber || null)
        setEditingOriginalItems(Array.isArray(snapshot.editingOriginalItems) ? snapshot.editingOriginalItems : [])
        setEditingMutationVersion(Number.isInteger(snapshot.editingMutationVersion) ? snapshot.editingMutationVersion : null)
        lastPersistedCartFingerprintRef.current = normalizedFingerprint
    }, [resetPOS, retrieveSale])

    useEffect(() => {
        const snapshot = getPersistedPosCart()
        applyPersistedCart(snapshot)
        setIsCartHydrated(true)
    }, [applyPersistedCart])

    const revalidateCheckoutStock = useCallback(async () => {
        checkoutRequestControllerRef.current?.abort()
        const controller = new AbortController()
        checkoutRequestControllerRef.current = controller

        try {
            const stockValidation = await revalidatePosCartStock({
                items,
                baselineItems: editingSaleId ? editingOriginalItems : [],
                warehouseId: selectedWarehouse,
                signal: controller.signal,
            })

            if (controller.signal.aborted) {
                return false
            }

            syncStockLevels(stockValidation.effectiveStockLevels)

            if (!stockValidation.ok) {
                if (stockValidation.mismatches.length === 1) {
                    const mismatch = stockValidation.mismatches[0]
                    // Map mismatch to product name from cart items
                    const matchingItem = items.find(item => 
                        item.productId === mismatch.productId && 
                        (item.variantId || item.productVariantId || null) === mismatch.variantId
                    )
                    const productName = matchingItem?.name || t('common.unknown')
                    
                    showError(t('pos.insufficientStock', {
                        product: productName,
                        stock: roundCurrency(mismatch.availableStock + mismatch.baselineQuantity),
                    }))
                } else {
                    showError(t('pos.checkoutStockMismatch'))
                }
                return false
            }

            return true
        } catch (error) {
            if (isRequestCanceled(error)) {
                return false
            }

            console.error('Error revalidating cart stock:', error)
            showError(t('pos.failedToRevalidateStock'))
            return false
        } finally {
            if (checkoutRequestControllerRef.current === controller) {
                checkoutRequestControllerRef.current = null
            }
        }
    }, [editingOriginalItems, editingSaleId, items, selectedWarehouse, showError, syncStockLevels, t])

    const openPaymentModal = useCallback(async () => {
        if (showPaymentModal || isPreparingCheckout) {
            return
        }

        const validationError = getSaleValidationError()
        if (validationError) {
            showError(validationError)
            return
        }

        setIsPreparingCheckout(true)

        try {
            const hasFreshStock = await revalidateCheckoutStock()
            if (!hasFreshStock) {
                return
            }

            currentCartIdRef.current ||= buildPosCartId()
            currentPaymentAttemptRef.current = buildPaymentAttemptId()
            completedPaymentAttemptRef.current = null
            setShowPaymentModal(true)
        } finally {
            setIsPreparingCheckout(false)
        }
    }, [getSaleValidationError, isPreparingCheckout, revalidateCheckoutStock, showError, showPaymentModal])

    const handleWarehouseChange = async (nextWarehouseId) => {
        if (!nextWarehouseId || nextWarehouseId === selectedWarehouse) {
            return
        }

        if (items.length > 0) {
            const confirmed = await confirm({
                confirmLabel: t('common.confirm'),
                description: t('pos.changeWarehouseConfirm'),
                title: t('common.warning'),
                variant: 'warning',
            })

            if (!confirmed) {
                return
            }
        }

        if (items.length > 0) {
            resetPOS()
            showInfo(t('pos.warehouseChangedCartReset'))
        }

        setSelectedWarehouse(nextWarehouseId)
    }

    const handleClearCart = useCallback(async () => {
        if (items.length === 0) {
            return
        }

        const confirmed = await confirm({
            confirmLabel: t('common.confirm'),
            description: t('pos.clearCartConfirm'),
            title: t('common.warning'),
            variant: 'warning',
        })

        if (!confirmed) {
            return
        }

        resetPOS()
        showSuccess(t('pos.cartCleared'))
    }, [confirm, items.length, resetPOS, showSuccess, t])

    const addPreparedProductToCart = async (productToAdd) => {
        if (!productToAdd) {
            return
        }

        if (!selectedWarehouse) {
            showError(t('pos.selectWarehouseRequired'))
            return
        }

        // Abort any previous product preparation request
        productPrepControllerRef.current?.abort()
        const controller = new AbortController()
        productPrepControllerRef.current = controller

        let preparedProduct = productToAdd
        try {
            const [unitsResponse, freshStock] = await Promise.all([
                api.get(`/products/${productToAdd.id}/units`, { signal: controller.signal }),
                loadPosItemStock({
                    productId: productToAdd.id,
                    variantId: productToAdd.variantId || null,
                    warehouseId: selectedWarehouse,
                    force: true,
                    signal: controller.signal,
                }),
            ])

            preparedProduct = {
                ...productToAdd,
                productUnits: Array.isArray(unitsResponse.data) ? unitsResponse.data : [],
                stockQuantity: freshStock.stockQuantity,
            }

            // Verify this controller is still current and not cancelled
            if (productPrepControllerRef.current !== controller || controller.signal.aborted) {
                return
            }
        } catch (error) {
            if (isRequestCanceled(error)) {
                return
            }

            console.error('Error preparing product for POS cart:', error)
            showError(t('common.failedToLoadData'))
            return
        } finally {
            // Clear the controller ref if this is still the current request
            if (productPrepControllerRef.current === controller) {
                productPrepControllerRef.current = null
            }
        }

        const result = await addItem(preparedProduct, 1)
        if (!result?.success) {
            showError(t('pos.insufficientStock', {
                product: preparedProduct.name,
                stock: result?.availableStock ?? preparedProduct.stockQuantity ?? 0,
            }))
            return
        }

        showSuccess(t('pos.productAdded', { product: preparedProduct.name }))
    }

    // Fetch warehouses
    useEffect(() => {
        const controller = new AbortController()

        const fetchWarehouses = async () => {
            try {
                const response = await api.get('/warehouses', { signal: controller.signal })
                const warehouseOptions = Array.isArray(response.data) ? response.data : []
                setWarehouses(warehouseOptions)
                setSelectedWarehouse(currentWarehouseId => {
                    if (currentWarehouseId && warehouseOptions.some(warehouse => warehouse.id === currentWarehouseId)) {
                        return currentWarehouseId
                    }

                    const defaultWarehouse = warehouseOptions.find(warehouse => warehouse.isDefault)
                    return defaultWarehouse?.id || warehouseOptions[0]?.id || ''
                })
            } catch (error) {
                if (isRequestCanceled(error)) {
                    return
                }

                console.error('Error fetching warehouses:', error)
                showError(t('pos.failedLoadWarehouses'))
            }
        }

        void fetchWarehouses()

        return () => controller.abort()
    }, [showError, t])

    const buildCurrentCartSnapshot = useCallback(({ ensureCartId = true } = {}) => {
        if (items.length === 0) {
            return null
        }

        return {
            cartId: currentCartIdRef.current || (ensureCartId ? (currentCartIdRef.current = buildPosCartId()) : ''),
            items,
            customer,
            discountType,
            discountValue,
            warehouseId: selectedWarehouse,
            editingSaleId,
            editingInvoiceNumber,
            editingOriginalItems,
            editingMutationVersion,
        }
    }, [
        customer,
        discountType,
        discountValue,
        editingInvoiceNumber,
        editingOriginalItems,
        editingSaleId,
        editingMutationVersion,
        items,
        selectedWarehouse,
    ])

    useEffect(() => {
        if (!isCartHydrated) {
            return
        }

        const cartSnapshot = buildCurrentCartSnapshot()

        const nextFingerprint = buildPosCartSnapshotFingerprint(cartSnapshot)
        if (nextFingerprint === lastPersistedCartFingerprintRef.current) {
            return
        }

        if (!cartSnapshot) {
            clearPersistedPosCart()
            lastPersistedCartFingerprintRef.current = nextFingerprint
            return
        }

        savePosCartSnapshot(cartSnapshot)
        lastPersistedCartFingerprintRef.current = nextFingerprint
    }, [
        buildCurrentCartSnapshot,
        isCartHydrated,
    ])

    useEffect(() => subscribeToPosCartChanges(({ snapshot }) => {
        const nextFingerprint = buildPosCartSnapshotFingerprint(snapshot)
        if (nextFingerprint === lastPersistedCartFingerprintRef.current) {
            return
        }

        const localSnapshot = buildCurrentCartSnapshot({ ensureCartId: false })
        const localFingerprint = buildPosCartSnapshotFingerprint(localSnapshot)
        const localHasActiveCart = Boolean(localSnapshot) || showPaymentModal || isPreparingCheckout

        if (!shouldApplyExternalPosCartSnapshot({
            localHasActiveCart,
            localFingerprint,
            externalFingerprint: nextFingerprint,
        })) {
            if (blockedExternalCartFingerprintRef.current !== nextFingerprint) {
                showWarning(t(
                    'pos.cartChangedInAnotherTab',
                    'POS cart changed in another tab. Finish or clear this cart before syncing.',
                ))
                blockedExternalCartFingerprintRef.current = nextFingerprint
            }
            setPendingExternalCart({
                fingerprint: nextFingerprint,
                snapshot,
            })
            return
        }

        blockedExternalCartFingerprintRef.current = null
        setPendingExternalCart(null)

        // Clear the active payment attempt to prevent late onComplete callbacks
        // from processing against the wrong cart
        currentPaymentAttemptRef.current = null
        setShowPaymentModal(false)
        applyPersistedCart(snapshot)
    }), [
        applyPersistedCart,
        buildCurrentCartSnapshot,
        isPreparingCheckout,
        showPaymentModal,
        showWarning,
        t,
    ])

    const handleSyncExternalCart = useCallback(() => {
        if (!pendingExternalCart) {
            return
        }

        currentPaymentAttemptRef.current = null
        setShowPaymentModal(false)
        applyPersistedCart(pendingExternalCart.snapshot)
        blockedExternalCartFingerprintRef.current = null
        setPendingExternalCart(null)
    }, [applyPersistedCart, pendingExternalCart])

    const handleKeepLocalCart = useCallback(() => {
        const localSnapshot = buildCurrentCartSnapshot()
        const localFingerprint = buildPosCartSnapshotFingerprint(localSnapshot)

        if (localSnapshot) {
            savePosCartSnapshot(localSnapshot)
        } else {
            clearPersistedPosCart()
        }

        lastPersistedCartFingerprintRef.current = localFingerprint
        blockedExternalCartFingerprintRef.current = null
        setPendingExternalCart(null)
    }, [buildCurrentCartSnapshot])

    const handleProductFound = async (product) => {
        // PHASE 11.6: Check if product has variants
        // If scanned barcode matched a variant, product.matchedVariantId will be set
        if (product.hasVariants && !product.matchedVariantId && (product.variants?.length || 0) > 1) {
            // Product has multiple variants - force selection
            setPendingProduct(product)
            setShowVariantModal(true)
            return
        }

        await addPreparedProductToCart(prepareProductForCart(product))
    }

    const handleProductSelect = async (product) => {
        // PHASE 11.6: Check if product has variants
        // If product has multiple variants, force variant selection
        if (product.hasVariants && (product.variants?.length || 0) > 1) {
            setPendingProduct(product)
            setShowVariantModal(true)
            return
        }

        await addPreparedProductToCart(prepareProductForCart(product))
    }

    // PHASE 11.6: Handle variant selection from modal
    const handleVariantSelect = async (productWithVariant) => {
        setShowVariantModal(false)
        setPendingProduct(null)

        await addPreparedProductToCart({
            ...productWithVariant,
            stockQuantity: Math.max(0, Number(productWithVariant.stockQuantity ?? 0)),
        })
    }

    const handleCustomerSelect = (selectedCustomer) => {
        setCustomer(selectedCustomer)
        if (selectedCustomer) {
            showSuccess(t('pos.customerSelected', { name: selectedCustomer.name }))
        } else {
            showInfo(t('pos.walkInSelected'))
        }
    }

    const handleHoldSale = useCallback(() => {
        if (items.length === 0) {
            showWarning(t('pos.cartEmptyHold'))
            return
        }

        try {
            const referenceNumber = holdSale()
            if (referenceNumber) {
                showSuccess(t('pos.saleHeld', { reference: referenceNumber }))
                resetPOS()
            }
        } catch (error) {
            showError(t('pos.saleHoldFailed', { 
                defaultValue: 'Failed to hold sale. Please try again.' 
            }))
            console.error('Failed to hold sale:', error)
        }
    }, [holdSale, items.length, resetPOS, showSuccess, showWarning, showError, t])

    const handleRetrieveSale = (sale) => {
        currentCartIdRef.current = buildPosCartId()
        setEditingSaleId(null)
        setEditingInvoiceNumber(null)
        setEditingOriginalItems([])
        setEditingMutationVersion(null)
        retrieveSale(sale)
        showSuccess(t('pos.saleRetrieved', { reference: sale.referenceNumber }))
    }

    const handleUndo = useCallback(() => {
        const actionType = undo()
        if (actionType) {
            showInfo(t('pos.undone', { action: actionType.replace('_', ' ').toLowerCase() }))
        }
    }, [showInfo, t, undo])

    const handleRedo = useCallback(() => {
        const actionType = redo()
        if (actionType) {
            showInfo(t('pos.redone', { action: actionType.replace('_', ' ').toLowerCase() }))
        }
    }, [redo, showInfo, t])

    // Enhanced keyboard shortcuts
    useEffect(() => {
        const handleKeyPress = (e) => {
            // Ignore if typing in input (except F-keys and Ctrl combinations)
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') {
                // Allow F-keys and Ctrl combinations even in inputs
                if (!e.key || (!e.key.startsWith('F') && !e.ctrlKey)) return
            }

            // F1 - Focus barcode
            if (e.key === 'F1') {
                e.preventDefault()
                barcodeRef.current?.focus()
                showInfo(t('pos.barcodeFocused'))
            }
            // F2 - Focus search
            else if (e.key === 'F2') {
                e.preventDefault()
                searchRef.current?.focus()
                showInfo(t('pos.searchFocused'))
            }
            // F3 - Select customer
            else if (e.key === 'F3') {
                e.preventDefault()
                setShowCustomerModal(true)
            }
            // F4 - Hold current sale
            else if (e.key === 'F4') {
                e.preventDefault()
                handleHoldSale()
            }
            // F5 - Retrieve held sale
            else if (e.key === 'F5') {
                e.preventDefault()
                setShowHoldSalesModal(true)
            }
            // F6 - Search Sales (New)
            else if (e.key === 'F6') {
                e.preventDefault()
                setShowSearchModal(true)
            }
            // F9 - Process payment
            else if (e.key === 'F9' && items.length > 0) {
                e.preventDefault()
                openPaymentModal()
            }
            // Ctrl+Z - Undo
            else if (e.ctrlKey && e.key === 'z' && canUndo) {
                e.preventDefault()
                handleUndo()
            }
            // Ctrl+Y - Redo
            else if (e.ctrlKey && e.key === 'y' && canRedo) {
                e.preventDefault()
                handleRedo()
            }
            // Esc - Clear cart (with confirmation)
            else if (e.key === 'Escape' && items.length > 0 && !showPaymentModal && !showCustomerModal && !showHoldSalesModal && !showSearchModal) {
                void handleClearCart()
            }
            // Delete - Remove last item
            else if (e.key === 'Delete' && items.length > 0 && !showPaymentModal) {
                const lastItem = items[items.length - 1]
                removeItem(lastItem.productId, lastItem.variantId, lastItem.unitId)
                showInfo(t('pos.lastItemRemoved'))
            }
        }

        window.addEventListener('keydown', handleKeyPress)
        return () => window.removeEventListener('keydown', handleKeyPress)
    }, [items.length, showPaymentModal, showCustomerModal, showHoldSalesModal, showSearchModal, removeItem, showInfo, canUndo, canRedo, handleClearCart, openPaymentModal, handleHoldSale, handleUndo, handleRedo, t])

    const handlePaymentComplete = (saleData) => {
        if (!saleData?.attemptId || saleData.attemptId !== currentPaymentAttemptRef.current) {
            return
        }

        const isFirstCompletion = completedPaymentAttemptRef.current !== saleData.attemptId

        setShowPaymentModal(false)

        if (isFirstCompletion) {
            completedPaymentAttemptRef.current = saleData.attemptId
            resetPOS()
            setQuickAccessRefreshKey(prev => prev + 1)
            invalidatePosRuntimeCache({ reason: 'sale-completed' })
            showSuccess(t('pos.saleCompleted', { invoice: saleData.invoiceNumber }))
        }

        setReceiptData(saleData)

        if (saleData?.receiptWarning) {
            showWarning(saleData.receiptWarning)
        }

        if (saleData?.receipt) {
            setShowReceiptModal(true)
            return
        }

        setShowReceiptModal(false)
    }

    const handleSelectInvoice = async (invoice) => {
        setShowSearchModal(false)
        invoiceLoadRequestControllerRef.current?.abort()
        const controller = new AbortController()
        invoiceLoadRequestControllerRef.current = controller

        try {
            // Fetch full details
            const res = await api.get(`/sales/${invoice.id}`, { signal: controller.signal })
            const fullSale = res.data

            const originalItems = Array.isArray(fullSale.items) ? fullSale.items.map(item => ({
                productId: item.productId,
                variantId: item.productVariantId,
                name: item.product?.name || item.productName || t('common.unknown'),
                quantity: item.quantity,
                unitId: item.unitId,
                unitName: item.unit?.name || t('common.unit'),
                conversionFactor: item.conversionFactor,
                unitPrice: item.unitPrice,
                discountAmount: item.discountAmount,
                taxRate: item.taxRate,
                priceIncludesTax: Boolean(item.priceIncludesTax),
            })) : []

            // Render cart immediately with basic data
            setEditingSaleId(fullSale.id)
            setEditingInvoiceNumber(fullSale.invoiceNumber)
            setEditingOriginalItems(originalItems)
            setEditingMutationVersion(Number.isInteger(fullSale.mutationVersion) ? fullSale.mutationVersion : null)

            if (fullSale.warehouse?.id || fullSale.warehouseId) {
                setSelectedWarehouse(fullSale.warehouse?.id || fullSale.warehouseId)
            }

            const saleDataForCart = {
                items: originalItems,
                customer: fullSale.customer?.id ? { id: fullSale.customer.id, name: fullSale.customer.name || t('pos.customer') } : null,
                discountType: fullSale.discountPercentage > 0 ? 'percentage' : 'fixed',
                discountValue: fullSale.discountPercentage > 0 ? fullSale.discountPercentage : fullSale.discountAmount
            }

            currentCartIdRef.current = buildPosCartId()
            retrieveSale(saleDataForCart)
            showSuccess(t('pos.saleLoaded', { number: fullSale.invoiceNumber }))

            // Enrich data asynchronously in the background
            ;(async () => {
                try {
                    const [unitsByProductId, stockValidation] = await Promise.all([
                        loadProductUnitsMap(originalItems.map(item => item.productId), { signal: controller.signal }),
                        revalidatePosCartStock({
                            items: originalItems,
                            baselineItems: originalItems,
                            warehouseId: fullSale.warehouse?.id || fullSale.warehouseId,
                            signal: controller.signal,
                        }),
                    ])

                    if (controller.signal.aborted) {
                        return
                    }

                    const effectiveStockByKey = new Map(
                        stockValidation.effectiveStockLevels.map(stockLevel => [
                            `${stockLevel.productId}::${stockLevel.variantId || 'default'}`,
                            stockLevel.stockQuantity,
                        ]),
                    )

                    const mappedItems = originalItems.map(item => {
                        const productUnits = getProductUnitsForProduct(unitsByProductId, item.productId)
                        const productUnit = productUnits.find(unit => unit.unitId === item.unitId)

                        return {
                            ...item,
                            unitName: productUnit?.unitName || item.unitName || t('common.unit'),
                            productUnits,
                            stockQuantity: Math.max(
                                0,
                                Number(effectiveStockByKey.get(`${item.productId}::${item.variantId || 'default'}`))
                                    || Number(item.quantity) * Math.max(1, Number(item.conversionFactor || 1)),
                            ),
                        }
                    })

                    // Update with enriched data
                    setEditingOriginalItems(mappedItems)
                    
                    const enrichedSaleDataForCart = {
                        items: mappedItems,
                        customer: fullSale.customer?.id ? { id: fullSale.customer.id, name: fullSale.customer.name || t('pos.customer') } : null,
                        discountType: fullSale.discountPercentage > 0 ? 'percentage' : 'fixed',
                        discountValue: fullSale.discountPercentage > 0 ? fullSale.discountPercentage : fullSale.discountAmount
                    }

                    retrieveSale(enrichedSaleDataForCart)
                } catch (enrichmentError) {
                    if (controller.signal.aborted || isRequestCanceled(enrichmentError)) {
                        return
                    }
                    console.error('Error enriching sale data:', enrichmentError)
                    // Sale is already loaded with basic data, enrichment failure is non-critical
                }
            })()

        } catch (error) {
            if (controller.signal.aborted || isRequestCanceled(error)) {
                return
            }

            console.error("Error loading sale", error)
            showError(t('pos.failedToLoadSale'))
        } finally {
            if (invoiceLoadRequestControllerRef.current === controller) {
                invoiceLoadRequestControllerRef.current = null
            }
        }
    }

    return (
        <div className="h-[calc(100vh-120px)] flex flex-col" dir={isRTL ? 'rtl' : 'ltr'}>
            {/* Header */}
            <div className={`mb-4 flex items-center justify-between gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                <div className={`flex items-center gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                    <h2 className="text-2xl font-bold text-gray-900">{t('pos.title')}</h2>
                    {editingInvoiceNumber && (
                        <span className="bg-orange-100 text-orange-800 px-3 py-1 rounded-full text-sm font-bold border border-orange-200">
                            {t('common.editing')}: #{editingInvoiceNumber}
                        </span>
                    )}
                </div>

                <div className={`flex items-center gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                    {/* Search Button */}
                    <button
                        onClick={() => setShowSearchModal(true)}
                        className="btn-secondary"
                        title={t('pos.searchSalesShortcut')}
                    >
                        {t('common.search')}
                    </button>

                    {/* Print Format Toggle */}
                    <div className="flex bg-gray-100 rounded-lg p-1">
                        <button
                            onClick={() => updateLocalSettings('printFormat', 'receipt')}
                            className={`px-3 py-1 text-sm rounded-md transition-colors ${settings?.printFormat !== 'a4' ? 'bg-white shadow text-blue-600 font-medium' : 'text-gray-500 hover:text-gray-700'}`}
                            title={t('pos.receiptPrintFormatTitle')}
                        >
                            80mm
                        </button>
                        <button
                            onClick={() => updateLocalSettings('printFormat', 'a4')}
                            className={`px-3 py-1 text-sm rounded-md transition-colors ${settings?.printFormat === 'a4' ? 'bg-white shadow text-blue-600 font-medium' : 'text-gray-500 hover:text-gray-700'}`}
                            title={t('pos.a4PrintFormatTitle')}
                        >
                            A4
                        </button>
                    </div>

                    <div className={`flex items-center gap-2 ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <label className="text-sm font-medium text-gray-700">{t('pos.warehouse')}:</label>
                        <select
                            value={selectedWarehouse}
                            onChange={(e) => handleWarehouseChange(e.target.value)}
                            className="input text-sm min-w-[150px]"
                            disabled={!!editingSaleId} // Disable warehouse change during edit
                        >
                            {warehouses.map(w => (
                                <option key={w.id} value={w.id}>
                                    {w.name} {w.isDefault ? t('pos.defaultWarehouse') : ''}
                                </option>
                            ))}
                        </select>
                    </div>
                    <span className="text-sm text-gray-600">
                        {t('pos.customer')}: {customer ? customer.name : t('pos.walkIn')}
                    </span>
                    <button
                        onClick={() => setShowCustomerModal(true)}
                        className="btn-secondary text-sm"
                    >
                        {t('pos.selectCustomerF3')}
                    </button>
                </div>
            </div>

            {pendingExternalCart ? (
                <div className={`mb-4 flex flex-col gap-3 rounded-lg border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900 sm:flex-row sm:items-center sm:justify-between ${isRTL ? 'sm:flex-row-reverse text-right' : 'text-left'}`}>
                    <span className="font-medium">
                        {t('pos.cartChangedInAnotherTab', 'POS cart changed in another tab. Choose which cart should stay active.')}
                    </span>
                    <div className={`flex gap-2 ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <button
                            type="button"
                            onClick={handleSyncExternalCart}
                            className="rounded-md bg-amber-700 px-3 py-2 text-sm font-semibold text-white hover:bg-amber-800"
                        >
                            {t('pos.syncOtherTabCart', 'Sync other tab')}
                        </button>
                        <button
                            type="button"
                            onClick={handleKeepLocalCart}
                            className="rounded-md border border-amber-400 bg-white px-3 py-2 text-sm font-semibold text-amber-900 hover:bg-amber-100"
                        >
                            {t('pos.keepThisCart', 'Keep this cart')}
                        </button>
                    </div>
                </div>
            ) : null}

            {/* Main Content */}
            <div className="flex-1 grid grid-cols-1 lg:grid-cols-3 gap-6 overflow-hidden">
                {/* Left Panel - Product Search */}
                <div className="lg:col-span-2 flex flex-col gap-4 overflow-hidden">
                    {/* Barcode Scanner */}
                    <div className="card">
                        <h3 className="text-lg font-semibold mb-3">{t('pos.barcodeScanner')}</h3>
                        <BarcodeScanner
                            ref={barcodeRef}
                            onProductFound={handleProductFound}
                            onError={showError}
                            autoFocus={true}
                            warehouseId={selectedWarehouse}
                        />
                    </div>

                    {/* Product Search */}
                    <div className="card">
                        <h3 className="text-lg font-semibold mb-3">{t('pos.productSearch')}</h3>
                        <ProductSearch
                            ref={searchRef}
                            onProductSelect={handleProductSelect}
                            warehouseId={selectedWarehouse}
                        />
                    </div>

                    {/* Quick Access Products */}
                    <div className="card flex-1 overflow-hidden flex flex-col">
                        <h3 className="text-lg font-semibold mb-3">{t('pos.quickAccess')}</h3>
                        <div className="flex-1 overflow-hidden">
                            <QuickProductGrid
                                onProductSelect={handleProductSelect}
                                warehouseId={selectedWarehouse}
                                refreshKey={quickAccessRefreshKey}
                            />
                        </div>
                    </div>
                </div>

                {/* Right Panel - Shopping Cart */}
                <div className="flex flex-col gap-4 overflow-hidden">
                    {/* Cart Items */}
                    <div className="card flex-1 flex flex-col overflow-hidden">
                        <h3 className="text-lg font-semibold mb-3">
                            {t('pos.cart')} ({t('pos.cartItemCount', { count: totals.itemCount })})
                        </h3>

                        {items.length === 0 ? (
                            <div className="flex-1 flex items-center justify-center text-gray-500">
                                <div className={`text-center ${isRTL ? 'text-right' : 'text-left'}`}>
                                    <div>{t('pos.cartEmpty')}</div>
                                    <div className="text-sm mt-1">{t('pos.scanOrSearch')}</div>
                                </div>
                            </div>
                        ) : (
                            <div className="flex-1 overflow-y-auto border border-gray-200 rounded">
                                {items.map((item) => (
                                    <CartItem
                                        key={`${item.productId}-${item.variantId}-${item.unitId}`}
                                        item={item}
                                        onUpdateQuantity={updateQuantity}
                                        onUpdateDiscount={updateItemDiscount}
                                        onUpdateUnit={updateUnit}
                                        onRemove={removeItem}
                                    />
                                ))}
                            </div>
                        )}
                    </div>

                    {/* Totals & Payment */}
                    <div className="card">
                        {/* Discount Controls */}
                        <div className="mb-4 p-3 bg-gray-50 rounded">
                            <label className="label">{t('pos.invoiceDiscount')}</label>
                            <div className="flex gap-2">
                                <select
                                    value={discountType}
                                    onChange={(e) => {
                                        const nextDiscountType = e.target.value
                                        setDiscountType(nextDiscountType)
                                        setDiscountValue(clampDiscountValue(discountValue, nextDiscountType))
                                    }}
                                    className="input flex-1"
                                >
                                    <option value="percentage">{t('pos.percentageDiscount')}</option>
                                    <option value="fixed">{t('pos.fixedDiscount')}</option>
                                </select>
                                <input
                                    type="number"
                                    value={discountValue}
                                    onChange={(e) => setDiscountValue(clampDiscountValue(e.target.value))}
                                    onBlur={(e) => {
                                        // Normalize the value on blur to ensure it's valid
                                        const normalized = clampDiscountValue(e.target.value)
                                        const parsed = parseStrictAmountInput(normalized, { allowZero: true })
                                        if (parsed.isValid) {
                                            setDiscountValue(parsed.value)
                                        } else if (normalized === '' || normalized === '.' || normalized === '-') {
                                            // Clear invalid transitional states on blur
                                            setDiscountValue(0)
                                        }
                                    }}
                                    className="input w-24"
                                    min="0"
                                    max={discountType === 'percentage' ? 100 : remainingSubtotal}
                                    step="0.01"
                                    placeholder={t('common.amountPlaceholder')}
                                />
                            </div>
                        </div>

                        {/* Totals */}
                        <div className="space-y-2 mb-4">
                            <div className="flex justify-between text-sm">
                                <span className="text-gray-600">{t('common.subtotal')}:</span>
                                <span className="font-medium">{formatCurrency(totals.subtotal)}</span>
                            </div>
                            {totals.itemDiscounts > 0 && (
                                <div className="flex justify-between text-sm text-orange-600">
                                    <span>{t('pos.itemDiscounts')}:</span>
                                    <span>-{formatCurrency(totals.itemDiscounts)}</span>
                                </div>
                            )}
                            {totals.invoiceDiscount > 0 && (
                                <div className="flex justify-between text-sm text-orange-600">
                                    <span>{t('pos.invoiceDiscount')}:</span>
                                    <span>-{formatCurrency(totals.invoiceDiscount)}</span>
                                </div>
                            )}
                            <div className="flex justify-between text-sm">
                                <span className="text-gray-600">{t('common.tax')}:</span>
                                <span className="font-medium">{formatCurrency(totals.tax)}</span>
                            </div>
                            <div className="flex justify-between text-xl font-bold pt-2 border-t-2 border-gray-300">
                                <span>{t('common.total').toUpperCase()}:</span>
                                <span className="text-primary-600">{formatCurrency(totals.total)}</span>
                            </div>
                        </div>

                        {/* Payment Buttons */}
                        <div className="space-y-2">
                            <button
                                onClick={openPaymentModal}
                                disabled={isPreparingCheckout || Boolean(getSaleValidationError())}
                                className={`w-full text-white font-bold py-3 rounded shadow transition-colors disabled:opacity-50 disabled:cursor-not-allowed ${editingSaleId ? 'bg-orange-600 hover:bg-orange-700' : 'bg-primary-600 hover:bg-primary-700'}`}
                            >
                                {isPreparingCheckout
                                    ? t('pos.checkingStock')
                                    : (editingSaleId ? t('common.update') : t('pos.processPayment'))}
                            </button>
                            <button
                                onClick={handleClearCart}
                                disabled={items.length === 0}
                                className="w-full btn-secondary disabled:opacity-50 disabled:cursor-not-allowed"
                            >
                                {t('pos.clearCart')}
                            </button>
                        </div>
                    </div>
                </div>
            </div>

            {/* Payment Modal */}
            {showPaymentModal && (
                <PaymentModal
                    total={totals.total}
                    items={items}
                    customer={customer}
                    discountType={discountType}
                    discountValue={discountValue}
                    warehouseId={selectedWarehouse}
                    paymentAttemptId={currentPaymentAttemptRef.current}
                    posCartId={editingSaleId ? null : currentCartIdRef.current}
                    editingSaleId={editingSaleId}
                    editingMutationVersion={editingMutationVersion}
                    onRevalidateStock={revalidateCheckoutStock}
                    onClose={() => {
                        currentPaymentAttemptRef.current = null
                        setShowPaymentModal(false)
                    }}
                    onComplete={handlePaymentComplete}
                    onError={showError}
                />
            )}

            {/* Receipt Modal */}
            {showReceiptModal && receiptData && (
                <ReceiptModal
                    receiptData={receiptData}
                    onClose={() => setShowReceiptModal(false)}
                />
            )}

            {/* Customer Select Modal */}
            {showCustomerModal && (
                <CustomerSelectModal
                    onSelect={handleCustomerSelect}
                    onClose={() => setShowCustomerModal(false)}
                />
            )}

            {/* Hold Sales Modal */}
            {showHoldSalesModal && (
                <HoldSalesModal
                    onRetrieve={handleRetrieveSale}
                    onClose={() => setShowHoldSalesModal(false)}
                />
            )}

            {/* Search Invoice Modal */}
            {showSearchModal && (
                <InvoiceSearchModal
                    type="sales"
                    onClose={() => setShowSearchModal(false)}
                    onSelect={handleSelectInvoice}
                    onPrint={(invoice) => {
                        // Need to fetch full receipt data to print
                        api.get(`/Sales/receipt/${invoice.id}`)
                            .then(res => {
                                setReceiptData({ receipt: res.data })
                                setShowReceiptModal(true)
                                setShowSearchModal(false)
                            })
                            .catch(err => {
                                console.error(err)
                                showError(t('common.printFailed'))
                            })
                    }}
                />
            )}

            {/* PHASE 11.6: Variant Selection Modal */}
            {showVariantModal && pendingProduct && (
                <VariantSelectModal
                    product={pendingProduct}
                    warehouseId={selectedWarehouse}
                    onSelect={handleVariantSelect}
                    onClose={() => {
                        setShowVariantModal(false)
                        setPendingProduct(null)
                    }}
                />
            )}
        </div>
    )
}

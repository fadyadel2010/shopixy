import { useState, useCallback, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { saveHeldSale, generateHoldReference } from '../utils/localStorage'
import {
    roundCurrency,
    calculateLineSubtotal,
    clampLineDiscount,
    calculateRemainingSubtotal,
    toSafeNumber,
} from '../utils/financialMath'

const clampNumber = (value, min, max) => Math.min(max, Math.max(min, value))

const matchCartLine = (item, productId, variantId = null, unitId = null) =>
    item.productId === productId &&
    item.variantId === variantId &&
    (unitId == null || item.unitId === unitId)

const getUnitConversionFactor = (item, unitId = item.unitId) => {
    const selectedUnit = item.productUnits?.find(unit => unit.unitId === unitId)
    const conversionFactor = toSafeNumber(selectedUnit?.conversionFactor ?? item.conversionFactor, 1)
    return conversionFactor > 0 ? conversionFactor : 1
}

const getMaxQuantityForUnit = (stockQuantity, conversionFactor) => {
    const safeStockQuantity = Math.max(0, toSafeNumber(stockQuantity))
    const safeConversionFactor = toSafeNumber(conversionFactor, 1)

    if (safeConversionFactor <= 0) {
        return 0
    }

    return safeStockQuantity / safeConversionFactor
}

const clampLineQuantity = (item, quantity, unitId = item.unitId) => {
    const safeQuantity = Math.max(0, toSafeNumber(quantity))
    const maxQuantity = getMaxQuantityForUnit(item.stockQuantity, getUnitConversionFactor(item, unitId))
    return clampNumber(safeQuantity, 0, maxQuantity)
}

const hasOverride = (object, key) => Object.prototype.hasOwnProperty.call(object, key)

const cloneCartSnapshot = (snapshot) => {
    if (typeof globalThis.structuredClone === 'function') {
        return globalThis.structuredClone(snapshot)
    }

    return JSON.parse(JSON.stringify(snapshot))
}

export function usePOSCart() {
    const { t } = useTranslation()
    const [items, setItemsState] = useState([])
    const [customer, setCustomerState] = useState(null)
    const [discountType, setDiscountTypeState] = useState('percentage') // 'percentage' or 'fixed'
    const [discountValue, setDiscountValueState] = useState(0)
    const [history, setHistoryState] = useState([])
    const [historyIndex, setHistoryIndexState] = useState(-1)

    const itemsRef = useRef(items)
    const customerRef = useRef(customer)
    const discountTypeRef = useRef(discountType)
    const discountValueRef = useRef(discountValue)
    const historyRef = useRef(history)
    const historyIndexRef = useRef(historyIndex)

    const setItems = useCallback((nextItems) => {
        itemsRef.current = nextItems
        setItemsState(nextItems)
    }, [])

    const setCustomer = useCallback((nextCustomer) => {
        const resolvedCustomer = typeof nextCustomer === 'function'
            ? nextCustomer(customerRef.current)
            : nextCustomer

        customerRef.current = resolvedCustomer
        setCustomerState(resolvedCustomer)
    }, [])

    const setDiscountType = useCallback((nextDiscountType) => {
        const resolvedDiscountType = typeof nextDiscountType === 'function'
            ? nextDiscountType(discountTypeRef.current)
            : nextDiscountType

        discountTypeRef.current = resolvedDiscountType
        setDiscountTypeState(resolvedDiscountType)
    }, [])

    const setDiscountValue = useCallback((nextDiscountValue) => {
        const resolvedDiscountValue = typeof nextDiscountValue === 'function'
            ? nextDiscountValue(discountValueRef.current)
            : nextDiscountValue

        discountValueRef.current = resolvedDiscountValue
        setDiscountValueState(resolvedDiscountValue)
    }, [])

    const setHistoryCursor = useCallback((nextHistoryIndex) => {
        historyIndexRef.current = nextHistoryIndex
        setHistoryIndexState(nextHistoryIndex)
    }, [])

    const commitHistory = useCallback((nextHistory, nextHistoryIndex) => {
        historyRef.current = nextHistory
        historyIndexRef.current = nextHistoryIndex
        setHistoryState(nextHistory)
        setHistoryIndexState(nextHistoryIndex)
    }, [])

    const clearHistory = useCallback(() => {
        commitHistory([], -1)
    }, [commitHistory])

    const captureCartSnapshot = useCallback((overrides = {}) => cloneCartSnapshot({
        items: hasOverride(overrides, 'items') ? overrides.items : itemsRef.current,
        customer: hasOverride(overrides, 'customer') ? overrides.customer : customerRef.current,
        discountType: hasOverride(overrides, 'discountType') ? overrides.discountType : discountTypeRef.current,
        discountValue: hasOverride(overrides, 'discountValue') ? overrides.discountValue : discountValueRef.current,
    }), [])

    const restoreCartSnapshot = useCallback((snapshot) => {
        const clonedSnapshot = cloneCartSnapshot(snapshot)

        setItems(clonedSnapshot.items)
        setCustomer(clonedSnapshot.customer)
        setDiscountType(clonedSnapshot.discountType)
        setDiscountValue(clonedSnapshot.discountValue)
    }, [setCustomer, setDiscountType, setDiscountValue, setItems])

    // Record action for undo/redo
    const recordAction = useCallback((type, previousState, newState) => {
        const truncatedHistory = historyRef.current.slice(0, historyIndexRef.current + 1)
        truncatedHistory.push({
            type,
            timestamp: Date.now(),
            previousState,
            newState,
        })

        const nextHistory = truncatedHistory.slice(-20)
        commitHistory(nextHistory, nextHistory.length - 1)
    }, [commitHistory])

    // Add item to cart
    const addItem = useCallback(async (product, quantity = 1) => {
        const requestedQuantity = Math.max(0, toSafeNumber(quantity))
        const availableStock = Math.max(0, toSafeNumber(product?.stockQuantity))

        if (!product?.id || requestedQuantity <= 0) {
            return { success: false, availableStock }
        }

        const productUnits = Array.isArray(product.productUnits) ? product.productUnits : []
        const defaultUnit = productUnits.find(u => u.isDefault) || productUnits[0] || null

        const defaultUnitId = defaultUnit?.unitId || null
        const defaultConversionFactor = toSafeNumber(defaultUnit?.conversionFactor, 1)
        const requestedBaseQuantity = requestedQuantity * defaultConversionFactor
        const currentItems = itemsRef.current
        const previousState = captureCartSnapshot()
        const existingIndex = currentItems.findIndex(
            item => matchCartLine(item, product.id, product.variantId || null, defaultUnitId)
        )

        let nextItems

        if (existingIndex >= 0) {
            const existingItem = currentItems[existingIndex]
            const existingBaseQuantity = toSafeNumber(existingItem.quantity) * getUnitConversionFactor(existingItem)

            if (existingBaseQuantity + requestedBaseQuantity > availableStock) {
                return { success: false, availableStock }
            }

            nextItems = [...currentItems]
            nextItems[existingIndex] = {
                ...existingItem,
                quantity: toSafeNumber(existingItem.quantity) + requestedQuantity,
                stockQuantity: availableStock,
                productUnits,
                discountAmount: clampLineDiscount(
                    toSafeNumber(existingItem.quantity) + requestedQuantity,
                    existingItem.unitPrice,
                    existingItem.discountAmount,
                ),
            }
        } else {
            if (requestedBaseQuantity > availableStock) {
                return { success: false, availableStock }
            }

            // Add new item
            // PHASE 11.6: Include variant attributes for display
            nextItems = [...currentItems, {
                productId: product.id,
                variantId: product.variantId || null,
                name: product.name,
                barcode: product.barcode || product.sku,
                sku: product.sku,
                unitId: defaultUnitId,
                unitName: defaultUnit?.unitName || product.unit || t('common.piece'),
                conversionFactor: defaultConversionFactor,
                unitPrice: Math.max(0, toSafeNumber(defaultUnit?.sellingPrice ?? product.canonicalUnitPrice)),
                quantity: requestedQuantity,
                taxRate: Math.max(0, toSafeNumber(product.taxRate)),
                priceIncludesTax: Boolean(product.priceIncludesTax),
                unit: product.unit,
                stockQuantity: availableStock,
                discountAmount: 0,
                images: product.images,
                productUnits: productUnits, // Store available units
                // PHASE 11.6: Variant display information
                variantAttributes: product.variantAttributes || [],
                variantDisplayName: product.variantDisplayName || null,
            }]
        }

        setItems(nextItems)
        recordAction('ADD_ITEM', previousState, captureCartSnapshot({ items: nextItems }))
        return { success: true, availableStock }
    }, [captureCartSnapshot, recordAction, setItems, t])

    // Remove item from cart
    const removeItem = useCallback((productId, variantId = null, unitId = null) => {
        const currentItems = itemsRef.current
        const nextItems = currentItems.filter(item =>
            !matchCartLine(item, productId, variantId, unitId)
        )

        if (nextItems.length === currentItems.length) {
            return
        }

        const previousState = captureCartSnapshot()
        setItems(nextItems)
        recordAction('REMOVE_ITEM', previousState, captureCartSnapshot({ items: nextItems }))
    }, [captureCartSnapshot, recordAction, setItems])

    // Update item quantity
    const updateQuantity = useCallback((productId, variantId, quantity, unitId = null) => {
        const currentItems = itemsRef.current
        const previousState = captureCartSnapshot()
        let didChange = false

        const nextItems = currentItems.flatMap(item => {
            if (!matchCartLine(item, productId, variantId, unitId)) {
                return [item]
            }

            const currentQuantity = toSafeNumber(item.quantity)
            const safeQuantity = clampLineQuantity(item, quantity, unitId)

            if (safeQuantity <= 0) {
                didChange = true
                return []
            }

            const currentDiscountAmount = clampLineDiscount(currentQuantity, item.unitPrice, item.discountAmount)
            const nextDiscountAmount = clampLineDiscount(safeQuantity, item.unitPrice, item.discountAmount)

            if (safeQuantity === currentQuantity && nextDiscountAmount === currentDiscountAmount) {
                return [item]
            }

            didChange = true
            return [{
                ...item,
                quantity: safeQuantity,
                discountAmount: nextDiscountAmount,
            }]
        })

        if (!didChange) {
            return
        }

        setItems(nextItems)
        recordAction('CHANGE_QUANTITY', previousState, captureCartSnapshot({ items: nextItems }))
    }, [captureCartSnapshot, recordAction, setItems])

    // Update item discount
    const updateItemDiscount = useCallback((productId, variantId, discountAmount, unitId = null) => {
        const currentItems = itemsRef.current
        const previousState = captureCartSnapshot()
        let didChange = false

        const nextItems = currentItems.map(item => {
            if (!matchCartLine(item, productId, variantId, unitId)) {
                return item
            }

            const currentDiscountAmount = clampLineDiscount(item.quantity, item.unitPrice, item.discountAmount)
            const nextDiscountAmount = clampLineDiscount(item.quantity, item.unitPrice, discountAmount)

            if (currentDiscountAmount === nextDiscountAmount) {
                return item
            }

            didChange = true
            return {
                ...item,
                discountAmount: nextDiscountAmount,
            }
        })

        if (!didChange) {
            return
        }

        setItems(nextItems)
        recordAction('CHANGE_DISCOUNT', previousState, captureCartSnapshot({ items: nextItems }))
    }, [captureCartSnapshot, recordAction, setItems])

    // Update item unit
    const updateUnit = useCallback((productId, variantId, unitId) => {
        const currentItems = itemsRef.current
        const previousState = captureCartSnapshot()
        let didUpdate = false

        const nextItems = currentItems.flatMap(item => {
            if (didUpdate || item.productId !== productId || item.variantId !== variantId) {
                return [item]
            }

            const selectedUnit = item.productUnits?.find(u => u.unitId === unitId)
            if (!selectedUnit) {
                return [item]
            }

            const nextUnitPrice = Math.max(0, toSafeNumber(selectedUnit.sellingPrice ?? item.unitPrice))
            const nextConversionFactor = toSafeNumber(selectedUnit.conversionFactor, 1)
            const safeQuantity = clampNumber(
                toSafeNumber(item.quantity),
                0,
                getMaxQuantityForUnit(item.stockQuantity, nextConversionFactor),
            )
            const currentDiscountAmount = clampLineDiscount(item.quantity, item.unitPrice, item.discountAmount)
            const nextDiscountAmount = clampLineDiscount(safeQuantity, nextUnitPrice, item.discountAmount)

            if (
                item.unitId === selectedUnit.unitId &&
                item.unitName === selectedUnit.unitName &&
                toSafeNumber(item.conversionFactor, 1) === nextConversionFactor &&
                Math.max(0, toSafeNumber(item.unitPrice)) === nextUnitPrice &&
                toSafeNumber(item.quantity) === safeQuantity &&
                currentDiscountAmount === nextDiscountAmount
            ) {
                return [item]
            }

            didUpdate = true

            if (safeQuantity <= 0) {
                return []
            }

            return [{
                ...item,
                unitId: selectedUnit.unitId,
                unitName: selectedUnit.unitName,
                conversionFactor: nextConversionFactor,
                unitPrice: nextUnitPrice,
                quantity: safeQuantity,
                discountAmount: nextDiscountAmount,
            }]
        })

        if (!didUpdate) {
            return
        }

        setItems(nextItems)
        recordAction('CHANGE_UNIT', previousState, captureCartSnapshot({ items: nextItems }))
    }, [captureCartSnapshot, recordAction, setItems])

    // Calculate totals
    const calculateTotals = useCallback(() => {
        const subtotal = roundCurrency(items.reduce((sum, item) => (
            sum + calculateLineSubtotal(item.quantity, item.unitPrice)
        ), 0))

        const itemDiscounts = roundCurrency(items.reduce((sum, item) => (
            sum + clampLineDiscount(item.quantity, item.unitPrice, item.discountAmount)
        ), 0))

        const remainingSubtotal = calculateRemainingSubtotal(subtotal, itemDiscounts)
        const safeDiscountValue = Math.max(0, roundCurrency(discountValue))
        const invoiceDiscount = discountType === 'percentage'
            ? roundCurrency(remainingSubtotal * clampNumber(safeDiscountValue, 0, 100) / 100)
            : clampNumber(safeDiscountValue, 0, remainingSubtotal)

        const totalDiscount = roundCurrency(itemDiscounts + invoiceDiscount)

        const tax = roundCurrency(items.reduce((sum, item) => {
            if (item.priceIncludesTax) return sum
            const itemSubtotal = Math.max(0, roundCurrency(
                calculateLineSubtotal(item.quantity, item.unitPrice)
                - clampLineDiscount(item.quantity, item.unitPrice, item.discountAmount),
            ))
            return sum + (itemSubtotal * Math.max(0, toSafeNumber(item.taxRate)) / 100)
        }, 0))

        const total = Math.max(0, roundCurrency(subtotal - totalDiscount + tax))

        return {
            subtotal,
            itemDiscounts,
            invoiceDiscount,
            totalDiscount,
            tax,
            total,
            itemCount: items.reduce((sum, item) => sum + Math.max(0, toSafeNumber(item.quantity)), 0),
        }
    }, [items, discountType, discountValue])

    // Clear cart
    const clearCart = useCallback(() => {
        setItems([])
        setCustomer(null)
        setDiscountType('percentage')
        setDiscountValue(0)
        clearHistory()
    }, [clearHistory, setCustomer, setDiscountType, setDiscountValue, setItems])

    // Check if item can be added (stock validation)
    const canAddItem = useCallback((product, requestedQty = 1) => {
        const safeRequestedQuantity = Math.max(0, toSafeNumber(requestedQty))
        const requestedBaseQuantity = safeRequestedQuantity * Math.max(1, toSafeNumber(product?.conversionFactor, 1))
        const availableStock = Math.max(0, toSafeNumber(product?.stockQuantity))
        const currentBaseQuantity = items
            .filter(item => item.productId === product?.id && item.variantId === (product?.variantId || null))
            .reduce((sum, item) => sum + (toSafeNumber(item.quantity) * getUnitConversionFactor(item)), 0)

        return currentBaseQuantity + requestedBaseQuantity <= availableStock
    }, [items])

    const syncStockLevels = useCallback((stockLevels) => {
        if (!Array.isArray(stockLevels) || stockLevels.length === 0) {
            return
        }

        const stockMap = new Map(
            stockLevels.map((stockLevel) => [
                `${stockLevel.productId}::${stockLevel.variantId || 'default'}`,
                Math.max(0, toSafeNumber(stockLevel.stockQuantity)),
            ]),
        )

        const currentItems = itemsRef.current
        let didChange = false
        const nextItems = currentItems.map(item => {
            const nextStockQuantity = stockMap.get(`${item.productId}::${item.variantId || 'default'}`)

            if (nextStockQuantity == null || nextStockQuantity === item.stockQuantity) {
                return item
            }

            didChange = true
            return {
                ...item,
                stockQuantity: nextStockQuantity,
            }
        })

        if (!didChange) {
            return
        }

        setItems(nextItems)
    }, [setItems])

    // Hold current sale
    const holdSale = useCallback(() => {
        if (items.length === 0) return null

        const totals = calculateTotals()
        const heldSale = {
            id: typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
                ? crypto.randomUUID()
                : `hold_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`,
            referenceNumber: generateHoldReference(),
            heldAt: new Date().toISOString(),
            customer,
            items,
            discountType,
            discountValue,
            total: totals.total,
            itemCount: totals.itemCount,
        }

        try {
            saveHeldSale(heldSale)
            clearCart()
            return heldSale.referenceNumber
        } catch (error) {
            console.error('Failed to hold sale:', error)
            // Don't clear cart if save failed - preserve the cart state
            throw error
        }
    }, [items, customer, discountType, discountValue, calculateTotals, clearCart])

    // Retrieve held sale
    const retrieveSale = useCallback((sale) => {
        setItems(Array.isArray(sale?.items) ? cloneCartSnapshot(sale.items) : [])
        setCustomer(sale?.customer ?? null)
        setDiscountType(sale?.discountType === 'fixed' ? 'fixed' : 'percentage')
        setDiscountValue(Math.max(0, toSafeNumber(sale?.discountValue)))
        clearHistory()
    }, [clearHistory, setCustomer, setDiscountType, setDiscountValue, setItems])

    // Undo last action
    const undo = useCallback(() => {
        if (historyIndexRef.current < 0) return null

        const action = historyRef.current[historyIndexRef.current]
        restoreCartSnapshot(action.previousState)
        setHistoryCursor(historyIndexRef.current - 1)
        return action.type
    }, [restoreCartSnapshot, setHistoryCursor])

    // Redo last undone action
    const redo = useCallback(() => {
        if (historyIndexRef.current >= historyRef.current.length - 1) return null

        const nextHistoryIndex = historyIndexRef.current + 1
        const action = historyRef.current[nextHistoryIndex]
        restoreCartSnapshot(action.newState)
        setHistoryCursor(nextHistoryIndex)
        return action.type
    }, [restoreCartSnapshot, setHistoryCursor])

    return {
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
        canAddItem,
        syncStockLevels,
        holdSale,
        retrieveSale,
        undo,
        redo,
        canUndo: historyIndex >= 0,
        canRedo: historyIndex < history.length - 1,
    }
}

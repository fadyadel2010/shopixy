import { useState, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import { useSettings } from '../contexts/SettingsContext'
import api from '../services/api'
import { useToast } from '../components/Toast'
import ReceiptModal from '../components/ReceiptModal'
import AsyncEntitySelect from '../components/AsyncEntitySelect'
import { buildIdempotencyHeaders, stableStringify } from '../utils/requestSafety'
import {
    roundCurrency,
    toSafeNumber,
    toCurrencyMinorUnits,
    fromCurrencyMinorUnits,
} from '../utils/financialMath'
import { formatDate } from '../utils/dateFormat'
import { searchCustomers } from '../services/entityLookup'

const clampNumber = (value, min, max) => Math.min(max, Math.max(min, value))
export const roundQuantity = (value) => Math.round(toSafeNumber(value) * 1_000_000) / 1_000_000
const toQuantityUnits = (value) => Math.round(toSafeNumber(value) * 1_000_000)

const buildRequestId = (prefix) => `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`

const prorateMinorUnits = (amountMinor, partialBasisUnits, totalBasisUnits) => {
    if (amountMinor === 0 || partialBasisUnits <= 0 || totalBasisUnits <= 0) {
        return 0
    }

    const clampedBasisUnits = Math.min(partialBasisUnits, totalBasisUnits)
    return Math.round((amountMinor * clampedBasisUnits) / totalBasisUnits)
}

const allocateMinorUnitsAcrossLines = (lines, totalAmountMinor, basisSelector) => {
    if (!Array.isArray(lines) || lines.length === 0 || totalAmountMinor === 0) {
        return []
    }

    const totalBasisUnits = lines.reduce(
        (total, line) => total + basisSelector(line),
        0,
    )
    if (totalBasisUnits <= 0) {
        return lines.map(() => 0)
    }

    let allocatedAmountMinor = 0
    let accumulatedBasisUnits = 0

    return lines.map((line) => {
        accumulatedBasisUnits += basisSelector(line)
        const cumulativeAllocationMinor = prorateMinorUnits(
            totalAmountMinor,
            accumulatedBasisUnits,
            totalBasisUnits,
        )
        const currentAllocationMinor = cumulativeAllocationMinor - allocatedAmountMinor
        allocatedAmountMinor = cumulativeAllocationMinor
        return currentAllocationMinor
    })
}

const calculateReturnLine = (item) => {
    const qty = Math.max(0, toSafeNumber(item?.quantity))
    const unitPrice = Math.max(0, toSafeNumber(item?.originalUnitPrice ?? item?.unitPrice))
    const taxRate = Math.max(0, toSafeNumber(item?.originalTaxRate ?? item?.taxRate))
    const lineSubtotal = roundCurrency(qty * unitPrice)
    const taxAmount = roundCurrency(lineSubtotal * (taxRate / 100))
    const lineSubtotalMinor = toCurrencyMinorUnits(lineSubtotal)
    const computedTaxMinor = toCurrencyMinorUnits(taxAmount)
    const quantityUnits = toQuantityUnits(qty)
    const originalQuantityUnits = toQuantityUnits(Math.max(0, toSafeNumber(item?.originalQuantity)))
    const previouslyReturnedQuantityUnits = toQuantityUnits(
        Math.max(0, toSafeNumber(item?.previouslyReturnedQuantity)),
    )
    const hasOriginalTaxAmount = item?.originalTaxAmount != null && item?.originalTaxAmount !== ''
    const lineDiscountMinor = quantityUnits > 0 && originalQuantityUnits > 0
        ? prorateMinorUnits(
            toCurrencyMinorUnits(item?.originalDiscountAmount),
            previouslyReturnedQuantityUnits + quantityUnits,
            originalQuantityUnits,
        ) - prorateMinorUnits(
            toCurrencyMinorUnits(item?.originalDiscountAmount),
            previouslyReturnedQuantityUnits,
            originalQuantityUnits,
        )
        : 0
    const taxMinor = quantityUnits > 0 && originalQuantityUnits > 0 && hasOriginalTaxAmount
        ? prorateMinorUnits(
            toCurrencyMinorUnits(item.originalTaxAmount),
            previouslyReturnedQuantityUnits + quantityUnits,
            originalQuantityUnits,
        ) - prorateMinorUnits(
            toCurrencyMinorUnits(item.originalTaxAmount),
            previouslyReturnedQuantityUnits,
            originalQuantityUnits,
        )
        : computedTaxMinor

    return {
        quantity: qty,
        lineSubtotalMinor,
        lineDiscountMinor,
        discountBasisMinor: lineSubtotalMinor - lineDiscountMinor,
        taxMinor,
    }
}

export const calculateReturnTotals = (items, originalInvoice) => {
    const lineInputs = items.map(item => calculateReturnLine(item))

    const activeLines = lineInputs.filter(line => line.quantity > 0)
    const currentReturnDiscountBasisMinor = activeLines.reduce(
        (total, line) => total + line.discountBasisMinor,
        0,
    )
    const previouslyReturnedDiscountBasisMinor = items.reduce((total, item) => {
        const previousQuantityUnits = toQuantityUnits(
            Math.max(0, toSafeNumber(item?.previouslyReturnedQuantity)),
        )
        const originalQuantityUnits = toQuantityUnits(
            Math.max(0, toSafeNumber(item?.originalQuantity)),
        )
        const previousGrossSubtotalMinor = toCurrencyMinorUnits(
            Math.max(0, toSafeNumber(item?.previouslyReturnedQuantity)) *
            Math.max(0, toSafeNumber(item?.originalUnitPrice ?? item?.unitPrice)),
        )
        const previousLineDiscountMinor = prorateMinorUnits(
            toCurrencyMinorUnits(item?.originalDiscountAmount),
            previousQuantityUnits,
            originalQuantityUnits,
        )
        return total + previousGrossSubtotalMinor - previousLineDiscountMinor
    }, 0)
    const originalInvoiceDiscountMinor = toCurrencyMinorUnits(originalInvoice?.discountAmount)
    const originalInvoiceDiscountBasisMinor = toCurrencyMinorUnits(originalInvoice?.subtotal) - items.reduce(
        (total, item) => total + toCurrencyMinorUnits(item?.originalDiscountAmount),
        0,
    )
    const currentInvoiceDiscountMinor = prorateMinorUnits(
        originalInvoiceDiscountMinor,
        previouslyReturnedDiscountBasisMinor + currentReturnDiscountBasisMinor,
        originalInvoiceDiscountBasisMinor,
    ) - prorateMinorUnits(
        originalInvoiceDiscountMinor,
        previouslyReturnedDiscountBasisMinor,
        originalInvoiceDiscountBasisMinor,
    )
    const invoiceDiscountSharesMinor = allocateMinorUnitsAcrossLines(
        activeLines,
        currentInvoiceDiscountMinor,
        line => line.discountBasisMinor,
    )

    let activeLineIndex = 0
    const lines = lineInputs.map((line) => {
        if (line.quantity <= 0) {
            return {
                invoiceDiscountMinor: 0,
                invoiceDiscount: 0,
                subtotalMinor: 0,
                subtotal: 0,
                taxMinor: 0,
                tax: 0,
                totalMinor: 0,
                total: 0,
            }
        }

        const invoiceDiscountMinor = invoiceDiscountSharesMinor[activeLineIndex] || 0
        activeLineIndex += 1

        const subtotalMinor = line.lineSubtotalMinor - line.lineDiscountMinor - invoiceDiscountMinor
        const totalMinor = subtotalMinor + line.taxMinor

        return {
            invoiceDiscountMinor,
            invoiceDiscount: fromCurrencyMinorUnits(invoiceDiscountMinor),
            subtotalMinor,
            subtotal: fromCurrencyMinorUnits(subtotalMinor),
            taxMinor: line.taxMinor,
            tax: fromCurrencyMinorUnits(line.taxMinor),
            totalMinor,
            total: fromCurrencyMinorUnits(totalMinor),
        }
    })

    const totals = lines.reduce(
        (accumulator, line) => ({
            subtotalMinor: accumulator.subtotalMinor + line.subtotalMinor,
            taxMinor: accumulator.taxMinor + line.taxMinor,
        }),
        { subtotalMinor: 0, taxMinor: 0 },
    )

    return {
        lines,
        subtotal: fromCurrencyMinorUnits(totals.subtotalMinor),
        tax: fromCurrencyMinorUnits(totals.taxMinor),
        total: fromCurrencyMinorUnits(totals.subtotalMinor + totals.taxMinor),
    }
}

const formatVariantDisplayName = (item, t) => {
    const productName = item.productName
    const variantAttributes = item.variantAttributes

    if (Array.isArray(variantAttributes) && variantAttributes.length > 0) {
        return `${productName} - ${variantAttributes.map(attribute => attribute.value).join(' / ')}`
    }

    if (variantAttributes && typeof variantAttributes === 'object' && Object.keys(variantAttributes).length > 0) {
        return `${productName} - ${Object.values(variantAttributes).join(' / ')}`
    }

    if (item.variantSku) {
        return `${productName} (${item.variantSku})`
    }

    return productName
}

export default function SalesReturns() {
    const { t, i18n } = useTranslation()
    const { confirm, confirmAction } = useConfirmDialog()
    const isRTL = i18n.language === 'ar'
    const { formatCurrency, settings, updateLocalSettings } = useSettings()
    const { showSuccess, showError } = useToast()

    // View State
    const [view, setView] = useState('list') // 'list' or 'create'
    const [returns, setReturns] = useState([])
    const [loading, setLoading] = useState(false)
    const [pagination, setPagination] = useState({ page: 1, pageSize: 20, totalPages: 1 })

    // Receipt State
    const [showReceiptModal, setShowReceiptModal] = useState(false)
    const [receiptData, setReceiptData] = useState(null)

    // Form State
    const [warehouses, setWarehouses] = useState([])
    const [salesInvoices, setSalesInvoices] = useState([])
    const [invoicesLoading, setInvoicesLoading] = useState(false)
    const [invoicesError, setInvoicesError] = useState('')
    const [invoiceLoading, setInvoiceLoading] = useState(false)

    const [selectedCustomer, setSelectedCustomer] = useState(null)
    const [warehouseId, setWarehouseId] = useState('')
    const [returnDate, setReturnDate] = useState(new Date().toISOString().split('T')[0])
    const [originalInvoiceId, setOriginalInvoiceId] = useState('')
    const [originalInvoice, setOriginalInvoice] = useState(null)
    const [items, setItems] = useState([])
    const [reason, setReason] = useState('')
    const [notes, setNotes] = useState('')
    const [refundMethod, setRefundMethod] = useState('accountCredit')
    const submissionLockRef = useRef(false)
    const lastSubmissionRef = useRef(null)
    const returnsRequestIdRef = useRef(0)
    const initialDataRequestIdRef = useRef(0)
    const salesInvoicesRequestIdRef = useRef(0)
    const invoiceRequestIdRef = useRef(0)
    const customerId = selectedCustomer?.id || ''

    useEffect(() => {
        if (view === 'list') {
            fetchReturns()
        } else {
            fetchInitialData()
        }
    }, [view, pagination.page])

    const returnSummary = calculateReturnTotals(items, originalInvoice)
    const subtotal = returnSummary.subtotal
    const tax = returnSummary.tax
    const total = returnSummary.total
    const remainingCashRefundable = Math.max(0, toSafeNumber(originalInvoice?.remainingCashRefundableAmount))
    const remainingCardRefundable = Math.max(0, toSafeNumber(originalInvoice?.remainingCardRefundableAmount))
    const hasLegacySettlementGap = Boolean(originalInvoice?.hasLegacySettlementGap)
    const cashRefundUnavailable = hasLegacySettlementGap || remainingCashRefundable <= 0 || total > remainingCashRefundable
    const cardRefundUnavailable = hasLegacySettlementGap || remainingCardRefundable <= 0 || total > remainingCardRefundable
    const returnItems = items.filter(item => toSafeNumber(item.quantity) > 0)
    const hasDraftChanges = Boolean(
        customerId ||
        originalInvoiceId ||
        reason.trim() ||
        notes.trim() ||
        items.some(item => toSafeNumber(item.quantity) > 0) ||
        refundMethod !== 'accountCredit',
    )

    const fetchReturns = async () => {
        const requestId = returnsRequestIdRef.current + 1
        returnsRequestIdRef.current = requestId
        setLoading(true)
        try {
            const response = await api.get(`/salesreturns?page=${pagination.page}&pageSize=${pagination.pageSize}`)
            if (requestId !== returnsRequestIdRef.current) {
                return
            }
            setReturns(Array.isArray(response.data?.items) ? response.data.items : [])
            setPagination(prev => ({
                ...prev,
                totalPages: response.data?.totalPages || 1,
            }))
        } catch (error) {
            if (requestId !== returnsRequestIdRef.current) {
                return
            }
            console.error('Error fetching sales returns:', error)
            showError(t('salesReturns.failedToLoad'))
        } finally {
            if (requestId === returnsRequestIdRef.current) {
                setLoading(false)
            }
        }
    }

    const fetchInitialData = async () => {
        const requestId = initialDataRequestIdRef.current + 1
        initialDataRequestIdRef.current = requestId

        try {
            const warehousesRes = await api.get('/warehouses')
            const warehousesData = warehousesRes.data.items || warehousesRes.data || []

            if (requestId !== initialDataRequestIdRef.current) {
                return
            }

            setWarehouses(warehousesData)

            if (!warehouseId && warehousesData.length > 0) {
                setWarehouseId(warehousesData[0].id)
            }
            await fetchSalesInvoices(null)
        } catch (error) {
            console.error('Error loading initial data:', error)
            showError(t('salesReturns.failedToLoadData'))
        }
    }

    const resetRefundSelection = (nextRefundMethod = 'accountCredit') => {
        setRefundMethod(nextRefundMethod)
    }

    const fetchSalesInvoices = async (nextCustomerId = null) => {
        const requestId = salesInvoicesRequestIdRef.current + 1
        salesInvoicesRequestIdRef.current = requestId
        setInvoicesLoading(true)
        setInvoicesError('')

        try {
            const query = nextCustomerId ? `?customerId=${nextCustomerId}&pageSize=50` : '?pageSize=50'
            const response = await api.get(`/salesreturns/eligible-invoices${query}`)
            if (requestId !== salesInvoicesRequestIdRef.current) {
                return
            }
            const invoicesData = response.data || []
            setSalesInvoices(invoicesData)
        } catch (error) {
            if (requestId !== salesInvoicesRequestIdRef.current) {
                return
            }
            console.error('Error fetching sales invoices:', error)
            setSalesInvoices([])
            setInvoicesError(t('salesReturns.failedToLoadEligibleInvoices'))
        } finally {
            if (requestId === salesInvoicesRequestIdRef.current) {
                setInvoicesLoading(false)
            }
        }
    }

    const handleCustomerChange = (customer) => {
        const nextCustomerId = customer?.id || ''

        salesInvoicesRequestIdRef.current += 1
        invoiceRequestIdRef.current += 1
        setSelectedCustomer(customer)
        setOriginalInvoiceId('')
        setOriginalInvoice(null)
        setItems([])
        resetRefundSelection()
        fetchSalesInvoices(nextCustomerId || null)
    }

    const handleInvoiceSelect = async (invoiceId) => {
        const requestId = invoiceRequestIdRef.current + 1
        invoiceRequestIdRef.current = requestId
        setOriginalInvoiceId(invoiceId)
        resetRefundSelection()
        if (!invoiceId) {
            setOriginalInvoice(null)
            setItems([])
            return
        }

        setInvoiceLoading(true)
        try {
            const response = await api.get(`/salesreturns/invoice/${invoiceId}`)
            if (requestId !== invoiceRequestIdRef.current) {
                return
            }
            setOriginalInvoice(response.data)
            setSelectedCustomer(response.data.customer || null)
            setWarehouseId(response.data.warehouse.id)
            resetRefundSelection(
                response.data.customer
                    ? 'accountCredit'
                    : toSafeNumber(response.data.remainingCashRefundableAmount) > 0
                        ? 'cash'
                        : 'card',
            )

            const returnItems = response.data.items.map(item => {
                return {
                    productId: item.productId,
                    productVariantId: item.productVariantId,
                    name: item.productName,
                    productName: item.productName,
                    variantDisplayName: formatVariantDisplayName(item, t),
                    variantSku: item.variantSku,
                    variantAttributes: item.variantAttributes,
                    unitId: item.unitId,
                    unitName: item.unitName || t('common.unit'),
                    conversionFactor: item.conversionFactor,
                    originalQuantity: roundQuantity(item.soldQuantity),
                    soldQuantity: roundQuantity(item.soldQuantity),
                    previouslyReturnedQuantity: roundQuantity(item.previouslyReturnedQuantity),
                    remainingQuantity: roundQuantity(item.remainingQuantity),
                    quantity: 0,
                    unitPrice: roundCurrency(item.unitPrice),
                    originalUnitPrice: roundCurrency(item.unitPrice),
                    originalDiscountAmount: roundCurrency(item.discountAmount),
                    originalTaxAmount: roundCurrency(item.taxAmount),
                    originalLineTotal: roundCurrency(item.totalAmount),
                    taxRate: roundCurrency(item.taxRate),
                    originalTaxRate: roundCurrency(item.taxRate),
                    reason: '',
                }
            })
            setItems(returnItems)
        } catch (error) {
            if (requestId !== invoiceRequestIdRef.current) {
                return
            }
            console.error('Error fetching invoice:', error)
            setOriginalInvoice(null)
            setItems([])
            showError(t('salesReturns.failedToLoadInvoice'))
        } finally {
            if (requestId === invoiceRequestIdRef.current) {
                setInvoiceLoading(false)
            }
        }
    }

    const updateItem = (index, field, value) => {
        setItems(currentItems => currentItems.map((item, itemIndex) => {
            if (itemIndex !== index) {
                return item
            }

            if (field === 'quantity') {
                return {
                    ...item,
                    quantity: clampNumber(roundQuantity(value), 0, toSafeNumber(item.remainingQuantity)),
                }
            }

            if (field === 'reason') {
                return { ...item, reason: value }
            }

            return item
        }))
    }

    const resetForm = () => {
        salesInvoicesRequestIdRef.current += 1
        invoiceRequestIdRef.current += 1
        setSelectedCustomer(null)
        setWarehouseId(warehouses.length > 0 ? warehouses[0].id : '')
        setReturnDate(new Date().toISOString().split('T')[0])
        setOriginalInvoiceId('')
        setOriginalInvoice(null)
        setItems([])
        setReason('')
        setNotes('')
        resetRefundSelection()
        setSalesInvoices([])
        setInvoicesError('')
        setInvoicesLoading(false)
        setInvoiceLoading(false)
        submissionLockRef.current = false
        lastSubmissionRef.current = null
    }

    const handleBackToList = async () => {
        if (loading) {
            return
        }

        if (hasDraftChanges) {
            const confirmed = await confirm({
                confirmLabel: t('common.confirm'),
                description: t('salesReturns.confirmDiscardDraft'),
                title: t('common.warning'),
                variant: 'warning',
            })

            if (!confirmed) {
                return
            }
        }

        setView('list')
        resetForm()
    }

    const getValidationMessage = () => {
        if (!warehouseId) {
            return t('salesReturns.warehouseRequiredError')
        }

        if (!returnDate) {
            return t('salesReturns.returnDateRequired')
        }

        if (!originalInvoiceId || !originalInvoice) {
            return t('salesReturns.originalInvoiceRequired')
        }

        if (returnItems.length === 0) {
            return t('salesReturns.specifyQuantities')
        }

        for (const item of returnItems) {
            const quantity = roundQuantity(item.quantity)
            const remainingQuantity = roundQuantity(item.remainingQuantity)

            if (!Number.isFinite(quantity) || quantity <= 0) {
                return t('salesReturns.invalidReturnQuantity', { product: item.name })
            }

            if (quantity > remainingQuantity) {
                return t('salesReturns.cannotReturnMore', { product: item.name })
            }

            if (roundCurrency(item.unitPrice) !== roundCurrency(item.originalUnitPrice)) {
                return t('salesReturns.unitPriceLocked')
            }

            if (roundCurrency(item.taxRate) !== roundCurrency(item.originalTaxRate)) {
                return t('salesReturns.taxRateLocked')
            }
        }

        if (total <= 0) {
            return t('salesReturns.totalMustBePositive')
        }

        if (refundMethod === 'accountCredit' && !customerId) {
            return t('salesReturns.customerAccountRequired')
        }

        if (refundMethod !== 'accountCredit' && hasLegacySettlementGap) {
            return t('salesReturns.legacySettlementGap')
        }

        if (refundMethod === 'cash' && cashRefundUnavailable) {
            return t('salesReturns.cashRefundExceedsCollected', {
                amount: formatCurrency(remainingCashRefundable),
            })
        }

        if (refundMethod === 'card' && cardRefundUnavailable) {
            return t('salesReturns.cardRefundExceedsCollected', {
                amount: formatCurrency(remainingCardRefundable),
            })
        }

        return ''
    }

    const validationMessage = getValidationMessage()

    const handleSubmit = async (e) => {
        e.preventDefault()

        if (submissionLockRef.current) {
            return
        }

        if (validationMessage) {
            showError(validationMessage)
            return
        }

        const payload = {
            customerId: customerId || null,
            warehouseId,
            returnDate,
            originalInvoiceId: originalInvoiceId || null,
            items: returnItems.map(item => ({
                productId: item.productId,
                productVariantId: item.productVariantId,
                unitId: item.unitId,
                quantity: roundQuantity(item.quantity),
                reason: item.reason || null,
            })),
            reason: reason.trim() || null,
            notes: notes.trim() || null,
            refundMethod: refundMethod === 'accountCredit'
                ? 'AccountCredit'
                : refundMethod === 'cash'
                    ? 'Cash'
                    : 'Card',
        }
        const receiptItems = items.flatMap((item, index) => (
            toSafeNumber(item.quantity) > 0
                ? [{
                    productName: item.name,
                    variantDescription: Array.isArray(item.variantAttributes)
                        ? item.variantAttributes.map(attribute => `${attribute.name}: ${attribute.value}`).join(' / ')
                        : '',
                    variantSku: item.variantSku,
                    unitName: item.unitName,
                    conversionFactor: item.conversionFactor,
                    quantity: item.quantity,
                    unitPrice: item.originalUnitPrice,
                    total: returnSummary.lines[index]?.total || 0,
                }]
                : []
        ))

        await confirmAction({
            confirmLabel: t('common.confirm'),
            description: t('salesReturns.confirmCreateReturn', { total: formatCurrency(total) }),
            onConfirm: async () => {
                const fingerprint = stableStringify(payload)
                const idempotencyKey = lastSubmissionRef.current?.fingerprint === fingerprint
                    ? lastSubmissionRef.current.key
                    : buildRequestId('sales-return-create')
                const paymentAttemptId = buildRequestId('sales-return-attempt')

                lastSubmissionRef.current = { key: idempotencyKey, fingerprint }
                submissionLockRef.current = true
                setLoading(true)

                try {
                    const response = await api.post('/salesreturns', payload, {
                        headers: buildIdempotencyHeaders(idempotencyKey, paymentAttemptId),
                    })
                    showSuccess(t('salesReturns.returnCreated'))

                    const returnData = response.data
                    setReceiptData({
                        receipt: {
                            invoiceNumber: returnData.returnNumber,
                            originalInvoiceNumber: originalInvoice.invoiceNumber,
                            invoiceDate: returnDate,
                            customerName: selectedCustomer?.name || t('salesHistory.walkInCustomer'),
                            warehouseName: warehouses.find(w => w.id === warehouseId)?.name || t('common.unknown'),
                            items: receiptItems,
                            subtotal,
                            tax,
                            total: returnData.totalAmount || total,
                            discount: 0,
                            paymentMethod: refundMethod === 'accountCredit'
                                ? t('salesReturns.creditToAccount')
                                : refundMethod === 'cash'
                                    ? t('salesReturns.refundFromCash')
                                    : t('salesReturns.refundFromVisa'),
                            paid: returnData.refundAmount || 0,
                            change: 0,
                            reason: reason.trim() || null,
                            notes: notes.trim() || null,
                        },
                    })
                    setShowReceiptModal(true)
                } catch (error) {
                    console.error('Error creating sales return:', error)
                    const message = error.response?.data?.message || t('salesReturns.failedToCreate')
                    showError(message)
                    throw new Error(message)
                } finally {
                    submissionLockRef.current = false
                    setLoading(false)
                }
            },
            title: t('common.confirm'),
            variant: 'warning',
        })
    }

    if (view === 'list') {
        return (
            <div className="h-full flex flex-col gap-6" dir={isRTL ? 'rtl' : 'ltr'}>
                <div className={`flex items-center justify-between ${isRTL ? 'flex-row-reverse' : ''}`}>
                    <h1 className="text-2xl font-bold text-gray-800">{t('salesReturns.title')}</h1>
                    <button
                        onClick={() => setView('create')}
                        className="btn-primary"
                    >
                        {t('salesReturns.newReturn')}
                    </button>
                </div>

                <div className="bg-white rounded-lg shadow overflow-hidden flex-1">
                    <div className="overflow-x-auto">
                        <table className="w-full">
                            <thead className="bg-gray-50 border-b border-gray-200">
                                <tr>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('salesReturns.returnNumber')}</th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('common.date')}</th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('pos.customer')}</th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('salesReturns.originalInvoice')}</th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('common.description')}</th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-left' : 'text-right'}`}>{t('common.total')}</th>
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
                                            {t('salesReturns.noReturnsFound')}
                                        </td>
                                    </tr>
                                ) : (
                                    returns.map(ret => (
                                        <tr key={ret.id} className="hover:bg-gray-50">
                                            <td className="px-6 py-4 text-sm font-medium text-gray-900">
                                                {ret.returnNumber}
                                            </td>
                                            <td className="px-6 py-4 text-sm text-gray-500">
                                                {formatDate(ret.returnDate)}
                                            </td>
                                            <td className="px-6 py-4 text-sm text-gray-900">
                                                {ret.customer?.name || t('salesHistory.walkInCustomer')}
                                            </td>
                                            <td className="px-6 py-4 text-sm text-gray-500">
                                                {ret.originalInvoiceNumber || '-'}
                                            </td>
                                            <td className="px-6 py-4 text-sm text-gray-500">
                                                {ret.reason || '-'}
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
                                {t('salesHistory.previous')}
                            </button>
                            <span className="text-sm text-gray-600">
                                {t('salesHistory.page')} {pagination.page} {t('salesHistory.of')} {pagination.totalPages}
                            </span>
                            <button
                                onClick={() => setPagination(prev => ({ ...prev, page: prev.page + 1 }))}
                                disabled={pagination.page === pagination.totalPages}
                                className="btn-secondary disabled:opacity-50"
                            >
                                {t('salesHistory.next')}
                            </button>
                        </div>
                    )}
                </div>
            </div>
        )
    }

    return (
        <div className="h-full flex flex-col gap-6" dir={isRTL ? 'rtl' : 'ltr'}>
            <div className={`flex flex-wrap justify-between items-center gap-3 ${isRTL ? 'flex-row-reverse' : ''}`}>
                <h1 className="text-2xl font-bold text-gray-800">{t('salesReturns.newReturnTitle')}</h1>
                <div className={`flex flex-wrap items-center gap-3 ${isRTL ? 'flex-row-reverse' : ''}`}>
                    <div className="flex bg-gray-100 rounded-lg p-1">
                        <button
                            type="button"
                            onClick={() => updateLocalSettings('printFormat', 'receipt')}
                            className={`px-3 py-1 text-sm rounded-md transition-colors ${settings?.printFormat !== 'a4' ? 'bg-white shadow text-blue-600 font-medium' : 'text-gray-500 hover:text-gray-700'}`}
                            title={t('pos.receiptPrintFormatTitle')}
                        >
                            🧾 80mm
                        </button>
                        <button
                            type="button"
                            onClick={() => updateLocalSettings('printFormat', 'a4')}
                            className={`px-3 py-1 text-sm rounded-md transition-colors ${settings?.printFormat === 'a4' ? 'bg-white shadow text-blue-600 font-medium' : 'text-gray-500 hover:text-gray-700'}`}
                            title={t('pos.a4PrintFormatTitle')}
                        >
                            📄 A4
                        </button>
                    </div>

                    <button
                        type="button"
                        onClick={handleBackToList}
                        className="btn-secondary"
                        disabled={loading}
                    >
                        {t('salesReturns.backToList')}
                    </button>
                </div>
            </div>

            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 lg:h-full lg:overflow-hidden">
                <div className={`lg:col-span-2 flex flex-col gap-6 lg:overflow-y-auto ${isRTL ? 'lg:pl-2' : 'lg:pr-2'}`}>
                    <div className="bg-white p-4 rounded-lg shadow grid grid-cols-1 sm:grid-cols-2 gap-4">
                        <div>
                            <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('salesReturns.customerOptional')}</label>
                            <AsyncEntitySelect
                                value={selectedCustomer}
                                onChange={handleCustomerChange}
                                loadOptions={searchCustomers}
                                placeholder={t('salesHistory.walkInCustomer')}
                                emptyText={t('customers.noCustomers')}
                                disabled={loading || Boolean(originalInvoice)}
                                renderOption={(customerOption) => (
                                    <div>
                                        <div className="font-medium text-gray-900">{customerOption.name}</div>
                                        <div className="text-sm text-gray-500">{customerOption.phone || customerOption.email || t('common.none')}</div>
                                    </div>
                                )}
                            />
                        </div>
                        <div>
                            <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('salesReturns.warehouseRequired')}</label>
                            <select
                                value={warehouseId}
                                onChange={(e) => setWarehouseId(e.target.value)}
                                className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                                disabled={loading || Boolean(originalInvoice)}
                            >
                                {warehouses?.map(w => (
                                    <option key={w.id} value={w.id}>{w.name}</option>
                                ))}
                            </select>
                        </div>
                        <div>
                            <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('salesReturns.returnDate')}</label>
                            <input
                                type="date"
                                value={returnDate}
                                onChange={(e) => setReturnDate(e.target.value)}
                                className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                                disabled={loading}
                            />
                        </div>
                        <div>
                            <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('salesReturns.originalSalesInvoice')}</label>
                            <select
                                value={originalInvoiceId}
                                onChange={(e) => handleInvoiceSelect(e.target.value)}
                                className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                                disabled={loading || invoicesLoading || invoiceLoading}
                            >
                                <option value="">
                                    {invoicesLoading
                                        ? t('common.loading')
                                        : t('salesReturns.selectInvoiceOptional')}
                                </option>
                                {salesInvoices?.map(inv => (
                                    <option key={inv.id} value={inv.id}>
                                        {inv.invoiceNumber} - {formatCurrency(inv.totalAmount)}
                                        {inv.customer?.name ? ` - ${inv.customer.name}` : ''}
                                    </option>
                                ))}
                            </select>
                            {invoicesError ? (
                                <div className="mt-2 flex items-center justify-between gap-2 text-sm text-red-700">
                                    <span>{invoicesError}</span>
                                    <button
                                        type="button"
                                        onClick={() => fetchSalesInvoices(customerId || null)}
                                        className="font-medium underline"
                                    >
                                        {t('common.retry')}
                                    </button>
                                </div>
                            ) : null}
                            {!invoicesLoading && !invoicesError && salesInvoices.length === 0 ? (
                                <div className="mt-2 text-sm text-gray-500">{t('salesReturns.noEligibleInvoices')}</div>
                            ) : null}
                        </div>
                        <div className="sm:col-span-2">
                            <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('salesReturns.reasonForReturn')}</label>
                            <input
                                type="text"
                                value={reason}
                                onChange={(e) => setReason(e.target.value)}
                                className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                                placeholder={t('salesReturns.reasonPlaceholder')}
                                disabled={loading}
                            />
                        </div>
                        <div className="sm:col-span-2">
                            <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('common.description')}</label>
                            <textarea
                                value={notes}
                                onChange={(e) => setNotes(e.target.value)}
                                className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                                rows="2"
                                placeholder={t('salesReturns.notesPlaceholder')}
                                disabled={loading}
                            />
                        </div>
                    </div>

                    <div className="bg-white rounded-lg shadow flex-1 overflow-hidden flex flex-col">
                        <div className={`border-b border-gray-200 px-4 py-3 text-sm text-gray-600 ${isRTL ? 'text-right' : 'text-left'}`}>
                            {t('salesReturns.lockedFieldsHelp')}
                        </div>
                        <div className="overflow-x-auto">
                            <table className="w-full">
                                <thead className="bg-gray-50 border-b border-gray-200">
                                    <tr>
                                        <th className={`px-4 py-2 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('salesReturns.product')}</th>
                                        <th className={`px-4 py-2 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('units.unit')}</th>
                                        <th className="px-4 py-2 text-center text-xs font-medium text-gray-500 uppercase w-24">{t('salesReturns.sold')}</th>
                                        <th className="px-4 py-2 text-center text-xs font-medium text-gray-500 uppercase w-24">{t('salesReturns.previouslyReturned')}</th>
                                        <th className="px-4 py-2 text-center text-xs font-medium text-gray-500 uppercase w-24">{t('salesReturns.remaining')}</th>
                                        <th className="px-4 py-2 text-center text-xs font-medium text-gray-500 uppercase w-28 min-w-28">{t('salesReturns.returnQty')}</th>
                                        <th className="px-4 py-2 text-center text-xs font-medium text-gray-500 uppercase w-24 min-w-24">{t('salesReturns.conversion')}</th>
                                        <th className="px-4 py-2 text-center text-xs font-medium text-gray-500 uppercase w-36 min-w-36">{t('salesReturns.unitPrice')}</th>
                                        <th className="px-4 py-2 text-center text-xs font-medium text-gray-500 uppercase w-24 min-w-24">{t('salesReturns.taxPercent')}</th>
                                        <th className={`px-4 py-2 text-xs font-medium text-gray-500 uppercase w-32 ${isRTL ? 'text-left' : 'text-right'}`}>{t('common.total')}</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-gray-200">
                                    {items.length === 0 ? (
                                        <tr>
                                            <td colSpan="10" className="px-4 py-8 text-center text-gray-500">
                                                {invoiceLoading
                                                    ? t('common.loading')
                                                    : originalInvoiceId
                                                        ? t('salesReturns.noItemsInInvoice')
                                                        : t('salesReturns.selectInvoiceToLoad')}
                                            </td>
                                        </tr>
                                    ) : (
                                        items.map((item, index) => (
                                            <tr key={`${item.productId}-${item.productVariantId || 'default'}`}>
                                                <td className={`px-4 py-2 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                    <div className="text-sm font-medium">{item.variantDisplayName || item.name}</div>
                                                    {item.variantSku && (
                                                        <div className="text-xs text-gray-500">{t('common.code')}: {item.variantSku}</div>
                                                    )}
                                                </td>
                                                <td className={`px-4 py-2 text-sm text-gray-500 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                    {item.unitName}
                                                </td>
                                                <td className="px-4 py-2 text-center text-sm text-gray-600">
                                                    {item.originalQuantity}
                                                </td>
                                                <td className="px-4 py-2 text-center text-sm text-gray-600">
                                                    {item.previouslyReturnedQuantity}
                                                </td>
                                                <td className="px-4 py-2 text-center text-sm font-medium text-gray-700">
                                                    {item.remainingQuantity}
                                                </td>
                                                <td className="px-4 py-2">
                                                    <input
                                                        type="number"
                                                        value={item.quantity}
                                                        onChange={(e) => updateItem(index, 'quantity', e.target.value)}
                                                        className="input w-full min-w-24 text-center p-1"
                                                        min="0"
                                                        max={item.remainingQuantity}
                                                        step="0.000001"
                                                        disabled={loading}
                                                    />
                                                </td>
                                                <td className="px-4 py-2 min-w-24 text-center text-sm text-gray-600">
                                                    x{item.conversionFactor}
                                                </td>
                                                <td className="px-4 py-2">
                                                    <input
                                                        type="number"
                                                        value={item.originalUnitPrice}
                                                        readOnly
                                                        className="input w-full min-w-32 bg-gray-50 text-center p-1 text-gray-600"
                                                        tabIndex={-1}
                                                    />
                                                </td>
                                                <td className="px-4 py-2">
                                                    <input
                                                        type="number"
                                                        value={item.originalTaxRate}
                                                        readOnly
                                                        className="input w-full min-w-20 bg-gray-50 text-center p-1 text-gray-600"
                                                        tabIndex={-1}
                                                    />
                                                </td>
                                                <td className={`px-4 py-2 font-medium ${isRTL ? 'text-left' : 'text-right'}`}>
                                                    {formatCurrency(returnSummary.lines[index]?.total || 0)}
                                                </td>
                                            </tr>
                                        ))
                                    )}
                                </tbody>
                            </table>
                        </div>
                    </div>
                </div>

                <div className="flex flex-col gap-6">
                    <div className="bg-white p-6 rounded-lg shadow h-full flex flex-col">
                        <h2 className={`text-lg font-bold mb-4 ${isRTL ? 'text-right' : 'text-left'}`}>{t('salesReturns.returnSummary')}</h2>

                        <div className="space-y-3 mb-6 flex-1">
                            <div className={`flex justify-between text-gray-600 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                <span>{t('common.subtotal')}</span>
                                <span>{formatCurrency(subtotal)}</span>
                            </div>
                            <div className={`flex justify-between text-gray-600 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                <span>{t('common.tax')}</span>
                                <span>{formatCurrency(tax)}</span>
                            </div>
                            <div className={`border-t border-gray-200 pt-3 flex justify-between text-xl font-bold text-gray-900 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                <span>{t('salesReturns.totalReturn')}</span>
                                <span>{formatCurrency(total)}</span>
                            </div>
                        </div>

                        <div className="space-y-4 border-t border-gray-200 pt-6">
                            <div className="space-y-4">
                                <label className={`label ${isRTL ? 'text-right' : 'text-left'}`}>{t('salesReturns.refundMethod')}</label>
                                {originalInvoice ? (
                                    <div className={`rounded-lg bg-gray-50 px-3 py-2 text-xs text-gray-600 ${isRTL ? 'text-right' : 'text-left'}`}>
                                        {t('salesReturns.remainingTenderCapacity', {
                                            cash: formatCurrency(remainingCashRefundable),
                                            card: formatCurrency(remainingCardRefundable),
                                        })}
                                    </div>
                                ) : null}
                                <div className="flex flex-col gap-2">
                                    <label className={`flex items-center gap-2 cursor-pointer ${isRTL ? 'flex-row-reverse justify-end' : ''}`}>
                                        <input
                                            type="radio"
                                            name="refundMethod"
                                            value="accountCredit"
                                            checked={refundMethod === 'accountCredit'}
                                            onChange={() => resetRefundSelection('accountCredit')}
                                            className="text-blue-600 focus:ring-blue-500"
                                            disabled={loading || !customerId}
                                        />
                                        <span className="text-sm font-medium">{t('salesReturns.creditToAccount')}</span>
                                    </label>
                                    <label className={`flex items-center gap-2 cursor-pointer ${isRTL ? 'flex-row-reverse justify-end' : ''}`}>
                                        <input
                                            type="radio"
                                            name="refundMethod"
                                            value="cash"
                                            checked={refundMethod === 'cash'}
                                            onChange={() => resetRefundSelection('cash')}
                                            className="text-blue-600 focus:ring-blue-500"
                                            disabled={loading || cashRefundUnavailable}
                                        />
                                        <span className="text-sm font-medium">{t('salesReturns.refundFromCash')}</span>
                                    </label>
                                    <label className={`flex items-center gap-2 cursor-pointer ${isRTL ? 'flex-row-reverse justify-end' : ''}`}>
                                        <input
                                            type="radio"
                                            name="refundMethod"
                                            value="card"
                                            checked={refundMethod === 'card'}
                                            onChange={() => resetRefundSelection('card')}
                                            className="text-blue-600 focus:ring-blue-500"
                                            disabled={loading || cardRefundUnavailable}
                                        />
                                        <span className="text-sm font-medium">{t('salesReturns.refundFromVisa')}</span>
                                    </label>
                                </div>
                            </div>

                            {validationMessage ? (
                                <div className={`rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700 ${isRTL ? 'text-right' : 'text-left'}`}>
                                    {validationMessage}
                                </div>
                            ) : null}

                            <button
                                onClick={handleSubmit}
                                disabled={loading || Boolean(validationMessage)}
                                className="btn-primary w-full py-3 text-lg disabled:opacity-50 disabled:cursor-not-allowed"
                            >
                                {loading ? t('salesReturns.processing') : t('salesReturns.createReturn')}
                            </button>
                        </div>
                    </div>
                </div>
            </div>

            {showReceiptModal && receiptData && (
                <ReceiptModal
                    receiptData={receiptData}
                    onClose={() => {
                        setShowReceiptModal(false)
                        setView('list')
                        resetForm()
                    }}
                />
            )}
        </div>
    )
}

import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import { useToast } from './Toast'
import { buildIdempotencyHeaders, stableStringify } from '../utils/requestSafety'

const buildRequestId = (prefix) => `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
const roundCurrency = (value) => Math.round((Number(value) || 0) * 100) / 100
const prorateAmount = (amount, partialBasis, totalBasis) => {
    if (roundCurrency(amount) === 0 || Number(partialBasis) <= 0 || Number(totalBasis) <= 0) {
        return 0
    }

    return roundCurrency(roundCurrency(amount) * Math.min(Number(partialBasis), Number(totalBasis)) / Number(totalBasis))
}

export default function ReturnModal({ sale, onClose, onSuccess }) {
    const { t } = useTranslation()
    const { confirmAction } = useConfirmDialog()
    const [returnItems, setReturnItems] = useState({}) // { [itemId]: quantity }
    const [reason, setReason] = useState('')
    const [refundMethod, setRefundMethod] = useState('cash')
    const [cashRefundAmount, setCashRefundAmount] = useState(0)
    const [cardRefundAmount, setCardRefundAmount] = useState(0)
    const [isProcessing, setIsProcessing] = useState(false)
    const submissionLockRef = useRef(false)
    const lastSubmissionRef = useRef(null)
    const { showSuccess, showError } = useToast()

    // Initialize return items with 0
    const handleQuantityChange = (itemId, quantity, maxQuantity) => {
        const qty = Math.max(0, Math.min(quantity, maxQuantity))
        setReturnItems(prev => ({
            ...prev,
            [itemId]: qty
        }))
    }

    const calculateRefundTotal = () => {
        let grossSubtotal = 0
        let lineDiscount = 0
        let lineTax = 0

        sale.items.forEach(item => {
            const qty = returnItems[item.id] || 0
            if (qty > 0) {
                const quantityRatio = item.quantity > 0 ? Math.min(1, qty / item.quantity) : 0
                grossSubtotal += roundCurrency(qty * (Number(item.unitPrice) || 0))
                lineDiscount += roundCurrency((Number(item.discountAmount) || 0) * quantityRatio)
                lineTax += roundCurrency((Number(item.taxAmount) || 0) * quantityRatio)
            }
        })

        const invoiceDiscount = prorateAmount(
            roundCurrency(sale.discountAmount),
            roundCurrency(grossSubtotal),
            roundCurrency(sale.subtotal),
        )

        return roundCurrency(grossSubtotal - lineDiscount - invoiceDiscount + lineTax)
    }

    const refundTotal = calculateRefundTotal()
    const immediateRefundTotal = roundCurrency(cashRefundAmount + cardRefundAmount)

    // Ensure accountCredit is not selected for walk-in sales without customerId
    useEffect(() => {
        if (!sale.customerId && refundMethod === 'accountCredit') {
            resetRefundSelection('cash')
        }
    }, [sale.customerId, refundMethod])

    useEffect(() => {
        if (refundMethod === 'accountCredit') {
            if (cashRefundAmount !== 0) {
                setCashRefundAmount(0)
            }
            if (cardRefundAmount !== 0) {
                setCardRefundAmount(0)
            }
            return
        }

        if (refundMethod === 'cash') {
            if (refundTotal > 0 && cashRefundAmount === 0 && cardRefundAmount === 0) {
                setCashRefundAmount(refundTotal)
            } else if (cashRefundAmount > refundTotal) {
                setCashRefundAmount(refundTotal)
            }

            if (cardRefundAmount !== 0) {
                setCardRefundAmount(0)
            }
            return
        }

        if (refundMethod === 'card') {
            if (refundTotal > 0 && cashRefundAmount === 0 && cardRefundAmount === 0) {
                setCardRefundAmount(refundTotal)
            } else if (cardRefundAmount > refundTotal) {
                setCardRefundAmount(refundTotal)
            }

            if (cashRefundAmount !== 0) {
                setCashRefundAmount(0)
            }
            return
        }

        if (cashRefundAmount > refundTotal) {
            setCashRefundAmount(refundTotal)
            return
        }

        if (cashRefundAmount + cardRefundAmount > refundTotal) {
            setCardRefundAmount(currentCardAmount => Math.max(
                0,
                Math.min(roundCurrency(currentCardAmount), roundCurrency(refundTotal - cashRefundAmount)),
            ))
        }
    }, [refundMethod, refundTotal, cashRefundAmount, cardRefundAmount])

    const resetRefundSelection = (nextRefundMethod) => {
        setRefundMethod(nextRefundMethod)

        if (nextRefundMethod === 'accountCredit') {
            setCashRefundAmount(0)
            setCardRefundAmount(0)
            return
        }

        if (nextRefundMethod === 'cash') {
            setCashRefundAmount(refundTotal)
            setCardRefundAmount(0)
            return
        }

        if (nextRefundMethod === 'card') {
            setCashRefundAmount(0)
            setCardRefundAmount(refundTotal)
            return
        }

        const seededCashAmount = roundCurrency(refundTotal / 2)
        const seededCardAmount = roundCurrency(refundTotal - seededCashAmount)
        setCashRefundAmount(seededCashAmount)
        setCardRefundAmount(seededCardAmount)
    }

    const clampRefundAmount = (value) => Math.max(0, Math.min(roundCurrency(value), refundTotal))

    const handleMixedRefundChange = (field, value) => {
        const nextAmount = clampRefundAmount(value)

        if (field === 'cash') {
            setCashRefundAmount(nextAmount)
            setCardRefundAmount(currentCardAmount => Math.max(
                0,
                Math.min(roundCurrency(currentCardAmount), roundCurrency(refundTotal - nextAmount)),
            ))
            return
        }

        setCardRefundAmount(nextAmount)
        setCashRefundAmount(currentCashAmount => Math.max(
            0,
            Math.min(roundCurrency(currentCashAmount), roundCurrency(refundTotal - nextAmount)),
        ))
    }

    const handleSubmit = async (e) => {
        e.preventDefault()

        if (submissionLockRef.current) {
            return
        }

        const itemsToReturn = sale.items
            .filter(item => returnItems[item.id] > 0)
            .map(item => ({
                productId: item.productId,
                productVariantId: item.productVariantId,
                unitId: item.unitId || null,
                quantity: returnItems[item.id],
                unitPrice: item.unitPrice,
                taxRate: item.taxRate,
                reason: reason || null,
            }))

        if (itemsToReturn.length === 0) {
            showError('Please select at least one item to return')
            return
        }

        const warehouseId = sale.warehouseId || sale.warehouse?.id
        if (!warehouseId) {
            showError('This sale is missing warehouse data and cannot be refunded safely')
            return
        }

        if (refundMethod === 'cash' && roundCurrency(cashRefundAmount) <= 0) {
            showError(t('salesReturns.refundAmountRequired'))
            return
        }

        if (refundMethod === 'card' && roundCurrency(cardRefundAmount) <= 0) {
            showError(t('salesReturns.refundAmountRequired'))
            return
        }

        if (refundMethod === 'mixed') {
            if (roundCurrency(cashRefundAmount) <= 0 || roundCurrency(cardRefundAmount) <= 0) {
                showError(t('salesReturns.mixedRefundAmountsRequired'))
                return
            }

            if (roundCurrency(immediateRefundTotal) > refundTotal) {
                showError(t('salesReturns.refundAmountExceedsTotal'))
                return
            }
        }

        const payload = {
            customerId: sale.customerId || null,
            warehouseId,
            returnDate: new Date().toISOString().split('T')[0],
            originalInvoiceId: sale.id,
            items: itemsToReturn,
            reason: reason || null,
            notes: null,
            refundMethod: refundMethod === 'accountCredit'
                ? 'AccountCredit'
                : refundMethod === 'cash'
                    ? 'Cash'
                    : refundMethod === 'card'
                        ? 'Card'
                        : 'Mixed',
            cashAmount: refundMethod === 'cash' || refundMethod === 'mixed'
                ? clampRefundAmount(cashRefundAmount)
                : 0,
            cardAmount: refundMethod === 'card' || refundMethod === 'mixed'
                ? clampRefundAmount(cardRefundAmount)
                : 0,
        }

        await confirmAction({
            confirmLabel: t('common.confirm'),
            description: `Process this return for $${refundTotal.toFixed(2)}?`,
            onConfirm: async () => {
                const fingerprint = stableStringify(payload)
                const idempotencyKey = lastSubmissionRef.current?.fingerprint === fingerprint
                    ? lastSubmissionRef.current.key
                    : buildRequestId('sales-return-legacy')
                const paymentAttemptId = buildRequestId('sales-return-legacy-attempt')

                lastSubmissionRef.current = { key: idempotencyKey, fingerprint }
                submissionLockRef.current = true
                setIsProcessing(true)
                try {
                    const response = await api.post('/salesreturns', payload, {
                        headers: buildIdempotencyHeaders(idempotencyKey, paymentAttemptId),
                    })
                    showSuccess('Return processed successfully')
                    onSuccess(response.data)
                } catch (error) {
                    console.error('Return error:', error)
                    lastSubmissionRef.current = null
                    const message = error.response?.data?.message || 'Failed to process return'
                    showError(message)
                    throw new Error(message)
                } finally {
                    submissionLockRef.current = false
                    setIsProcessing(false)
                }
            },
            title: t('common.confirm'),
            variant: 'warning',
        })
    }

    const hasItemsSelected = Object.values(returnItems).some(qty => qty > 0)

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-[60]">
            <div className="bg-white rounded-lg shadow-xl max-w-lg w-full mx-4 flex flex-col max-h-[90vh]">
                <div className="p-6 border-b border-gray-200">
                    <h2 className="text-xl font-bold text-gray-900">Process Return</h2>
                    <p className="text-sm text-gray-500">Select items to return from Invoice #{sale.invoiceNumber}</p>
                </div>

                <div className="p-6 overflow-y-auto flex-1">
                    <form id="return-form" onSubmit={handleSubmit}>
                        <div className="space-y-4">
                            {sale.items.map((item) => {
                                // Build variant display name
                                let variantInfo = '';
                                if (item.productVariant) {
                                    const attrs = item.productVariant?.attributes;
                                    if (attrs && typeof attrs === 'object' && Object.keys(attrs).length > 0) {
                                        variantInfo = Object.values(attrs).join(' / ');
                                    } else if (item.productVariant?.sku) {
                                        variantInfo = item.productVariant.sku;
                                    }
                                }

                                return (
                                    <div key={item.id} className="flex items-center justify-between p-3 bg-gray-50 rounded-lg">
                                        <div className="flex-1">
                                            <div className="font-medium text-gray-900">{item.product?.name}</div>
                                            {variantInfo && (
                                                <div className="text-xs text-blue-600">{variantInfo}</div>
                                            )}
                                            {item.productVariant?.sku && (
                                                <div className="text-xs text-gray-500">SKU: {item.productVariant.sku}</div>
                                            )}
                                            <div className="text-sm text-gray-500">
                                                Sold: {item.quantity} × ${item.unitPrice.toFixed(2)}
                                            </div>
                                        </div>
                                        <div className="flex items-center gap-3">
                                            <label className="text-sm text-gray-600">Return Qty:</label>
                                            <input
                                                type="number"
                                                min="0"
                                                max={item.quantity}
                                                value={returnItems[item.id] || 0}
                                                onChange={(e) => handleQuantityChange(item.id, parseInt(e.target.value) || 0, item.quantity)}
                                                className="w-20 input text-center"
                                            />
                                        </div>
                                    </div>
                                )
                            })}

                            <div>
                                <label className="label">Reason for Return</label>
                                <select
                                    value={reason}
                                    onChange={(e) => setReason(e.target.value)}
                                    className="input w-full"
                                    required
                                >
                                    <option value="">Select a reason...</option>
                                    <option value="Defective">Defective / Damaged</option>
                                    <option value="Wrong Item">Wrong Item</option>
                                    <option value="Customer Changed Mind">Customer Changed Mind</option>
                                    <option value="Other">Other</option>
                                </select>
                            </div>

                            <div>
                                <label className="label">{t('salesReturns.refundMethod')}</label>
                                <select
                                    value={refundMethod}
                                    onChange={(e) => resetRefundSelection(e.target.value)}
                                    className="input w-full"
                                    disabled={isProcessing}
                                >
                                    {sale.customerId && (
                                        <option value="accountCredit">{t('salesReturns.creditToAccount')}</option>
                                    )}
                                    <option value="cash">{t('salesReturns.refundFromCash')}</option>
                                    <option value="card">{t('salesReturns.refundFromVisa')}</option>
                                    <option value="mixed">{t('salesReturns.refundMixed')}</option>
                                </select>
                            </div>

                            {(refundMethod === 'cash' || refundMethod === 'card') && (
                                <div>
                                    <label className="label">{t('salesReturns.refundAmount')}</label>
                                    <input
                                        type="number"
                                        min="0"
                                        max={refundTotal}
                                        step="0.01"
                                        value={refundMethod === 'cash' ? cashRefundAmount : cardRefundAmount}
                                        onChange={(e) => {
                                            const nextAmount = clampRefundAmount(e.target.value)
                                            if (refundMethod === 'cash') {
                                                setCashRefundAmount(nextAmount)
                                                setCardRefundAmount(0)
                                                return
                                            }

                                            setCashRefundAmount(0)
                                            setCardRefundAmount(nextAmount)
                                        }}
                                        className="input w-full"
                                        disabled={isProcessing}
                                    />
                                </div>
                            )}

                            {refundMethod === 'mixed' && (
                                <div className="space-y-4">
                                    <div>
                                        <label className="label">{t('salesReturns.cashRefundAmount')}</label>
                                        <input
                                            type="number"
                                            min="0"
                                            max={refundTotal}
                                            step="0.01"
                                            value={cashRefundAmount}
                                            onChange={(e) => handleMixedRefundChange('cash', e.target.value)}
                                            className="input w-full"
                                            disabled={isProcessing}
                                        />
                                    </div>
                                    <div>
                                        <label className="label">{t('salesReturns.cardRefundAmount')}</label>
                                        <input
                                            type="number"
                                            min="0"
                                            max={refundTotal}
                                            step="0.01"
                                            value={cardRefundAmount}
                                            onChange={(e) => handleMixedRefundChange('card', e.target.value)}
                                            className="input w-full"
                                            disabled={isProcessing}
                                        />
                                    </div>
                                    <div className="text-sm text-gray-500">
                                        {t('salesReturns.refundAmount')}: ${immediateRefundTotal.toFixed(2)}
                                    </div>
                                </div>
                            )}
                        </div>
                    </form>
                </div>

                <div className="p-6 border-t border-gray-200 bg-gray-50 rounded-b-lg">
                    <div className="flex justify-between items-center mb-4">
                        <span className="font-medium text-gray-700">Refund Total:</span>
                        <span className="text-2xl font-bold text-red-600">${refundTotal.toFixed(2)}</span>
                    </div>

                    <div className="flex gap-3">
                        <button
                            type="button"
                            onClick={onClose}
                            disabled={isProcessing}
                            className="flex-1 btn-secondary"
                        >
                            Cancel
                        </button>
                        <button
                            type="submit"
                            form="return-form"
                            disabled={isProcessing || !hasItemsSelected}
                            className="flex-1 btn-primary bg-red-600 hover:bg-red-700 disabled:bg-red-300"
                        >
                            {isProcessing ? 'Processing...' : 'Confirm Return'}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    )
}

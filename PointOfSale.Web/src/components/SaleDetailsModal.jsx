import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import ReceiptModal from './ReceiptModal'
import ReturnModal from './ReturnModal'
import api from '../services/api'

export default function SaleDetailsModal({ sale, onClose, onReturnProcessed }) {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()
    const [showReceipt, setShowReceipt] = useState(false)
    const [showReturn, setShowReturn] = useState(false)
    const [receiptData, setReceiptData] = useState(null)

    const handleReprintReceipt = async () => {
        try {
            const response = await api.get(`/sales/receipt/${sale.id}`)
            setReceiptData({ receipt: response.data })
            setShowReceipt(true)
        } catch (error) {
            console.error('Error fetching receipt:', error)
            alert(t('saleDetailsModal.failedToLoadReceipt'))
        }
    }

    const handleReturnSuccess = (returnResponse) => {
        setShowReturn(false)
        onReturnProcessed(returnResponse)
        onClose()
    }

    // Calculate totals if not present (for safety)
    const subtotal = sale.subtotal || 0
    const tax = sale.taxAmount || 0
    const discount = sale.discountAmount || 0
    const total = sale.totalAmount || 0

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
            <div className="bg-white rounded-lg shadow-xl max-w-2xl w-full mx-4 max-h-[90vh] flex flex-col">
                {/* Header */}
                <div className="p-6 border-b border-gray-200 flex justify-between items-center">
                    <div>
                        <h2 className="text-xl font-bold text-gray-900">{t('saleDetailsModal.title')}</h2>
                        <p className="text-sm text-gray-500">{sale.invoiceNumber}</p>
                    </div>
                    <button onClick={onClose} className="text-gray-400 hover:text-gray-600 text-2xl">×</button>
                </div>

                {/* Content */}
                <div className="p-6 overflow-y-auto flex-1">
                    {/* Info Grid */}
                    <div className="grid grid-cols-2 gap-6 mb-6">
                        <div>
                            <h3 className="text-sm font-medium text-gray-500 mb-1">{t('pos.customer')}</h3>
                            <p className="text-gray-900 font-medium">{sale.customer?.name || t('salesHistory.walkInCustomer')}</p>
                            {sale.customer?.phone && <p className="text-sm text-gray-500">{sale.customer.phone}</p>}
                        </div>
                        <div>
                            <h3 className="text-sm font-medium text-gray-500 mb-1">{t('saleDetailsModal.dateTime')}</h3>
                            <p className="text-gray-900">{new Date(sale.invoiceDate).toLocaleString()}</p>
                        </div>
                        <div>
                            <h3 className="text-sm font-medium text-gray-500 mb-1">{t('paymentModal.paymentMethod')}</h3>
                            <p className="text-gray-900">{sale.paymentMethod}</p>
                        </div>
                        <div>
                            <h3 className="text-sm font-medium text-gray-500 mb-1">{t('common.status')}</h3>
                            <span className={`px-2 inline-flex text-xs leading-5 font-semibold rounded-full ${sale.totalAmount < 0 ? 'bg-red-100 text-red-800' : 'bg-green-100 text-green-800'
                                }`}>
                                {sale.totalAmount < 0 ? t('salesHistory.return') : t('salesHistory.completed')}
                            </span>
                        </div>
                    </div>

                    {/* Items Table */}
                    <div className="border rounded-lg overflow-hidden mb-6">
                        <table className="w-full text-sm">
                            <thead className="bg-gray-50 border-b border-gray-200">
                                <tr>
                                    <th className="px-4 py-2 text-left font-medium text-gray-500">{t('saleDetailsModal.item')}</th>
                                    <th className="px-4 py-2 text-right font-medium text-gray-500">{t('saleDetailsModal.qty')}</th>
                                    <th className="px-4 py-2 text-right font-medium text-gray-500">{t('common.price')}</th>
                                    <th className="px-4 py-2 text-right font-medium text-gray-500">{t('common.total')}</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-200">
                                {sale.items?.map((item) => (
                                    <tr key={item.id}>
                                        <td className="px-4 py-3">
                                            <div className="font-medium text-gray-900">{item.product?.name || t('saleDetailsModal.unknownProduct')}</div>
                                            {item.productVariant && <div className="text-xs text-gray-500">{item.productVariant.sku}</div>}
                                        </td>
                                        <td className="px-4 py-3 text-right">{item.quantity}</td>
                                        <td className="px-4 py-3 text-right">{formatCurrency(item.unitPrice)}</td>
                                        <td className="px-4 py-3 text-right font-medium">{formatCurrency(item.totalAmount)}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>

                    {/* Totals */}
                    <div className="flex justify-end">
                        <div className="w-64 space-y-2">
                            <div className="flex justify-between text-sm text-gray-600">
                                <span>{t('common.subtotal')}:</span>
                                <span>{formatCurrency(subtotal)}</span>
                            </div>
                            {discount > 0 && (
                                <div className="flex justify-between text-sm text-orange-600">
                                    <span>{t('common.discount')}:</span>
                                    <span>-{formatCurrency(discount)}</span>
                                </div>
                            )}
                            <div className="flex justify-between text-sm text-gray-600">
                                <span>{t('common.tax')}:</span>
                                <span>{formatCurrency(tax)}</span>
                            </div>
                            <div className="flex justify-between text-lg font-bold text-gray-900 pt-2 border-t">
                                <span>{t('common.total')}:</span>
                                <span>{formatCurrency(total)}</span>
                            </div>
                        </div>
                    </div>
                </div>

                {/* Footer Actions */}
                <div className="p-6 border-t border-gray-200 bg-gray-50 rounded-b-lg flex justify-between">
                    <button
                        onClick={handleReprintReceipt}
                        className="btn-secondary flex items-center gap-2"
                    >
                        {t('saleDetailsModal.reprintReceipt')}
                    </button>

                    {sale.totalAmount > 0 && (
                        <button
                            onClick={() => setShowReturn(true)}
                            className="btn-danger flex items-center gap-2"
                        >
                            {t('saleDetailsModal.processReturn')}
                        </button>
                    )}
                </div>
            </div>

            {/* Nested Modals */}
            {showReceipt && receiptData && (
                <ReceiptModal
                    receiptData={receiptData}
                    onClose={() => setShowReceipt(false)}
                />
            )}

            {showReturn && (
                <ReturnModal
                    sale={sale}
                    onClose={() => setShowReturn(false)}
                    onSuccess={handleReturnSuccess}
                />
            )}
        </div>
    )
}

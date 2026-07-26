import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'

export default function ReceiptModal({ receiptData, onClose }) {
    const { t } = useTranslation()
    const receipt = receiptData.receipt
    const { settings, formatCurrency } = useSettings()
    const [printStatus, setPrintStatus] = useState('pending')
    const [printing, setPrinting] = useState(false)

    const handlePrint = useCallback(async () => {
        // Guard against concurrent print attempts
        if (printing) {
            return
        }

        if (typeof window === 'undefined' || typeof window.print !== 'function') {
            setPrintStatus('error')
            return
        }

        try {
            setPrinting(true)
            setPrintStatus('printing')
            const result = window.print()
            // Handle both synchronous and asynchronous window.print implementations
            if (result && typeof result.then === 'function') {
                await result
            }
            setPrintStatus('printed')
        } catch (error) {
            console.error('Receipt print failed:', error)
            setPrintStatus('error')
        } finally {
            setPrinting(false)
        }
    }, [printing])

    // Auto-print when modal opens
    useEffect(() => {
        const timer = setTimeout(() => {
            handlePrint()
        }, 500)
        return () => clearTimeout(timer)
    }, [handlePrint])

    const isA4 = settings?.printFormat === 'a4'

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
            <div className={`bg-white rounded-lg shadow-xl w-full mx-4 max-h-[90vh] overflow-y-auto ${isA4 ? 'max-w-4xl' : 'max-w-md'}`}>
                <div className="p-6">
                    <div className="flex justify-between items-center mb-6">
                        <h3 className="text-2xl font-bold text-green-600">{t('receipt.saleComplete')}</h3>
                        <button
                            onClick={onClose}
                            className="text-gray-400 hover:text-gray-600 text-2xl"
                        >
                            ×
                        </button>
                    </div>

                    {/* Receipt Content */}
                    <div id="receipt-content" className={`border-2 border-gray-300 rounded-lg mb-6 print:border-0 ${isA4 ? 'p-10' : 'p-6'}`}>
                        {/* Header */}
                        <div className="text-center mb-6 border-b-2 border-dashed border-gray-300 pb-4">
                            {settings?.showLogoOnReceipt && settings?.companyLogo && (
                                <div className="mb-2 flex justify-center">
                                    <img src={settings.companyLogo} className={`object-contain ${isA4 ? 'h-24' : 'h-16'} w-auto`} alt="Logo" />
                                </div>
                            )}
                            <h2 className={`font-bold ${isA4 ? 'text-3xl' : 'text-2xl'}`}>{settings?.companyName || receipt.warehouseName || 'POS System'}</h2>
                            {settings?.companyAddress && (
                                <p className="text-sm text-gray-600 mt-1">{settings.companyAddress}</p>
                            )}
                            {settings?.companyPhone && (
                                <p className="text-sm text-gray-600">{settings.companyPhone}</p>
                            )}
                            {settings?.receiptHeader && (
                                <p className="text-sm text-gray-500 mt-2 font-medium">{settings.receiptHeader}</p>
                            )}
                        </div>

                        {/* Invoice Info */}
                        <div className="mb-4 text-sm">
                            <div className="flex justify-between mb-1">
                                <span className="font-semibold">{t('receipt.invoice')}:</span>
                                <span>{receipt.invoiceNumber}</span>
                            </div>
                            <div className="flex justify-between mb-1">
                                <span className="font-semibold">{t('receipt.date')}:</span>
                                <span>{new Date(receipt.invoiceDate).toLocaleString()}</span>
                            </div>
                            {receipt.customerName && (
                                <div className="flex justify-between">
                                    <span className="font-semibold">{t('receipt.customer')}:</span>
                                    <span>{receipt.customerName}</span>
                                </div>
                            )}
                        </div>

                        {/* Items */}
                        <div className="border-t-2 border-b-2 border-dashed border-gray-300 py-3 mb-3">
                            <table className="w-full text-sm">
                                <thead>
                                    <tr className="border-b border-gray-300">
                                        <th className="text-left pb-2">{t('receipt.item')}</th>
                                        <th className="text-right pb-2">{t('receipt.qty')}</th>
                                        <th className="text-right pb-2">{t('receipt.price')}</th>
                                        <th className="text-right pb-2">{t('receipt.total')}</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {receipt.items.map((item, index) => (
                                        <tr key={index} className="border-b border-gray-100">
                                            <td className="py-2">{item.productName}</td>
                                            <td className="text-right">{item.quantity}</td>
                                            <td className="text-right">{formatCurrency(item.unitPrice)}</td>
                                            <td className="text-right font-semibold">{formatCurrency(item.total)}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>

                        {/* Totals */}
                        <div className="space-y-1 text-sm mb-4">
                            <div className="flex justify-between">
                                <span>{t('receipt.subtotal')}:</span>
                                <span>{formatCurrency(receipt.subtotal)}</span>
                            </div>
                            {receipt.discount > 0 && (
                                <div className="flex justify-between text-orange-600">
                                    <span>{t('receipt.discount')}:</span>
                                    <span>-{formatCurrency(receipt.discount)}</span>
                                </div>
                            )}
                            <div className="flex justify-between">
                                <span>{t('receipt.tax')}:</span>
                                <span>{formatCurrency(receipt.tax)}</span>
                            </div>
                            <div className="flex justify-between text-lg font-bold pt-2 border-t-2 border-gray-300">
                                <span>{t('receipt.total')}:</span>
                                <span>{formatCurrency(receipt.total)}</span>
                            </div>
                        </div>

                        {/* Payment Info */}
                        <div className="border-t-2 border-dashed border-gray-300 pt-3 mb-4 text-sm">
                            <div className="flex justify-between mb-1">
                                <span className="font-semibold">{t('receipt.paymentMethod')}:</span>
                                <span>{receipt.paymentMethod}</span>
                            </div>
                            <div className="flex justify-between mb-1">
                                <span className="font-semibold">{t('receipt.paid')}:</span>
                                <span>{formatCurrency(receipt.paid)}</span>
                            </div>
                            {receipt.change > 0 && (
                                <div className="flex justify-between text-green-600 font-semibold">
                                    <span>{t('receipt.change')}:</span>
                                    <span>{formatCurrency(receipt.change)}</span>
                                </div>
                            )}
                        </div>

                        {/* Footer */}
                        <div className="text-center text-sm text-gray-600 border-t-2 border-dashed border-gray-300 pt-4">
                            <p className="font-semibold">{settings?.receiptFooter || t('receipt.thankYou')}</p>
                            <p className="text-xs mt-2">{t('receipt.keepReceipt')}</p>
                        </div>
                    </div>

                    {/* Action Buttons */}
                    {printStatus === 'error' ? (
                        <p
                            role="alert"
                            className="mb-3 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800 print:hidden"
                        >
                            {t('receipt.printError', 'Could not open the print dialog. Use the print button to try again.')}
                        </p>
                    ) : null}

                    <button
                        type="button"
                        onClick={handlePrint}
                        disabled={printing}
                        className="w-full btn-primary print:hidden disabled:cursor-not-allowed disabled:opacity-50"
                    >
                        {printStatus === 'printing'
                            ? t('receipt.printing', 'Printing...')
                            : t('receipt.print', 'Print receipt')}
                    </button>
                    <button
                        type="button"
                        onClick={onClose}
                        className="w-full mt-3 btn-secondary print:hidden"
                    >
                        {t('receipt.close')}
                    </button>
                </div>
            </div>

            {/* Print Styles */}
            <style>{`
        @media print {
          body * {
            visibility: hidden;
          }
          #receipt-content,
          #receipt-content * {
            visibility: visible;
          }
          #receipt-content {
            position: absolute;
            left: 0;
            top: 0;
            width: ${isA4 ? '100%' : '80mm'};
            font-size: ${isA4 ? '14px' : '12px'};
            padding: ${isA4 ? '10mm' : '0'};
          }
        }
      `}</style>
        </div>
    )
}

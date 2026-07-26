import { useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useReactToPrint } from 'react-to-print'
import { useSettings } from '../contexts/SettingsContext'

export default function PurchasePrintModal({ data, onClose }) {
    const { t } = useTranslation()
    const { formatCurrency, settings } = useSettings()
    const componentRef = useRef()

    const handlePrint = useReactToPrint({
        content: () => componentRef.current,
        documentTitle: `Purchase_${data.invoiceNumber}`,
        onAfterPrint: onClose
    })

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
            <div className="bg-white rounded-lg shadow-xl w-full max-w-4xl h-[90vh] flex flex-col">
                <div className="p-4 border-b border-gray-200 flex justify-between items-center bg-gray-50">
                    <h2 className="text-xl font-bold">{t('common.print')} - {data.invoiceNumber}</h2>
                    <div className="flex gap-3">
                        <button
                            onClick={handlePrint}
                            className="bg-blue-600 text-white px-4 py-2 rounded hover:bg-blue-700 font-medium flex items-center gap-2"
                        >
                            <span>🖨️</span> {t('common.print')}
                        </button>
                        <button
                            onClick={onClose}
                            className="bg-gray-200 text-gray-800 px-4 py-2 rounded hover:bg-gray-300 font-medium"
                        >
                            {t('common.close')}
                        </button>
                    </div>
                </div>

                <div className="flex-1 overflow-y-auto bg-gray-100 p-8">
                    <div ref={componentRef} className="bg-white mx-auto shadow-sm p-8 max-w-[210mm] min-h-[297mm]">
                        {/* Header */}
                        <div className="flex justify-between items-start mb-8 border-b pb-8">
                            <div>
                                {settings?.companyLogo && (
                                    <img
                                        src={settings.companyLogo}
                                        alt="Logo"
                                        className="h-20 object-contain mb-4"
                                    />
                                )}
                                <h1 className="text-2xl font-bold text-gray-900">{settings?.companyName || t('app.name')}</h1>
                                <div className="text-gray-600 text-sm mt-2 space-y-1">
                                    <p>{settings?.companyAddress}</p>
                                    <p>{settings?.companyPhone}</p>
                                    <p>{settings?.companyEmail}</p>
                                </div>
                            </div>
                            <div className="text-right">
                                <h2 className="text-2xl font-bold text-gray-800 mb-2">{t('purchases.invoice')}</h2>
                                <div className="space-y-1 text-sm">
                                    <p><span className="font-medium text-gray-600">{t('common.invoiceNumber')}:</span> {data.invoiceNumber}</p>
                                    <p><span className="font-medium text-gray-600">{t('common.date')}:</span> {new Date(data.date || data.invoiceDate).toLocaleDateString()}</p>
                                    <p><span className="font-medium text-gray-600">{t('purchases.referenceNotes')}:</span> {data.notes || '-'}</p>
                                </div>
                            </div>
                        </div>

                        {/* Supplier Info */}
                        <div className="mb-8 p-4 bg-gray-50 rounded border border-gray-100">
                            <h3 className="text-sm font-bold text-gray-500 uppercase tracking-wider mb-2">{t('purchases.supplier')}</h3>
                            <p className="font-bold text-lg text-gray-900">{data.supplier?.name}</p>
                            <p className="text-gray-600">{data.supplier?.contactPerson}</p>
                            <p className="text-gray-600">{data.supplier?.phone}</p>
                        </div>

                        {/* Items Table */}
                        <table className="w-full mb-8">
                            <thead>
                                <tr className="border-b-2 border-gray-800">
                                    <th className="py-2 text-left font-bold text-gray-900">{t('purchases.product')}</th>
                                    <th className="py-2 text-center font-bold text-gray-900">{t('purchases.qty')}</th>
                                    <th className="py-2 text-right font-bold text-gray-900">{t('purchases.cost')}</th>
                                    <th className="py-2 text-right font-bold text-gray-900">{t('purchases.taxPercent')}</th>
                                    <th className="py-2 text-right font-bold text-gray-900">{t('purchases.total')}</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-200">
                                {data.items.map((item, index) => (
                                    <tr key={index}>
                                        <td className="py-3 text-sm text-gray-900">{item.product?.name || item.productName}</td>
                                        <td className="py-3 text-sm text-center text-gray-900">{item.quantity}</td>
                                        <td className="py-3 text-sm text-right text-gray-900">{formatCurrency(item.unitPrice)}</td>
                                        <td className="py-3 text-sm text-right text-gray-900">{item.taxRate}%</td>
                                        <td className="py-3 text-sm text-right font-medium text-gray-900">{formatCurrency(item.totalAmount || (item.quantity * item.unitPrice * (1 + item.taxRate / 100)))}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>

                        {/* Footer / Totals */}
                        <div className="flex justify-end">
                            <div className="w-64 space-y-2">
                                <div className="flex justify-between text-sm text-gray-600">
                                    <span>{t('purchases.subtotal')}</span>
                                    <span>{formatCurrency(data.subtotal || data.items.reduce((sum, i) => sum + (i.quantity * i.unitPrice), 0))}</span>
                                </div>
                                <div className="flex justify-between text-sm text-gray-600">
                                    <span>{t('purchases.tax')}</span>
                                    <span>{formatCurrency(data.taxAmount || data.items.reduce((sum, i) => sum + (i.quantity * i.unitPrice * i.taxRate / 100), 0))}</span>
                                </div>
                                <div className="flex justify-between text-xl font-bold text-gray-900 pt-2 border-t-2 border-gray-900">
                                    <span>{t('purchases.total')}</span>
                                    <span>{formatCurrency(data.totalAmount)}</span>
                                </div>
                                <div className="pt-4 mt-4 border-t border-gray-200">
                                    <div className="flex justify-between text-sm text-gray-600">
                                        <span>{t('purchases.amountPaid')}</span>
                                        <span>{formatCurrency(data.paidAmount)}</span>
                                    </div>
                                    <div className="flex justify-between text-sm text-gray-600">
                                        <span>{t('purchases.paymentMethod')}</span>
                                        <span>{t(`purchases.${data.paymentMethod?.toLowerCase()}`) || data.paymentMethod}</span>
                                    </div>
                                </div>
                            </div>
                        </div>

                        {/* Notes */}
                        {data.notes && (
                            <div className="mt-8 pt-4 border-t border-gray-200">
                                <p className="text-sm text-gray-500 italic">{data.notes}</p>
                            </div>
                        )}
                    </div>
                </div>
            </div>
        </div>
    )
}

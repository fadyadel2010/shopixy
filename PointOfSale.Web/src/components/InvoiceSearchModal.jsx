import { useState, useEffect, useRef, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'
import { useSettings } from '../contexts/SettingsContext'
import { useToast } from './Toast'
import ValidationDeleteModal from './ValidationDeleteModal'
import { useRTL } from '../hooks/useRTL'
import { formatDate } from '../utils/dateFormat'
import { useDebounce } from '../hooks/useDebounce'
import { canRewriteInvoice } from './invoiceAuthoring'

export default function InvoiceSearchModal({ type, onClose, onSelect, onPrint }) {
    const { t } = useTranslation()
    const { isRTL, dir, locale } = useRTL()
    const { formatCurrency } = useSettings()
    const { showError, showSuccess } = useToast()

    const [searchTerm, setSearchTerm] = useState('')
    const [invoices, setInvoices] = useState([])
    const [loading, setLoading] = useState(false)
    const [page, setPage] = useState(1)
    const [totalPages, setTotalPages] = useState(1)
    const [deleteModal, setDeleteModal] = useState(null) // { id, invoiceNumber }
    const requestIdRef = useRef(0)
    const debouncedSearchTerm = useDebounce(searchTerm, 300)
    const isSearchSettled = debouncedSearchTerm === searchTerm

    useEffect(() => {
        return () => {
            requestIdRef.current += 1
        }
    }, [])

    useEffect(() => {
        requestIdRef.current += 1
    }, [searchTerm])

    const fetchInvoices = useCallback(async () => {
        const requestId = ++requestIdRef.current
        setLoading(true)
        try {
            const endpoint = type === 'sales' ? '/sales' : '/purchases'
            const response = await api.get(endpoint, {
                params: {
                    page,
                    pageSize: 10,
                    search: debouncedSearchTerm,
                },
            })

            if (requestId !== requestIdRef.current) {
                return
            }

            setInvoices(response.data.items)
            setTotalPages(response.data.totalPages)
        } catch (error) {
            if (requestId !== requestIdRef.current) {
                return
            }

            console.error('Error fetching invoices:', error)
            showError(t('common.failedToLoadData'))
        } finally {
            if (requestId === requestIdRef.current) {
                setLoading(false)
            }
        }
    }, [type, page, debouncedSearchTerm, showError, t])

    useEffect(() => {
        if (!isSearchSettled) {
            return
        }

        void fetchInvoices()
    }, [fetchInvoices, isSearchSettled])

    useEffect(() => {
        if (isSearchSettled || page === 1) {
            return
        }

        setPage(1)
    }, [isSearchSettled, page, searchTerm])

    const handleDelete = (invoice) => {
        setDeleteModal({
            id: invoice.id || invoice.invoiceId,
            invoiceNumber: invoice.invoiceNumber
        })
    }

    const handleDeleteSuccess = () => {
        showSuccess(t('common.deletedSuccessfully'))
        void fetchInvoices()
    }

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-2 sm:p-4 z-50" dir={dir}>
            <div className="bg-white rounded-lg shadow-xl w-full min-w-0 max-w-4xl max-h-[90vh] flex flex-col">
                <div className={`p-4 border-b border-gray-200 flex justify-between items-center ${isRTL ? 'flex-row-reverse' : ''}`}>
                    <h2 className={`text-xl font-bold ${isRTL ? 'text-right' : 'text-left'}`}>
                        {type === 'sales' ? t('pos.searchInvoices') : t('purchases.searchInvoices')}
                    </h2>
                    <button onClick={onClose} className="text-gray-500 hover:text-gray-700 text-2xl">&times;</button>
                </div>

                <div className="p-4 border-b border-gray-200">
                    <input
                        type="text"
                        value={searchTerm}
                        onChange={(e) => setSearchTerm(e.target.value)}
                        placeholder={t('common.searchPlaceholder')}
                        className={`input w-full ${isRTL ? 'text-right' : 'text-left'}`}
                        autoFocus
                    />
                    {loading && invoices.length > 0 ? (
                        <p className="mt-2 text-xs text-gray-500">{t('common.loading')}</p>
                    ) : null}
                </div>

                <div className="flex-1 min-w-0 overflow-y-auto p-4">
                    {loading && invoices.length === 0 ? (
                        <div className="flex justify-center p-8">
                            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600"></div>
                        </div>
                    ) : (
                        <div className="w-full overflow-x-auto">
                        <table className={`w-full min-w-[680px] ${isRTL ? 'text-right' : 'text-left'}`}>
                            <thead className="bg-gray-50 text-gray-500 text-xs uppercase font-medium">
                                <tr>
                                    <th className={`px-4 py-2 ${isRTL ? 'text-right' : 'text-left'}`}>{t('common.invoiceNumber')}</th>
                                    <th className={`px-4 py-2 ${isRTL ? 'text-right' : 'text-left'}`}>{t('common.date')}</th>
                                    <th className={`px-4 py-2 ${isRTL ? 'text-right' : 'text-left'}`}>
                                        {type === 'sales' ? t('pos.customer') : t('purchases.supplier')}
                                    </th>
                                    <th className={`px-4 py-2 ${isRTL ? 'text-left' : 'text-right'}`}>{t('common.amount')}</th>
                                    <th className="px-4 py-2 text-center">{t('common.actions')}</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-200">
                                {invoices.map(invoice => {
                                    const canRewrite = canRewriteInvoice(type, invoice)

                                    return (
                                    <tr key={invoice.id || invoice.invoiceId} className="hover:bg-gray-50">
                                        <td className={`px-4 py-3 font-medium ${isRTL ? 'text-right' : 'text-left'}`}>{invoice.invoiceNumber}</td>
                                        <td className={`px-4 py-3 cursor-default ${isRTL ? 'text-right' : 'text-left'}`}>
                                            {formatDate(invoice.invoiceDate, locale)}
                                        </td>
                                        <td className={`px-4 py-3 ${isRTL ? 'text-right' : 'text-left'}`}>
                                            {type === 'sales' ? invoice.customer?.name || t('pos.walkIn') : invoice.supplier?.name}
                                        </td>
                                        <td className={`px-4 py-3 font-bold text-primary-600 ${isRTL ? 'text-left' : 'text-right'}`}>
                                            {formatCurrency(invoice.totalAmount)}
                                        </td>
                                        <td className={`px-4 py-3 flex justify-center gap-2 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                            <span title={canRewrite ? '' : t('pos.completedInvoiceReturnRequired')}>
                                                <button
                                                    onClick={() => onSelect(invoice)}
                                                    disabled={!canRewrite}
                                                    className="btn-secondary py-1 px-3 text-sm disabled:cursor-not-allowed disabled:opacity-50"
                                                >
                                                    {t('common.edit')}
                                                </button>
                                            </span>
                                            {onPrint && (
                                                <button
                                                    onClick={() => onPrint(invoice)}
                                                    className="btn-secondary py-1 px-3 text-sm"
                                                    title={t('common.print')}
                                                >
                                                    🖨️
                                                </button>
                                            )}
                                            <button
                                                onClick={() => handleDelete(invoice)}
                                                className="bg-red-50 text-red-600 hover:bg-red-100 py-1 px-3 rounded text-sm transition-colors"
                                                title={t('common.delete')}
                                            >
                                                🗑️
                                            </button>
                                        </td>
                                    </tr>
                                    )
                                })}
                                {invoices.length === 0 && (
                                    <tr>
                                        <td colSpan="5" className="px-4 py-8 text-center text-gray-500">
                                            {t('common.noResults')}
                                        </td>
                                    </tr>
                                )}
                            </tbody>
                        </table>
                        </div>
                    )}
                </div>

                {totalPages > 1 && (
                    <div className={`p-4 border-t border-gray-200 flex justify-between items-center bg-gray-50 ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <button
                            onClick={() => setPage(p => Math.max(1, p - 1))}
                            disabled={page === 1}
                            className="btn-secondary text-sm disabled:opacity-50"
                        >
                            {t('common.previous')}
                        </button>
                        <span className="text-sm text-gray-600">
                            {t('common.page')} {page} {t('common.of')} {totalPages}
                        </span>
                        <button
                            onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                            disabled={page === totalPages}
                            className="btn-secondary text-sm disabled:opacity-50"
                        >
                            {t('common.next')}
                        </button>
                    </div>
                )}
            </div>

            {/* Validation Delete Modal for Invoices */}
            {deleteModal && (
                <ValidationDeleteModal
                    isOpen={!!deleteModal}
                    onClose={() => setDeleteModal(null)}
                    entityType={type === 'sales' ? 'sale' : 'purchase'}
                    entityId={deleteModal.id}
                    entityName={deleteModal.invoiceNumber}
                    onDeleteSuccess={handleDeleteSuccess}
                />
            )}
        </div>
    )
}

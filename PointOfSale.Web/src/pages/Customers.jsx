import { useState, useEffect, useRef } from 'react'
import api from '../services/api'
import { useToast } from '../components/Toast'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import ValidationDeleteModal from '../components/ValidationDeleteModal'
import { useRTL } from '../hooks/useRTL'
import { useDebounce } from '../hooks/useDebounce'

export default function Customers() {
    const { t } = useTranslation()
    const { isRTL, dir } = useRTL()
    const { formatCurrency } = useSettings() // Added settings
    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const actionAlignClass = isRTL ? 'text-left' : 'text-right'
    const actionSpacingClass = isRTL ? 'ml-3' : 'mr-3'
    const [customers, setCustomers] = useState([])
    const [loading, setLoading] = useState(true)
    const [search, setSearch] = useState('')
    const [page, setPage] = useState(1)
    const [totalPages, setTotalPages] = useState(1)
    const [showModal, setShowModal] = useState(false)
    const [editingCustomer, setEditingCustomer] = useState(null)
    const [isSaving, setIsSaving] = useState(false)
    const saveInFlightRef = useRef(false)
    const [formData, setFormData] = useState({
        name: '',
        email: '',
        phone: '',
        address: '',
        taxNumber: '',
        notes: ''
    })
    const [deleteModal, setDeleteModal] = useState(null) // { id, name }
    const requestIdRef = useRef(0)
    const lastFetchKeyRef = useRef('')
    const debouncedSearch = useDebounce(search, 300)
    const prevPageRef = useRef(page)
    const prevSearchRef = useRef(debouncedSearch)

    const { showSuccess, showError } = useToast()

    const hasSameAppliedSearch = (searchValue) => searchValue.trim() === debouncedSearch.trim()

    useEffect(() => {
        const pageResetFromSearchChange = page === 1
            && prevPageRef.current !== page
            && prevSearchRef.current !== debouncedSearch

        prevPageRef.current = page
        prevSearchRef.current = debouncedSearch

        if (pageResetFromSearchChange) {
            return
        }

        void fetchCustomers()
    }, [page, debouncedSearch])

    useEffect(() => {
        return () => {
            requestIdRef.current += 1
        }
    }, [])

    const fetchCustomers = async (searchValue = debouncedSearch, pageNumber = page, { force = false } = {}) => {
        const normalizedSearch = searchValue.trim()
        const fetchKey = JSON.stringify({
            page: pageNumber,
            search: normalizedSearch,
        })

        if (!force && fetchKey === lastFetchKeyRef.current) {
            return
        }

        const requestId = ++requestIdRef.current
        setLoading(true)
        try {
            const response = await api.get('/customers', {
                params: { page: pageNumber, pageSize: 20, search: normalizedSearch || undefined }
            })

            if (requestId !== requestIdRef.current) {
                return
            }

            lastFetchKeyRef.current = fetchKey
            // Backend now returns PagedResult<Customer> with { items, totalPages, totalCount, page, pageSize }
            setCustomers(response.data.items)
            setTotalPages(response.data.totalPages)
        } catch (error) {
            if (requestId !== requestIdRef.current) {
                return
            }

            console.error('Error fetching customers:', error)
            showError(t('customers.failedToLoad'))
        } finally {
            if (requestId === requestIdRef.current) {
                setLoading(false)
            }
        }
    }

    const handleSearch = (e) => {
        e.preventDefault()
        const sameAppliedSearch = hasSameAppliedSearch(search)

        if (page !== 1 && sameAppliedSearch) {
            setPage(1)

            return
        }

        if (page !== 1 && !sameAppliedSearch) {
            prevSearchRef.current = search
        }

        setPage(1)
        void fetchCustomers(search, 1, { force: true })
    }

    const handleEdit = (customer) => {
        if (saveInFlightRef.current) return
        setEditingCustomer(customer)
        setFormData({
            name: customer.name,
            email: customer.email || '',
            phone: customer.phone || '',
            address: customer.address || '',
            taxNumber: customer.taxNumber || '',
            notes: customer.notes || ''
        })
        setShowModal(true)
    }

    const handleAdd = () => {
        if (saveInFlightRef.current) return
        setEditingCustomer(null)
        setFormData({
            name: '',
            email: '',
            phone: '',
            address: '',
            taxNumber: '',
            notes: ''
        })
        setShowModal(true)
    }

    const handleSubmit = async (e) => {
        e.preventDefault()
        if (saveInFlightRef.current) return
        saveInFlightRef.current = true
        setIsSaving(true)
        try {
            if (editingCustomer) {
                await api.put(`/customers/${editingCustomer.id}`, formData)
                showSuccess(t('customers.updated'))
            } else {
                await api.post('/customers', formData)
                showSuccess(t('customers.created'))
            }
            setShowModal(false)
            await fetchCustomers(search, page, { force: true })
        } catch (error) {
            console.error('Error saving customer:', error)
            const message = error.response?.data?.message || t('customers.failedToSave')
            showError(message)
        } finally {
            saveInFlightRef.current = false
            setIsSaving(false)
        }
    }

    const handleDelete = (customer) => {
        setDeleteModal({
            id: customer.id,
            name: customer.name
        })
    }

    const handleDeleteSuccess = () => {
        showSuccess(t('customers.deleted'))
        void fetchCustomers(search, page, { force: true })
    }

    return (
        <div dir={dir} className="h-full flex flex-col">
            <div className="mb-6 flex justify-between items-center">
                <h1 className="text-2xl font-bold text-gray-800">{t('customers.title')}</h1>
                <div className="flex gap-3">
                    <button onClick={handleAdd} className="btn-primary">
                        {t('customers.addCustomer')}
                    </button>
                </div>
            </div>

            {/* Search */}
            <div className="bg-white p-4 rounded-lg shadow mb-6">
                <form onSubmit={handleSearch} className="flex gap-4">
                    <input
                        type="text"
                        placeholder={t('customers.searchPlaceholder')}
                        value={search}
                        onChange={(e) => {
                            const nextSearch = e.target.value
                            const sameAppliedSearch = hasSameAppliedSearch(nextSearch)

                            if (page !== 1 && !sameAppliedSearch) {
                                prevSearchRef.current = nextSearch
                            }

                            setSearch(nextSearch)
                            setPage(1)
                        }}
                        className="input flex-1"
                    />
                    <button type="submit" className="btn-secondary">{t('common.search')}</button>
                </form>
            </div>

            {/* Customers Table */}
            <div className="bg-white rounded-lg shadow flex-1 overflow-hidden flex flex-col">
                {loading && customers.length > 0 && (
                    <div className="border-b border-gray-200 bg-gray-50 px-6 py-2 text-xs text-gray-500">
                        {t('common.loading')}
                    </div>
                )}
                <div className="overflow-x-auto flex-1">
                    <table className="w-full">
                        <thead className="bg-gray-50 border-b border-gray-200">
                            <tr>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>{t('customers.customerName')}</th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>{t('customers.contact')}</th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>{t('customers.balance')}</th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${actionAlignClass}`}>{t('common.actions')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {loading && customers.length === 0 ? (
                                <tr>
                                    <td colSpan="4" className="px-6 py-4 text-center text-gray-500">{t('common.loading')}</td>
                                </tr>
                            ) : customers.length === 0 ? (
                                <tr>
                                    <td colSpan="4" className="px-6 py-4 text-center text-gray-500">{t('customers.noCustomers')}</td>
                                </tr>
                            ) : (
                                customers.map((customer) => (
                                    <tr key={customer.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <div className="text-sm font-medium text-gray-900">{customer.name}</div>
                                            <div className="text-xs text-gray-500">{customer.taxNumber}</div>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <div className="text-sm text-gray-900">{customer.email}</div>
                                            <div className="text-sm text-gray-500">{customer.phone}</div>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm font-medium">
                                            <span className={customer.balance > 0 ? 'text-green-600' : customer.balance < 0 ? 'text-red-600' : 'text-gray-900'}>
                                                {formatCurrency(Math.abs(customer.balance))} {customer.balance > 0 ? t('accounts.debit') : customer.balance < 0 ? t('accounts.credit') : ''}
                                            </span>
                                        </td>
                                        <td className={`px-6 py-4 whitespace-nowrap text-sm font-medium ${actionAlignClass}`}>
                                            <button
                                                onClick={() => handleEdit(customer)}
                                                className={`text-primary-600 hover:text-primary-900 ${actionSpacingClass}`}
                                            >
                                                {t('common.edit')}
                                            </button>
                                            <button
                                                onClick={() => handleDelete(customer)}
                                                className="text-red-600 hover:text-red-900"
                                            >
                                                {t('common.delete')}
                                            </button>
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>

                {/* Pagination - hidden if only 1 page */}
                {totalPages > 1 && (
                    <div className="bg-gray-50 px-4 py-3 border-t border-gray-200 flex items-center justify-between">
                        <button
                            onClick={() => setPage(p => Math.max(1, p - 1))}
                            disabled={page === 1}
                            className="btn-secondary text-sm"
                        >
                            {t('customers.previous')}
                        </button>
                        <span className="text-sm text-gray-700">
                            {t('customers.page')} {page} {t('customers.of')} {totalPages}
                        </span>
                        <button
                            onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                            disabled={page === totalPages}
                            className="btn-secondary text-sm"
                        >
                            {t('customers.next')}
                        </button>
                    </div>
                )}
            </div>

            {/* Create/Edit Modal */}
            {showModal && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4 p-6">
                        <h2 className="text-xl font-bold mb-4">
                            {editingCustomer ? t('customers.editCustomer') : t('customers.newCustomer')}
                        </h2>
                        <form onSubmit={handleSubmit} className="space-y-4">
                            <div>
                                <label className="label">{t('customers.name')}</label>
                                <input
                                    type="text"
                                    value={formData.name}
                                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                                    className="input w-full"
                                    required
                                />
                            </div>
                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="label">{t('customers.email')}</label>
                                    <input
                                        type="email"
                                        value={formData.email}
                                        onChange={(e) => setFormData({ ...formData, email: e.target.value })}
                                        className="input w-full"
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('customers.phone')}</label>
                                    <input
                                        type="text"
                                        value={formData.phone}
                                        onChange={(e) => setFormData({ ...formData, phone: e.target.value })}
                                        className="input w-full"
                                    />
                                </div>
                            </div>
                            <div>
                                <label className="label">{t('customers.address')}</label>
                                <input
                                    type="text"
                                    value={formData.address}
                                    onChange={(e) => setFormData({ ...formData, address: e.target.value })}
                                    className="input w-full"
                                />
                            </div>
                            <div>
                                <label className="label">{t('customers.taxNumber')}</label>
                                <input
                                    type="text"
                                    value={formData.taxNumber}
                                    onChange={(e) => setFormData({ ...formData, taxNumber: e.target.value })}
                                    className="input w-full"
                                />
                            </div>
                            <div>
                                <label className="label">{t('customers.notes')}</label>
                                <textarea
                                    value={formData.notes}
                                    onChange={(e) => setFormData({ ...formData, notes: e.target.value })}
                                    className="input w-full h-20"
                                />
                            </div>
                            <div className="flex justify-end gap-3 mt-6">
                                <button
                                    type="button"
                                    onClick={() => setShowModal(false)}
                                    disabled={isSaving}
                                    className="btn-secondary"
                                >
                                    {t('common.cancel')}
                                </button>
                                <button type="submit" disabled={isSaving} aria-busy={isSaving || undefined} className="btn-primary">
                                    {t('common.save')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {/* Validation Delete Modal */}
            {deleteModal && (
                <ValidationDeleteModal
                    isOpen={!!deleteModal}
                    onClose={() => setDeleteModal(null)}
                    entityType="customer"
                    entityId={deleteModal.id}
                    entityName={deleteModal.name}
                    onDeleteSuccess={handleDeleteSuccess}
                />
            )}
        </div>
    )
}

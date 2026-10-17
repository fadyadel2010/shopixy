import { useState, useEffect, useRef } from 'react'
import { Link } from 'react-router-dom'
import api from '../services/api'
import { useToast } from '../components/Toast'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import ValidationDeleteModal from '../components/ValidationDeleteModal'
import { useRTL } from '../hooks/useRTL'

export default function Suppliers() {
    const { t } = useTranslation()
    const { isRTL, dir } = useRTL()
    const { formatCurrency } = useSettings()
    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const actionAlignClass = isRTL ? 'text-left' : 'text-right'
    const actionSpacingClass = isRTL ? 'ml-3' : 'mr-3'
    const [suppliers, setSuppliers] = useState([])
    const [loading, setLoading] = useState(true)
    const [search, setSearch] = useState('')
    const [page, setPage] = useState(1)
    const [totalPages, setTotalPages] = useState(1)
    const [showModal, setShowModal] = useState(false)
    const [editingSupplier, setEditingSupplier] = useState(null)
    const [isSaving, setIsSaving] = useState(false)
    const saveInFlightRef = useRef(false)
    const requestIdRef = useRef(0)
    const [formData, setFormData] = useState({
        name: '',
        email: '',
        phone: '',
        address: '',
        taxNumber: '',
        paymentTermsDays: 0,
        notes: ''
    })
    const [deleteModal, setDeleteModal] = useState(null) // { id, name }

    const { showSuccess, showError } = useToast()

    useEffect(() => {
        fetchSuppliers()
    }, [page, search])

    useEffect(() => () => { requestIdRef.current += 1 }, [])

    const fetchSuppliers = async () => {
        const requestId = ++requestIdRef.current
        setLoading(true)
        try {
            const response = await api.get('/suppliers', {
                params: { page, pageSize: 20, search: search || undefined }
            })
            if (requestId !== requestIdRef.current) return
            setSuppliers(response.data.items)
            setTotalPages(response.data.totalPages)
        } catch (error) {
            if (requestId !== requestIdRef.current) return
            console.error('Error fetching suppliers:', error)
            showError(t('suppliers.failedToLoad'))
        } finally {
            if (requestId === requestIdRef.current) setLoading(false)
        }
    }

    const handleSearch = (e) => {
        e.preventDefault()
        setPage(1)
        fetchSuppliers()
    }

    const handleEdit = (supplier) => {
        if (saveInFlightRef.current) return
        setEditingSupplier(supplier)
        setFormData({
            name: supplier.name,
            email: supplier.email || '',
            phone: supplier.phone || '',
            address: supplier.address || '',
            taxNumber: supplier.taxNumber || '',
            paymentTermsDays: supplier.paymentTermsDays || 0,
            notes: supplier.notes || ''
        })
        setShowModal(true)
    }

    const handleAdd = () => {
        if (saveInFlightRef.current) return
        setEditingSupplier(null)
        setFormData({
            name: '',
            email: '',
            phone: '',
            address: '',
            taxNumber: '',
            paymentTermsDays: 0,
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
            if (editingSupplier) {
                await api.put(`/suppliers/${editingSupplier.id}`, formData)
                showSuccess(t('suppliers.updated'))
            } else {
                await api.post('/suppliers', formData)
                showSuccess(t('suppliers.created'))
            }
            setShowModal(false)
            fetchSuppliers()
        } catch (error) {
            console.error('Error saving supplier:', error)
            showError(t('suppliers.failedToSave'))
        } finally {
            saveInFlightRef.current = false
            setIsSaving(false)
        }
    }

    const handleDelete = (supplier) => {
        setDeleteModal({
            id: supplier.id,
            name: supplier.name
        })
    }

    const handleDeleteSuccess = () => {
        showSuccess(t('suppliers.deleted'))
        fetchSuppliers()
    }

    return (
        <div dir={dir} className="h-full flex flex-col">
            <div className="mb-6 flex justify-between items-center">
                <h1 className="text-2xl font-bold text-gray-800">{t('suppliers.title')}</h1>
                <div className="flex gap-3">
                    <Link to="/purchase-invoice" className="btn-secondary">
                        {t('suppliers.newPurchase')}
                    </Link>
                    <button onClick={handleAdd} className="btn-primary">
                        {t('suppliers.addSupplier')}
                    </button>
                </div>
            </div>

            {/* Search */}
            <div className="bg-white p-4 rounded-lg shadow mb-6">
                <form onSubmit={handleSearch} className="flex gap-4">
                    <input
                        type="text"
                        placeholder={t('suppliers.searchPlaceholder')}
                        value={search}
                        onChange={(e) => setSearch(e.target.value)}
                        className="input flex-1"
                    />
                    <button type="submit" className="btn-secondary">{t('common.search')}</button>
                </form>
            </div>

            {/* Suppliers Table */}
            <div className="bg-white rounded-lg shadow flex-1 overflow-hidden flex flex-col">
                <div className="overflow-x-auto flex-1">
                    <table className="w-full">
                        <thead className="bg-gray-50 border-b border-gray-200">
                            <tr>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>{t('suppliers.supplierName')}</th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>{t('suppliers.contact')}</th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>{t('suppliers.balance')}</th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${actionAlignClass}`}>{t('common.actions')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {loading ? (
                                <tr>
                                    <td colSpan="4" className="px-6 py-4 text-center text-gray-500">{t('common.loading')}</td>
                                </tr>
                            ) : suppliers.length === 0 ? (
                                <tr>
                                    <td colSpan="4" className="px-6 py-4 text-center text-gray-500">{t('suppliers.noSuppliers')}</td>
                                </tr>
                            ) : (
                                suppliers.map((supplier) => (
                                    <tr key={supplier.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <div className="text-sm font-medium text-gray-900">{supplier.name}</div>
                                            <div className="text-xs text-gray-500">{supplier.taxNumber}</div>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <div className="text-sm text-gray-900">{supplier.email}</div>
                                            <div className="text-sm text-gray-500">{supplier.phone}</div>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm font-medium">
                                            <span className={supplier.balance < 0 ? 'text-red-600' : supplier.balance > 0 ? 'text-green-600' : 'text-gray-600'}>
                                                {formatCurrency(supplier.balance)}
                                            </span>
                                        </td>
                                        <td className={`px-6 py-4 whitespace-nowrap text-sm font-medium ${actionAlignClass}`}>
                                            <button
                                                onClick={() => handleEdit(supplier)}
                                                className={`text-primary-600 hover:text-primary-900 ${actionSpacingClass}`}
                                            >
                                                {t('common.edit')}
                                            </button>
                                            <button
                                                onClick={() => handleDelete(supplier)}
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

                {/* Pagination */}
                <div className="bg-gray-50 px-4 py-3 border-t border-gray-200 flex items-center justify-between">
                    <button
                        onClick={() => setPage(p => Math.max(1, p - 1))}
                        disabled={page === 1}
                        className="btn-secondary text-sm"
                    >
                        {t('suppliers.previous')}
                    </button>
                    <span className="text-sm text-gray-700">
                        {t('suppliers.page')} {page} {t('suppliers.of')} {totalPages}
                    </span>
                    <button
                        onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                        disabled={page === totalPages}
                        className="btn-secondary text-sm"
                    >
                        {t('suppliers.next')}
                    </button>
                </div>
            </div>

            {/* Create/Edit Modal */}
            {showModal && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4 p-6">
                        <h2 className="text-xl font-bold mb-4">
                            {editingSupplier ? t('suppliers.editSupplier') : t('suppliers.newSupplier')}
                        </h2>
                        <form onSubmit={handleSubmit} className="space-y-4">
                            <div>
                                <label className="label">{t('suppliers.name')}</label>
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
                                    <label className="label">{t('suppliers.email')}</label>
                                    <input
                                        type="email"
                                        value={formData.email}
                                        onChange={(e) => setFormData({ ...formData, email: e.target.value })}
                                        className="input w-full"
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('suppliers.phone')}</label>
                                    <input
                                        type="text"
                                        value={formData.phone}
                                        onChange={(e) => setFormData({ ...formData, phone: e.target.value })}
                                        className="input w-full"
                                    />
                                </div>
                            </div>
                            <div>
                                <label className="label">{t('suppliers.address')}</label>
                                <input
                                    type="text"
                                    value={formData.address}
                                    onChange={(e) => setFormData({ ...formData, address: e.target.value })}
                                    className="input w-full"
                                />
                            </div>
                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="label">{t('suppliers.taxNumber')}</label>
                                    <input
                                        type="text"
                                        value={formData.taxNumber}
                                        onChange={(e) => setFormData({ ...formData, taxNumber: e.target.value })}
                                        className="input w-full"
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('suppliers.paymentTerms')}</label>
                                    <input
                                        type="number"
                                        value={formData.paymentTermsDays}
                                        onChange={(e) => setFormData({ ...formData, paymentTermsDays: parseInt(e.target.value) || 0 })}
                                        className="input w-full"
                                    />
                                </div>
                            </div>
                            <div>
                                <label className="label">{t('suppliers.notes')}</label>
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
                    entityType="supplier"
                    entityId={deleteModal.id}
                    entityName={deleteModal.name}
                    onDeleteSuccess={handleDeleteSuccess}
                />
            )}
        </div>
    )
}

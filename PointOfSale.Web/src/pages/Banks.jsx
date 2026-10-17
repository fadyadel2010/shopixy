import { useState, useEffect, useCallback } from 'react'
import api from '../services/api'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import { useToast } from '../components/Toast'
import { useTranslation } from 'react-i18next'

export default function Banks() {
    const { t } = useTranslation()
    const { confirm } = useConfirmDialog()
    const [banks, setBanks] = useState([])
    const [loading, setLoading] = useState(true)
    const [search, setSearch] = useState('')
    const [page, setPage] = useState(1)
    const [totalPages, setTotalPages] = useState(1)
    const [showModal, setShowModal] = useState(false)
    const [editingBank, setEditingBank] = useState(null)
    const [formData, setFormData] = useState({
        bankName: '',
        branchName: '',
        accountNumber: '',
        iban: '',
        notes: '',
        isActive: true
    })

    const { showSuccess, showError } = useToast()

    const fetchBanks = useCallback(async () => {
        setLoading(true)
        try {
            const response = await api.get('/banks', {
                params: { page, pageSize: 20, search: search || undefined }
            })
            setBanks(response.data.items)
            setTotalPages(response.data.totalPages)
        } catch (error) {
            console.error('Error fetching banks:', error)
            showError(t('banks.failedToLoad'))
        } finally {
            setLoading(false)
        }
    }, [page, search, showError, t])

    useEffect(() => {
        fetchBanks()
    }, [fetchBanks])

    const handleSearch = (e) => {
        e.preventDefault()
        setPage(1)
        fetchBanks()
    }

    const handleEdit = (bank) => {
        setEditingBank(bank)
        setFormData({
            bankName: bank.bankName,
            branchName: bank.branchName || '',
            accountNumber: bank.accountNumber || '',
            iban: bank.iban || '',
            notes: bank.notes || '',
            isActive: bank.isActive
        })
        setShowModal(true)
    }

    const handleAdd = () => {
        setEditingBank(null)
        setFormData({
            bankName: '',
            branchName: '',
            accountNumber: '',
            iban: '',
            notes: '',
            isActive: true
        })
        setShowModal(true)
    }

    const handleSubmit = async (e) => {
        e.preventDefault()
        try {
            if (editingBank) {
                await api.put(`/banks/${editingBank.id}`, formData)
                showSuccess(t('banks.updated'))
            } else {
                await api.post('/banks', formData)
                showSuccess(t('banks.created'))
            }
            setShowModal(false)
            fetchBanks()
        } catch (error) {
            console.error('Error saving bank:', error)
            const message = error.response?.data?.message || t('banks.failedToSave')
            showError(message)
        }
    }

    const handleDelete = async (id) => {
        const confirmed = await confirm({
            confirmLabel: t('common.delete'),
            description: t('banks.deleteConfirm'),
            title: t('common.warning'),
            variant: 'danger',
        })

        if (!confirmed) return
        try {
            await api.delete(`/banks/${id}`)
            showSuccess(t('banks.deleted'))
            fetchBanks()
        } catch (error) {
            console.error('Error deleting bank:', error)
            const message = error.response?.data?.message || t('banks.failedToDelete')
            showError(message)
        }
    }

    const handleToggleStatus = async (id) => {
        try {
            await api.patch(`/banks/${id}/toggle-status`)
            showSuccess(t('banks.statusUpdated'))
            fetchBanks()
        } catch (error) {
            console.error('Error toggling status:', error)
            showError(t('banks.failedToToggleStatus'))
        }
    }

    return (
        <div className="h-full flex flex-col">
            <div className="mb-6 flex justify-between items-center">
                <h1 className="text-2xl font-bold text-gray-800">{t('banks.title')}</h1>
                <div className="flex gap-3">
                    <button onClick={handleAdd} className="btn-primary">
                        {t('banks.addBank')}
                    </button>
                </div>
            </div>

            {/* Search */}
            <div className="bg-white p-4 rounded-lg shadow mb-6">
                <form onSubmit={handleSearch} className="flex gap-4">
                    <input
                        type="text"
                        placeholder={t('banks.searchPlaceholder')}
                        value={search}
                        onChange={(e) => setSearch(e.target.value)}
                        className="input flex-1"
                    />
                    <button type="submit" className="btn-secondary">{t('common.search')}</button>
                </form>
            </div>

            {/* Banks Table */}
            <div className="bg-white rounded-lg shadow flex-1 overflow-hidden flex flex-col">
                <div className="overflow-x-auto flex-1">
                    <table className="w-full">
                        <thead className="bg-gray-50 border-b border-gray-200">
                            <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('banks.bankName')}</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('banks.branch')}</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('banks.accountNumber')}</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('banks.iban')}</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('common.status')}</th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">{t('common.actions')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {loading ? (
                                <tr>
                                    <td colSpan="6" className="px-6 py-4 text-center text-gray-500">{t('common.loading')}</td>
                                </tr>
                            ) : banks.length === 0 ? (
                                <tr>
                                    <td colSpan="6" className="px-6 py-4 text-center text-gray-500">{t('banks.noBanks')}</td>
                                </tr>
                            ) : (
                                banks.map((bank) => (
                                    <tr key={bank.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <div className="text-sm font-medium text-gray-900">{bank.bankName}</div>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <div className="text-sm text-gray-900">{bank.branchName || '-'}</div>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <div className="text-sm text-gray-900">{bank.accountNumber || '-'}</div>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <div className="text-sm text-gray-900">{bank.iban || '-'}</div>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <button
                                                onClick={() => handleToggleStatus(bank.id)}
                                                className={`px-2 py-1 text-xs font-semibold rounded-full ${bank.isActive
                                                        ? 'bg-green-100 text-green-800'
                                                        : 'bg-gray-100 text-gray-800'
                                                    }`}
                                            >
                                                {bank.isActive ? t('common.active') : t('common.inactive')}
                                            </button>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                                            <button
                                                onClick={() => handleEdit(bank)}
                                                className="text-primary-600 hover:text-primary-900 mr-3"
                                            >
                                                {t('common.edit')}
                                            </button>
                                            <button
                                                onClick={() => handleDelete(bank.id)}
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
                {totalPages > 1 && (
                    <div className="bg-gray-50 px-4 py-3 border-t border-gray-200 flex items-center justify-between">
                        <button
                            onClick={() => setPage(p => Math.max(1, p - 1))}
                            disabled={page === 1}
                            className="btn-secondary text-sm"
                        >
                            {t('common.previous')}
                        </button>
                        <span className="text-sm text-gray-700">
                            {t('common.page')} {page} {t('common.of')} {totalPages}
                        </span>
                        <button
                            onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                            disabled={page === totalPages}
                            className="btn-secondary text-sm"
                        >
                            {t('common.next')}
                        </button>
                    </div>
                )}
            </div>

            {/* Create/Edit Modal */}
            {showModal && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4 p-6">
                        <h2 className="text-xl font-bold mb-4">
                            {editingBank ? t('banks.editBank') : t('banks.newBank')}
                        </h2>
                        <form onSubmit={handleSubmit} className="space-y-4">
                            <div>
                                <label className="label">{t('banks.bankName')}</label>
                                <input
                                    type="text"
                                    value={formData.bankName}
                                    onChange={(e) => setFormData({ ...formData, bankName: e.target.value })}
                                    className="input w-full"
                                    required
                                />
                            </div>
                            <div>
                                <label className="label">{t('banks.branchName')}</label>
                                <input
                                    type="text"
                                    value={formData.branchName}
                                    onChange={(e) => setFormData({ ...formData, branchName: e.target.value })}
                                    className="input w-full"
                                />
                            </div>
                            <div>
                                <label className="label">{t('banks.accountNumber')}</label>
                                <input
                                    type="text"
                                    value={formData.accountNumber}
                                    onChange={(e) => setFormData({ ...formData, accountNumber: e.target.value })}
                                    className="input w-full"
                                />
                            </div>
                            <div>
                                <label className="label">{t('banks.iban')}</label>
                                <input
                                    type="text"
                                    value={formData.iban}
                                    onChange={(e) => setFormData({ ...formData, iban: e.target.value })}
                                    className="input w-full"
                                />
                            </div>
                            <div>
                                <label className="label">{t('banks.notes')}</label>
                                <textarea
                                    value={formData.notes}
                                    onChange={(e) => setFormData({ ...formData, notes: e.target.value })}
                                    className="input w-full h-20"
                                />
                            </div>
                            <div className="flex items-center">
                                <input
                                    type="checkbox"
                                    id="isActive"
                                    checked={formData.isActive}
                                    onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                                    className="mr-2"
                                />
                                <label htmlFor="isActive" className="text-sm text-gray-700">{t('banks.isActive')}</label>
                            </div>
                            <div className="flex justify-end gap-3 mt-6">
                                <button
                                    type="button"
                                    onClick={() => setShowModal(false)}
                                    className="btn-secondary"
                                >
                                    {t('common.cancel')}
                                </button>
                                <button type="submit" className="btn-primary">
                                    {t('common.save')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    )
}

import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import api from '../services/api'
import { useToast } from '../components/Toast'

export default function SalesRepresentatives() {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()
    const { confirm } = useConfirmDialog()
    const { showSuccess, showError } = useToast()

    const [salesReps, setSalesReps] = useState([])
    const [loading, setLoading] = useState(false)
    const [showModal, setShowModal] = useState(false)
    const [editingRep, setEditingRep] = useState(null)
    const [searchTerm, setSearchTerm] = useState('')
    const [filterActive, setFilterActive] = useState('all')

    // Form state
    const [formData, setFormData] = useState({
        name: '',
        phone: '',
        isActive: true,
        monthlyTarget: '',
        commissionPercentage: '',
        notes: ''
    })

    useEffect(() => {
        fetchSalesReps()
    }, [])

    const fetchSalesReps = async () => {
        setLoading(true)
        try {
            const params = new URLSearchParams({ pageSize: 100 })
            if (searchTerm) params.append('search', searchTerm)
            if (filterActive !== 'all') params.append('isActive', filterActive === 'active')

            const response = await api.get(`/salesrepresentatives?${params}`)
            const data = response.data.items || response.data || []
            setSalesReps(data)
        } catch (error) {
            console.error('Error fetching sales representatives:', error)
            showError(t('salesRepresentatives.failedToLoad'))
        } finally {
            setLoading(false)
        }
    }

    const handleSubmit = async (e) => {
        e.preventDefault()

        if (!formData.name.trim()) {
            showError(t('salesRepresentatives.nameRequired'))
            return
        }

        setLoading(true)
        try {
            const payload = {
                name: formData.name,
                phone: formData.phone || null,
                isActive: formData.isActive,
                monthlyTarget: formData.monthlyTarget ? parseFloat(formData.monthlyTarget) : null,
                commissionPercentage: formData.commissionPercentage ? parseFloat(formData.commissionPercentage) : null,
                notes: formData.notes || null
            }

            if (editingRep) {
                await api.put(`/salesrepresentatives/${editingRep.id}`, payload)
                showSuccess(t('salesRepresentatives.repUpdated'))
            } else {
                await api.post('/salesrepresentatives', payload)
                showSuccess(t('salesRepresentatives.repCreated'))
            }

            setShowModal(false)
            resetForm()
            fetchSalesReps()
        } catch (error) {
            console.error('Error saving sales representative:', error)
            showError(error.response?.data?.message || t('salesRepresentatives.failedToSave'))
        } finally {
            setLoading(false)
        }
    }

    const handleEdit = (rep) => {
        setEditingRep(rep)
        setFormData({
            name: rep.name,
            phone: rep.phone || '',
            isActive: rep.isActive,
            monthlyTarget: rep.monthlyTarget || '',
            commissionPercentage: rep.commissionPercentage || '',
            notes: rep.notes || ''
        })
        setShowModal(true)
    }

    const handleDelete = async (id) => {
        const confirmed = await confirm({
            confirmLabel: t('common.delete'),
            description: t('salesRepresentatives.deleteConfirm'),
            title: t('common.warning'),
            variant: 'danger',
        })

        if (!confirmed) {
            return
        }

        setLoading(true)
        try {
            await api.delete(`/salesrepresentatives/${id}`)
            showSuccess(t('salesRepresentatives.repDeleted'))
            fetchSalesReps()
        } catch (error) {
            console.error('Error deleting sales representative:', error)
            showError(error.response?.data?.message || t('salesRepresentatives.failedToDelete'))
        } finally {
            setLoading(false)
        }
    }

    const resetForm = () => {
        setFormData({
            name: '',
            phone: '',
            isActive: true,
            monthlyTarget: '',
            commissionPercentage: '',
            notes: ''
        })
        setEditingRep(null)
    }

    const handleSearch = () => {
        fetchSalesReps()
    }

    return (
        <div className="h-full flex flex-col gap-6">
            <div className="flex justify-between items-center">
                <h1 className="text-2xl font-bold text-gray-800">{t('salesRepresentatives.title')}</h1>
                <button
                    onClick={() => {
                        resetForm()
                        setShowModal(true)
                    }}
                    className="btn-primary"
                >
                    {t('salesRepresentatives.addRep')}
                </button>
            </div>

            {/* Filters */}
            <div className="bg-white p-4 rounded-lg shadow">
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                    <div>
                        <input
                            type="text"
                            placeholder={t('salesRepresentatives.searchPlaceholder')}
                            value={searchTerm}
                            onChange={(e) => setSearchTerm(e.target.value)}
                            onKeyPress={(e) => e.key === 'Enter' && handleSearch()}
                            className="input w-full"
                        />
                    </div>
                    <div>
                        <select
                            value={filterActive}
                            onChange={(e) => setFilterActive(e.target.value)}
                            className="input w-full"
                        >
                            <option value="all">{t('salesRepresentatives.allStatus')}</option>
                            <option value="active">{t('salesRepresentatives.activeOnly')}</option>
                            <option value="inactive">{t('salesRepresentatives.inactiveOnly')}</option>
                        </select>
                    </div>
                    <div>
                        <button onClick={handleSearch} className="btn-primary w-full">
                            {t('salesRepresentatives.search')}
                        </button>
                    </div>
                </div>
            </div>

            {/* Table */}
            <div className="bg-white rounded-lg shadow overflow-hidden flex-1">
                <div className="overflow-x-auto">
                    <table className="w-full">
                        <thead className="bg-gray-50 border-b border-gray-200">
                            <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('salesRepresentatives.name')}</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('salesRepresentatives.phone')}</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('common.status')}</th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase">{t('salesRepresentatives.monthlyTarget')}</th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase">{t('salesRepresentatives.commission')}</th>
                                <th className="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase">{t('common.actions')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {loading ? (
                                <tr>
                                    <td colSpan="6" className="px-6 py-8 text-center text-gray-500">
                                        {t('common.loading')}
                                    </td>
                                </tr>
                            ) : salesReps.length === 0 ? (
                                <tr>
                                    <td colSpan="6" className="px-6 py-8 text-center text-gray-500">
                                        {t('salesRepresentatives.noRepsFound')}
                                    </td>
                                </tr>
                            ) : (
                                salesReps.map(rep => (
                                    <tr key={rep.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4 text-sm font-medium text-gray-900">
                                            {rep.name}
                                        </td>
                                        <td className="px-6 py-4 text-sm text-gray-500">
                                            {rep.phone || '-'}
                                        </td>
                                        <td className="px-6 py-4 text-sm">
                                            <span className={`px-2 py-1 rounded-full text-xs font-medium ${rep.isActive
                                                ? 'bg-green-100 text-green-800'
                                                : 'bg-red-100 text-red-800'
                                                }`}>
                                                {rep.isActive ? t('salesRepresentatives.active') : t('salesRepresentatives.inactive')}
                                            </span>
                                        </td>
                                        <td className="px-6 py-4 text-sm text-right text-gray-500">
                                            {rep.monthlyTarget ? formatCurrency(rep.monthlyTarget) : '-'}
                                        </td>
                                        <td className="px-6 py-4 text-sm text-right text-gray-500">
                                            {rep.commissionPercentage ? `${rep.commissionPercentage}%` : '-'}
                                        </td>
                                        <td className="px-6 py-4 text-sm text-center">
                                            <button
                                                onClick={() => handleEdit(rep)}
                                                className="text-blue-600 hover:text-blue-800 mr-3"
                                            >
                                                {t('common.edit')}
                                            </button>
                                            <button
                                                onClick={() => handleDelete(rep.id)}
                                                className="text-red-600 hover:text-red-800"
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
            </div>

            {/* Modal */}
            {showModal && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-lg p-6 w-full max-w-md">
                        <h2 className="text-xl font-bold mb-4">
                            {editingRep ? t('salesRepresentatives.editRep') : t('salesRepresentatives.addRepTitle')}
                        </h2>

                        <form onSubmit={handleSubmit} className="space-y-4">
                            <div>
                                <label className="label">{t('salesRepresentatives.name')}</label>
                                <input
                                    type="text"
                                    value={formData.name}
                                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                                    className="input w-full"
                                    required
                                />
                            </div>

                            <div>
                                <label className="label">{t('salesRepresentatives.phone')}</label>
                                <input
                                    type="text"
                                    value={formData.phone}
                                    onChange={(e) => setFormData({ ...formData, phone: e.target.value })}
                                    className="input w-full"
                                />
                            </div>

                            <div>
                                <label className="flex items-center gap-2">
                                    <input
                                        type="checkbox"
                                        checked={formData.isActive}
                                        onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                                        className="rounded"
                                    />
                                    <span className="text-sm font-medium">{t('salesRepresentatives.active')}</span>
                                </label>
                            </div>

                            <div>
                                <label className="label">{t('salesRepresentatives.monthlyTargetOptional')}</label>
                                <input
                                    type="number"
                                    step="0.01"
                                    value={formData.monthlyTarget}
                                    onChange={(e) => setFormData({ ...formData, monthlyTarget: e.target.value })}
                                    className="input w-full"
                                    placeholder="0.00"
                                />
                            </div>

                            <div>
                                <label className="label">{t('salesRepresentatives.commissionOptional')}</label>
                                <input
                                    type="number"
                                    step="0.01"
                                    value={formData.commissionPercentage}
                                    onChange={(e) => setFormData({ ...formData, commissionPercentage: e.target.value })}
                                    className="input w-full"
                                    placeholder="0.00"
                                />
                            </div>

                            <div>
                                <label className="label">{t('salesRepresentatives.notes')}</label>
                                <textarea
                                    value={formData.notes}
                                    onChange={(e) => setFormData({ ...formData, notes: e.target.value })}
                                    className="input w-full"
                                    rows="3"
                                />
                            </div>

                            <div className="flex gap-3 pt-4">
                                <button
                                    type="button"
                                    onClick={() => {
                                        setShowModal(false)
                                        resetForm()
                                    }}
                                    className="btn-secondary flex-1"
                                >
                                    {t('salesRepresentatives.cancel')}
                                </button>
                                <button
                                    type="submit"
                                    disabled={loading}
                                    className="btn-primary flex-1"
                                >
                                    {loading ? t('salesRepresentatives.saving') : (editingRep ? t('salesRepresentatives.update') : t('salesRepresentatives.create'))}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    )
}

import { useState, useEffect } from 'react'
import api from '../services/api'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import { useToast } from '../components/Toast'
import { useTranslation } from 'react-i18next'
import { useRTL } from '../hooks/useRTL'

export default function Units() {
    const { t } = useTranslation()
    const { isRTL, dir } = useRTL()
    const { confirm } = useConfirmDialog()
    const { showSuccess, showError } = useToast()
    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const actionAlignClass = isRTL ? 'text-left' : 'text-right'
    const actionSpacingClass = isRTL ? 'ml-4' : 'mr-4'
    const checkboxLabelSpacingClass = isRTL ? 'mr-2' : 'ml-2'
    const [units, setUnits] = useState([])
    const [loading, setLoading] = useState(true)
    const [showModal, setShowModal] = useState(false)
    const [editingUnit, setEditingUnit] = useState(null)
    const [formData, setFormData] = useState({
        name: '',
        symbol: '',
        description: '',
        isActive: true
    })

    useEffect(() => {
        fetchUnits()
    }, [])

    const fetchUnits = async () => {
        try {
            setLoading(true)
            const response = await api.get('/units?includeInactive=true')
            setUnits(response.data)
        } catch (error) {
            console.error('Error fetching units:', error)
            showError(t('units.failedToLoad'))
        } finally {
            setLoading(false)
        }
    }

    const handleAdd = () => {
        setEditingUnit(null)
        setFormData({
            name: '',
            symbol: '',
            description: '',
            isActive: true
        })
        setShowModal(true)
    }

    const handleEdit = (unit) => {
        setEditingUnit(unit)
        setFormData({
            name: unit.name,
            symbol: unit.symbol,
            description: unit.description || '',
            isActive: unit.isActive
        })
        setShowModal(true)
    }

    const handleSubmit = async (e) => {
        e.preventDefault()

        try {
            if (editingUnit) {
                await api.put(`/units/${editingUnit.id}`, formData)
                showSuccess(t('units.updated'))
            } else {
                await api.post('/units', formData)
                showSuccess(t('units.created'))
            }

            setShowModal(false)
            fetchUnits()
        } catch (error) {
            console.error('Error saving unit:', error)
            showError(error.response?.data || t('units.failedToSave'))
        }
    }

    const handleDelete = async (id) => {
        const confirmed = await confirm({
            confirmLabel: t('common.delete'),
            description: t('units.deleteConfirm'),
            title: t('common.warning'),
            variant: 'danger',
        })

        if (!confirmed) return

        try {
            await api.delete(`/units/${id}`)
            showSuccess(t('units.deleted'))
            fetchUnits()
        } catch (error) {
            console.error('Error deleting unit:', error)
            showError(error.response?.data || t('units.failedToDelete'))
        }
    }

    const handleSeedDefaults = async () => {
        const confirmed = await confirm({
            confirmLabel: t('common.confirm'),
            description: t('units.seedDefaultsConfirm'),
            title: t('common.warning'),
            variant: 'warning',
        })

        if (!confirmed) return

        try {
            const response = await api.post('/units/seed-defaults')
            showSuccess(response.data.message || t('units.defaultsSeeded'))
            fetchUnits()
        } catch (error) {
            console.error('Error seeding defaults:', error)
            showError(t('units.failedToSeedDefaults'))
        }
    }

    return (
        <div dir={dir} className="h-full flex flex-col">
            {/* Header */}
            <div className="flex justify-between items-center mb-6">
                <div>
                    <h1 className="text-2xl font-bold text-gray-800">{t('units.title')}</h1>
                    <p className="text-sm text-gray-600 mt-1">{t('units.subtitle')}</p>
                </div>
                <div className="flex gap-3">
                    <button
                        onClick={handleSeedDefaults}
                        className="btn-secondary"
                    >
                        🌱 {t('units.seedDefaults')}
                    </button>
                    <button
                        onClick={handleAdd}
                        className="btn-primary"
                    >
                        + {t('units.addUnit')}
                    </button>
                </div>
            </div>

            {/* Units Table */}
            <div className="card flex-1 overflow-hidden flex flex-col">
                {loading ? (
                    <div className="flex-1 flex items-center justify-center">
                        <p className="text-gray-500">{t('common.loading')}</p>
                    </div>
                ) : units.length === 0 ? (
                    <div className="flex-1 flex items-center justify-center">
                        <div className="text-center">
                            <p className="text-gray-500 mb-4">{t('units.noUnits')}</p>
                            <button onClick={handleAdd} className="btn-primary">
                                + {t('units.addFirstUnit')}
                            </button>
                        </div>
                    </div>
                ) : (
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                                <tr>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('units.name')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('units.symbol')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('units.description')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('common.status')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${actionAlignClass}`}>
                                        {t('common.actions')}
                                    </th>
                                </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                                {units.map((unit) => (
                                    <tr key={unit.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <div className="text-sm font-medium text-gray-900">
                                                {unit.name}
                                            </div>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <div className="text-sm text-gray-900">
                                                {unit.symbol}
                                            </div>
                                        </td>
                                        <td className="px-6 py-4">
                                            <div className="text-sm text-gray-500">
                                                {unit.description || t('common.notApplicable')}
                                            </div>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <span className={`px-2 inline-flex text-xs leading-5 font-semibold rounded-full ${unit.isActive
                                                    ? 'bg-green-100 text-green-800'
                                                    : 'bg-red-100 text-red-800'
                                                }`}>
                                                {unit.isActive ? t('common.active') : t('common.inactive')}
                                            </span>
                                        </td>
                                        <td className={`px-6 py-4 whitespace-nowrap text-sm font-medium ${actionAlignClass}`}>
                                            <button
                                                onClick={() => handleEdit(unit)}
                                                className={`text-primary-600 hover:text-primary-900 ${actionSpacingClass}`}
                                            >
                                                {t('common.edit')}
                                            </button>
                                            <button
                                                onClick={() => handleDelete(unit.id)}
                                                className="text-red-600 hover:text-red-900"
                                            >
                                                {t('common.delete')}
                                            </button>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>

            {/* Add/Edit Modal */}
            {showModal && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4">
                    <div className="bg-white rounded-lg shadow-xl max-w-md w-full">
                        <div className="p-6">
                            <div className="flex justify-between items-center mb-6">
                                <h3 className="text-xl font-bold">
                                    {editingUnit ? t('units.editUnit') : t('units.addNewUnit')}
                                </h3>
                                <button
                                    onClick={() => setShowModal(false)}
                                    className="text-gray-400 hover:text-gray-600"
                                >
                                    <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                                    </svg>
                                </button>
                            </div>

                            <form onSubmit={handleSubmit} className="space-y-4">
                                <div>
                                    <label className="label">{t('units.name')} *</label>
                                    <input
                                        type="text"
                                        value={formData.name}
                                        onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                                        className="input w-full"
                                        required
                                        placeholder={t('units.namePlaceholder')}
                                    />
                                </div>

                                <div>
                                    <label className="label">{t('units.symbol')} *</label>
                                    <input
                                        type="text"
                                        value={formData.symbol}
                                        onChange={(e) => setFormData({ ...formData, symbol: e.target.value })}
                                        className="input w-full"
                                        required
                                        placeholder={t('units.symbolPlaceholder')}
                                    />
                                </div>

                                <div>
                                    <label className="label">{t('units.description')}</label>
                                    <textarea
                                        value={formData.description}
                                        onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                                        className="input w-full"
                                        rows="3"
                                        placeholder={t('units.descriptionPlaceholder')}
                                    />
                                </div>

                                <div className="flex items-center">
                                    <input
                                        type="checkbox"
                                        checked={formData.isActive}
                                        onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                                        className="rounded"
                                    />
                                    <label className={`${checkboxLabelSpacingClass} text-sm text-gray-700`}>
                                        {t('units.isActive')}
                                    </label>
                                </div>

                                <div className="flex gap-3 pt-4">
                                    <button
                                        type="button"
                                        onClick={() => setShowModal(false)}
                                        className="btn-secondary flex-1"
                                    >
                                        {t('common.cancel')}
                                    </button>
                                    <button
                                        type="submit"
                                        className="btn-primary flex-1"
                                    >
                                        {editingUnit ? t('common.update') : t('common.create')}
                                    </button>
                                </div>
                            </form>
                        </div>
                    </div>
                </div>
            )}
        </div>
    )
}

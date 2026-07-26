import { useState, useEffect } from 'react'
import api from '../services/api'
import { useToast } from '../components/Toast'
import { useTranslation } from 'react-i18next'
import { useRTL } from '../hooks/useRTL'

export default function Warehouses() {
    const { t } = useTranslation()
    const { isRTL, dir } = useRTL()
    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const actionAlignClass = isRTL ? 'text-left' : 'text-right'
    const badgeSpacingClass = isRTL ? 'mr-2' : 'ml-2'
    const actionSpacingClass = isRTL ? 'ml-3' : 'mr-3'
    const [warehouses, setWarehouses] = useState([])
    const [loading, setLoading] = useState(true)
    const [showModal, setShowModal] = useState(false)
    const [editingWarehouse, setEditingWarehouse] = useState(null)
    const [formData, setFormData] = useState({
        name: '',
        code: '',
        address: '',
        phone: '',
        isActive: true,
        isDefault: false
    })

    const { showSuccess, showError } = useToast()

    useEffect(() => {
        fetchWarehouses()
    }, [])

    const fetchWarehouses = async () => {
        setLoading(true)
        try {
            const response = await api.get('/warehouses')
            setWarehouses(response.data)
        } catch (error) {
            console.error('Error fetching warehouses:', error)
            showError(t('warehouses.failedToLoad'))
        } finally {
            setLoading(false)
        }
    }

    const handleEdit = (warehouse) => {
        setEditingWarehouse(warehouse)
        setFormData({
            name: warehouse.name,
            code: warehouse.code || '',
            address: warehouse.address || '',
            phone: warehouse.phone || '',
            isActive: warehouse.isActive,
            isDefault: warehouse.isDefault
        })
        setShowModal(true)
    }

    const handleAdd = () => {
        setEditingWarehouse(null)
        setFormData({
            name: '',
            code: '',
            address: '',
            phone: '',
            isActive: true,
            isDefault: false
        })
        setShowModal(true)
    }

    const handleSubmit = async (e) => {
        e.preventDefault()
        try {
            if (editingWarehouse) {
                await api.put(`/warehouses/${editingWarehouse.id}`, { ...formData, id: editingWarehouse.id })
                showSuccess(t('warehouses.updated'))
            } else {
                await api.post('/warehouses', formData)
                showSuccess(t('warehouses.created'))
            }
            setShowModal(false)
            fetchWarehouses()
        } catch (error) {
            console.error('Error saving warehouse:', error)
            showError(t('warehouses.failedToSave'))
        }
    }

    const handleToggleActive = async (warehouse) => {
        try {
            // Assuming the backend supports partial update or we just send the full object with toggled IsActive
            // Or we can use the PUT endpoint.
            const updatedWarehouse = { ...warehouse, isActive: !warehouse.isActive }
            await api.put(`/warehouses/${warehouse.id}`, updatedWarehouse)
            showSuccess(t('warehouses.statusUpdated'))
            fetchWarehouses()
        } catch (error) {
            console.error('Error updating warehouse status:', error)
            showError(t('warehouses.failedToSave'))
        }
    }

    return (
        <div dir={dir} className="h-full flex flex-col">
            <div className="mb-6 flex justify-between items-center">
                <h1 className="text-2xl font-bold text-gray-800">{t('warehouses.title')}</h1>
                <button onClick={handleAdd} className="btn-primary">
                    + {t('warehouses.addWarehouse')}
                </button>
            </div>

            {/* Warehouses Table */}
            <div className="bg-white rounded-lg shadow flex-1 overflow-hidden flex flex-col">
                <div className="overflow-x-auto flex-1">
                    <table className="w-full">
                        <thead className="bg-gray-50 border-b border-gray-200">
                            <tr>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>{t('warehouses.nameCode')}</th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>{t('warehouses.contact')}</th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>{t('warehouses.status')}</th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${actionAlignClass}`}>{t('warehouses.actions')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {loading ? (
                                <tr>
                                    <td colSpan="4" className="px-6 py-4 text-center text-gray-500">{t('warehouses.loading')}</td>
                                </tr>
                            ) : warehouses.length === 0 ? (
                                <tr>
                                    <td colSpan="4" className="px-6 py-4 text-center text-gray-500">{t('warehouses.noWarehouses')}</td>
                                </tr>
                            ) : (
                                warehouses.map((warehouse) => (
                                    <tr key={warehouse.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <div className="flex items-center">
                                                <div>
                                                    <div className="text-sm font-medium text-gray-900">
                                                        {warehouse.name}
                                                        {warehouse.isDefault && (
                                                            <span className={`px-2 inline-flex text-xs leading-5 font-semibold rounded-full bg-blue-100 text-blue-800 ${badgeSpacingClass}`}>
                                                                {t('warehouses.default')}
                                                            </span>
                                                        )}
                                                    </div>
                                                    <div className="text-xs text-gray-500">{warehouse.code}</div>
                                                </div>
                                            </div>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <div className="text-sm text-gray-900">{warehouse.address}</div>
                                            <div className="text-sm text-gray-500">{warehouse.phone}</div>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <span className={`px-2 inline-flex text-xs leading-5 font-semibold rounded-full ${warehouse.isActive ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'
                                                }`}>
                                                {warehouse.isActive ? t('common.active') : t('common.inactive')}
                                            </span>
                                        </td>
                                        <td className={`px-6 py-4 whitespace-nowrap text-sm font-medium ${actionAlignClass}`}>
                                            <button
                                                onClick={() => handleEdit(warehouse)}
                                                className={`text-primary-600 hover:text-primary-900 ${actionSpacingClass}`}
                                            >
                                                {t('common.edit')}
                                            </button>
                                            <button
                                                onClick={() => handleToggleActive(warehouse)}
                                                className={`${warehouse.isActive ? 'text-red-600 hover:text-red-900' : 'text-green-600 hover:text-green-900'}`}
                                            >
                                                {warehouse.isActive ? t('warehouses.deactivate') : t('warehouses.activate')}
                                            </button>
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>
            </div>

            {/* Create/Edit Modal */}
            {showModal && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4 p-6">
                        <h2 className="text-xl font-bold mb-4">
                            {editingWarehouse ? t('warehouses.editWarehouse') : t('warehouses.newWarehouse')}
                        </h2>
                        <form onSubmit={handleSubmit} className="space-y-4">
                            <div>
                                <label className="label">{t('common.name')} *</label>
                                <input
                                    type="text"
                                    value={formData.name}
                                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                                    className="input w-full"
                                    required
                                />
                            </div>
                            <div>
                                <label className="label">{t('warehouses.code')}</label>
                                <input
                                    type="text"
                                    value={formData.code}
                                    onChange={(e) => setFormData({ ...formData, code: e.target.value })}
                                    className="input w-full"
                                />
                            </div>
                            <div>
                                <label className="label">{t('warehouses.address')}</label>
                                <input
                                    type="text"
                                    value={formData.address}
                                    onChange={(e) => setFormData({ ...formData, address: e.target.value })}
                                    className="input w-full"
                                />
                            </div>
                            <div>
                                <label className="label">{t('warehouses.phone')}</label>
                                <input
                                    type="text"
                                    value={formData.phone}
                                    onChange={(e) => setFormData({ ...formData, phone: e.target.value })}
                                    className="input w-full"
                                />
                            </div>
                            <div className="flex items-center gap-4">
                                <label className="flex items-center gap-2 cursor-pointer">
                                    <input
                                        type="checkbox"
                                        checked={formData.isActive}
                                        onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                                        className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                                    />
                                    <span className="text-sm text-gray-700">{t('warehouses.active')}</span>
                                </label>
                                <label className="flex items-center gap-2 cursor-pointer">
                                    <input
                                        type="checkbox"
                                        checked={formData.isDefault}
                                        onChange={(e) => setFormData({ ...formData, isDefault: e.target.checked })}
                                        className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                                    />
                                    <span className="text-sm text-gray-700">{t('warehouses.defaultWarehouse')}</span>
                                </label>
                            </div>
                            <div className="flex justify-end gap-3 mt-6">
                                <button
                                    type="button"
                                    onClick={() => setShowModal(false)}
                                    className="btn-secondary"
                                >
                                    {t('warehouses.cancel')}
                                </button>
                                <button type="submit" className="btn-primary">
                                    {t('warehouses.save')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    )
}

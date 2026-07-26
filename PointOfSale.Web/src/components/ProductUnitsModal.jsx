import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import api from '../services/api'

export default function ProductUnitsModal({ productId, productName, onClose }) {
    const { t } = useTranslation()
    const { confirm } = useConfirmDialog()
    const { formatCurrency } = useSettings()
    const [units, setUnits] = useState([])
    const [productUnits, setProductUnits] = useState([])
    const [loading, setLoading] = useState(true)
    const [saving, setSaving] = useState(false)
    const [error, setError] = useState('')
    const [showAddForm, setShowAddForm] = useState(false)

    const [formData, setFormData] = useState({
        unitId: '',
        conversionFactor: '1',
        purchasePrice: '',
        sellingPrice: '',
        barcode: '',
        isDefault: false,
        isBaseUnit: false
    })

    useEffect(() => {
        fetchUnits()
        fetchProductUnits()
    }, [productId])

    const fetchUnits = async () => {
        try {
            const response = await api.get('/units')
            setUnits(response.data)
        } catch (err) {
            console.error('Failed to load units', err)
        }
    }

    const fetchProductUnits = async () => {
        try {
            setLoading(true)
            const response = await api.get(`/products/${productId}/units`)
            setProductUnits(response.data)
        } catch (err) {
            setError(t('units.failedToLoad'))
            console.error(err)
        } finally {
            setLoading(false)
        }
    }

    const handleAdd = () => {
        setFormData({
            unitId: '',
            conversionFactor: '1',
            purchasePrice: '',
            sellingPrice: '',
            barcode: '',
            isDefault: false,
            isBaseUnit: false
        })
        setShowAddForm(true)
    }

    const handleSave = async (e) => {
        e.preventDefault()
        setSaving(true)
        setError('')

        try {
            const data = {
                unitId: formData.unitId,
                conversionFactor: parseFloat(formData.conversionFactor),
                purchasePrice: formData.purchasePrice ? parseFloat(formData.purchasePrice) : null,
                sellingPrice: formData.sellingPrice ? parseFloat(formData.sellingPrice) : null,
                barcode: formData.barcode || null,
                isDefault: formData.isDefault,
                isBaseUnit: formData.isBaseUnit
            }

            await api.post(`/products/${productId}/units`, data)
            await fetchProductUnits()
            setShowAddForm(false)
        } catch (err) {
            setError(err.response?.data?.message || t('units.failedToSave'))
        } finally {
            setSaving(false)
        }
    }

    const handleDelete = async (unitId) => {
        const confirmed = await confirm({
            confirmLabel: t('common.delete'),
            description: t('units.deleteConfirm'),
            title: t('common.warning'),
            variant: 'danger',
        })

        if (!confirmed) return

        try {
            await api.delete(`/products/${productId}/units/${unitId}`)
            await fetchProductUnits()
        } catch (err) {
            setError(err.response?.data?.message || t('units.failedToDelete'))
        }
    }

    const handleSetDefault = async (unitId) => {
        try {
            const unit = productUnits.find(u => u.unitId === unitId)
            if (!unit) return

            await api.put(`/products/${productId}/units/${unitId}`, {
                ...unit,
                isDefault: true
            })
            await fetchProductUnits()
        } catch (err) {
            setError(err.response?.data?.message || t('units.failedToUpdate'))
        }
    }

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4">
            <div className="bg-white rounded-lg shadow-xl max-w-4xl w-full max-h-[90vh] overflow-y-auto">
                <div className="p-6">
                    {/* Header */}
                    <div className="flex justify-between items-center mb-6">
                        <div>
                            <h3 className="text-2xl font-bold">{t('units.manageUnits')}</h3>
                            <p className="text-sm text-gray-600 mt-1">{productName}</p>
                        </div>
                        <button
                            onClick={onClose}
                            className="text-gray-400 hover:text-gray-600"
                        >
                            <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    </div>

                    {error && (
                        <div className="mb-4 p-3 bg-red-100 border border-red-400 text-red-700 rounded">
                            {error}
                        </div>
                    )}

                    {/* Add Unit Button */}
                    {!showAddForm && (
                        <button
                            onClick={handleAdd}
                            className="btn-primary mb-4"
                        >
                            + {t('units.addUnit')}
                        </button>
                    )}

                    {/* Add Unit Form */}
                    {showAddForm && (
                        <div className="card mb-6">
                            <h4 className="text-lg font-semibold mb-4">{t('units.addNewUnit')}</h4>
                            <form onSubmit={handleSave} className="space-y-4">
                                <div className="grid grid-cols-2 gap-4">
                                    <div>
                                        <label className="label">{t('units.unit')} *</label>
                                        <select
                                            value={formData.unitId}
                                            onChange={(e) => setFormData({ ...formData, unitId: e.target.value })}
                                            className="input"
                                            required
                                        >
                                            <option value="">{t('units.selectUnit')}</option>
                                            {units.filter(u => !productUnits.some(pu => pu.unitId === u.id)).map(unit => (
                                                <option key={unit.id} value={unit.id}>
                                                    {unit.name} ({unit.symbol})
                                                </option>
                                            ))}
                                        </select>
                                    </div>
                                    <div>
                                        <label className="label">{t('units.conversionFactor')} *</label>
                                        <input
                                            type="number"
                                            step="0.001"
                                            value={formData.conversionFactor}
                                            onChange={(e) => setFormData({ ...formData, conversionFactor: e.target.value })}
                                            className="input"
                                            required
                                            placeholder="1"
                                        />
                                        <p className="text-xs text-gray-500 mt-1">
                                            {t('units.conversionHelp')}
                                        </p>
                                    </div>
                                    <div>
                                        <label className="label">{t('units.purchasePrice')}</label>
                                        <input
                                            type="number"
                                            step="0.01"
                                            value={formData.purchasePrice}
                                            onChange={(e) => setFormData({ ...formData, purchasePrice: e.target.value })}
                                            className="input"
                                            placeholder={t('units.optional')}
                                        />
                                    </div>
                                    <div>
                                        <label className="label">{t('units.sellingPrice')}</label>
                                        <input
                                            type="number"
                                            step="0.01"
                                            value={formData.sellingPrice}
                                            onChange={(e) => setFormData({ ...formData, sellingPrice: e.target.value })}
                                            className="input"
                                            placeholder={t('units.optional')}
                                        />
                                    </div>
                                    <div>
                                        <label className="label">{t('units.barcode')}</label>
                                        <input
                                            type="text"
                                            value={formData.barcode}
                                            onChange={(e) => setFormData({ ...formData, barcode: e.target.value })}
                                            className="input"
                                            placeholder={t('units.optional')}
                                        />
                                    </div>
                                    <div className="flex items-center gap-4">
                                        <label className="flex items-center gap-2">
                                            <input
                                                type="checkbox"
                                                checked={formData.isDefault}
                                                onChange={(e) => setFormData({ ...formData, isDefault: e.target.checked })}
                                                className="rounded"
                                            />
                                            <span className="text-sm">{t('units.setAsDefault')}</span>
                                        </label>
                                        <label className="flex items-center gap-2">
                                            <input
                                                type="checkbox"
                                                checked={formData.isBaseUnit}
                                                onChange={(e) => setFormData({ ...formData, isBaseUnit: e.target.checked, conversionFactor: e.target.checked ? '1' : formData.conversionFactor })}
                                                className="rounded"
                                            />
                                            <span className="text-sm">{t('units.baseUnit')}</span>
                                        </label>
                                    </div>
                                </div>
                                <div className="flex gap-3">
                                    <button
                                        type="button"
                                        onClick={() => setShowAddForm(false)}
                                        className="btn-secondary"
                                        disabled={saving}
                                    >
                                        {t('common.cancel')}
                                    </button>
                                    <button
                                        type="submit"
                                        className="btn-primary"
                                        disabled={saving}
                                    >
                                        {saving ? t('common.saving') : t('common.save')}
                                    </button>
                                </div>
                            </form>
                        </div>
                    )}

                    {/* Units List */}
                    <div className="card">
                        {loading ? (
                            <p className="text-center py-8 text-gray-500">{t('common.loading')}</p>
                        ) : productUnits.length === 0 ? (
                            <p className="text-center py-8 text-gray-500">{t('units.noUnits')}</p>
                        ) : (
                            <div className="overflow-x-auto">
                                <table className="min-w-full divide-y divide-gray-200">
                                    <thead className="bg-gray-50">
                                        <tr>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('units.unit')}
                                            </th>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('units.conversionFactor')}
                                            </th>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('units.purchasePrice')}
                                            </th>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('units.sellingPrice')}
                                            </th>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('units.status')}
                                            </th>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('common.actions')}
                                            </th>
                                        </tr>
                                    </thead>
                                    <tbody className="bg-white divide-y divide-gray-200">
                                        {productUnits.map((unit) => (
                                            <tr key={unit.id} className="hover:bg-gray-50">
                                                <td className="px-6 py-4 whitespace-nowrap">
                                                    <div className="text-sm font-medium text-gray-900">
                                                        {unit.unitName}
                                                    </div>
                                                    <div className="text-sm text-gray-500">
                                                        {unit.unitSymbol}
                                                    </div>
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                    1 {unit.unitName} = {unit.conversionFactor} {t('units.baseUnits')}
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                    {unit.purchasePrice ? formatCurrency(unit.purchasePrice) : '-'}
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                    {unit.sellingPrice ? formatCurrency(unit.sellingPrice) : '-'}
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap">
                                                    <div className="flex gap-2">
                                                        {unit.isBaseUnit && (
                                                            <span className="px-2 inline-flex text-xs leading-5 font-semibold rounded-full bg-blue-100 text-blue-800">
                                                                {t('units.base')}
                                                            </span>
                                                        )}
                                                        {unit.isDefault && (
                                                            <span className="px-2 inline-flex text-xs leading-5 font-semibold rounded-full bg-green-100 text-green-800">
                                                                {t('units.default')}
                                                            </span>
                                                        )}
                                                    </div>
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm font-medium">
                                                    {!unit.isDefault && (
                                                        <button
                                                            onClick={() => handleSetDefault(unit.unitId)}
                                                            className="text-primary-600 hover:text-primary-900 mr-4"
                                                        >
                                                            {t('units.setDefault')}
                                                        </button>
                                                    )}
                                                    {!unit.isBaseUnit && (
                                                        <button
                                                            onClick={() => handleDelete(unit.unitId)}
                                                            className="text-red-600 hover:text-red-900"
                                                        >
                                                            {t('common.delete')}
                                                        </button>
                                                    )}
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        )}
                    </div>

                    {/* Close Button */}
                    <div className="mt-6">
                        <button
                            onClick={onClose}
                            className="btn-secondary w-full"
                        >
                            {t('common.close')}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    )
}

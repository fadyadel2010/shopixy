import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import api from '../services/api'
import { useToast } from '../components/Toast'

const ExpenseCategories = () => {
    const { t } = useTranslation()
    const { confirm } = useConfirmDialog()
    const { showSuccess, showError } = useToast()
    const [categories, setCategories] = useState([])
    const [loading, setLoading] = useState(false)
    const [showModal, setShowModal] = useState(false)
    const [editingCategory, setEditingCategory] = useState(null)
    const [formData, setFormData] = useState({
        name: '',
        description: '',
        isActive: true
    })

    useEffect(() => {
        loadCategories()
    }, [])

    const loadCategories = async () => {
        setLoading(true)
        try {
            const response = await api.get('/expensecategories')
            setCategories(response.data)
        } catch (error) {
            console.error('Error loading categories:', error)
            showError(t('expenses.failedToLoadCategories'))
        } finally {
            setLoading(false)
        }
    }

    const handleSubmit = async (e) => {
        e.preventDefault()
        setLoading(true)
        try {
            if (editingCategory) {
                await api.put(`/expensecategories/${editingCategory.id}`, formData)
                showSuccess(t('expenses.categoryUpdated'))
            } else {
                await api.post('/expensecategories', formData)
                showSuccess(t('expenses.categoryCreated'))
            }
            setShowModal(false)
            resetForm()
            loadCategories()
        } catch (error) {
            console.error('Error saving category:', error)
            showError(error.response?.data?.message || t('expenses.failedToSave'))
        } finally {
            setLoading(false)
        }
    }

    const handleEdit = (category) => {
        setEditingCategory(category)
        setFormData({
            name: category.name,
            description: category.description || '',
            isActive: category.isActive
        })
        setShowModal(true)
    }

    const handleDelete = async (id) => {
        const confirmed = await confirm({
            confirmLabel: t('common.delete'),
            description: t('expenses.deleteCategoryConfirm'),
            title: t('common.warning'),
            variant: 'danger',
        })

        if (!confirmed) return

        try {
            await api.delete(`/expensecategories/${id}`)
            showSuccess(t('expenses.categoryDeleted'))
            loadCategories()
        } catch (error) {
            console.error('Error deleting category:', error)
            showError(error.response?.data?.message || t('expenses.failedToDelete'))
        }
    }

    const handleToggleStatus = async (id) => {
        try {
            await api.patch(`/expensecategories/${id}/toggle-status`)
            loadCategories()
        } catch (error) {
            console.error('Error toggling status:', error)
            showError(t('common.error'))
        }
    }

    const resetForm = () => {
        setFormData({
            name: '',
            description: '',
            isActive: true
        })
        setEditingCategory(null)
    }

    const handleCloseModal = () => {
        setShowModal(false)
        resetForm()
    }

    return (
        <div className="space-y-6">
            {/* Header */}
            <div className="flex justify-between items-center">
                <h1 className="text-2xl font-bold text-gray-900">{t('expenses.categories')}</h1>
                <button
                    onClick={() => setShowModal(true)}
                    className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700"
                >
                    {t('expenses.addCategory')}
                </button>
            </div>

            {/* Categories Table */}
            <div className="bg-white rounded-lg shadow overflow-hidden">
                {loading && categories.length === 0 ? (
                    <div className="p-12 text-center text-gray-500">
                        {t('common.loading')}
                    </div>
                ) : categories.length === 0 ? (
                    <div className="p-12 text-center text-gray-500">
                        {t('expenses.noCategories')}
                    </div>
                ) : (
                    <table className="min-w-full divide-y divide-gray-200">
                        <thead className="bg-gray-50">
                            <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('expenses.categoryName')}
                                </th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('expenses.categoryDescription')}
                                </th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('common.status')}
                                </th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('common.actions')}
                                </th>
                            </tr>
                        </thead>
                        <tbody className="bg-white divide-y divide-gray-200">
                            {categories.map((category) => (
                                <tr key={category.id} className="hover:bg-gray-50">
                                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                        {category.name}
                                    </td>
                                    <td className="px-6 py-4 text-sm text-gray-500">
                                        {category.description || '-'}
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap">
                                        <button
                                            onClick={() => handleToggleStatus(category.id)}
                                            className={`px-2 py-1 text-xs font-medium rounded-full ${category.isActive
                                                    ? 'bg-green-100 text-green-800'
                                                    : 'bg-gray-100 text-gray-800'
                                                }`}
                                        >
                                            {category.isActive ? t('common.active') : t('common.inactive')}
                                        </button>
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                                        <button
                                            onClick={() => handleEdit(category)}
                                            className="text-blue-600 hover:text-blue-900 mr-4"
                                        >
                                            {t('common.edit')}
                                        </button>
                                        <button
                                            onClick={() => handleDelete(category.id)}
                                            className="text-red-600 hover:text-red-900"
                                        >
                                            {t('common.delete')}
                                        </button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </div>

            {/* Modal */}
            {showModal && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-lg p-6 w-full max-w-md">
                        <h2 className="text-xl font-bold text-gray-900 mb-4">
                            {editingCategory ? t('expenses.editCategory') : t('expenses.newCategory')}
                        </h2>
                        <form onSubmit={handleSubmit} className="space-y-4">
                            <div>
                                <label className="block text-sm font-medium text-gray-700 mb-2">
                                    {t('expenses.categoryName')} *
                                </label>
                                <input
                                    type="text"
                                    value={formData.name}
                                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                    required
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium text-gray-700 mb-2">
                                    {t('expenses.categoryDescription')}
                                </label>
                                <textarea
                                    value={formData.description}
                                    onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                    rows="3"
                                />
                            </div>
                            <div className="flex items-center">
                                <input
                                    type="checkbox"
                                    checked={formData.isActive}
                                    onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                                    className="w-4 h-4 text-blue-600 rounded focus:ring-blue-500"
                                />
                                <label className="ml-2 text-sm text-gray-700">
                                    {t('expenses.isActive')}
                                </label>
                            </div>
                            <div className="flex justify-end gap-2 mt-6">
                                <button
                                    type="button"
                                    onClick={handleCloseModal}
                                    className="px-4 py-2 border border-gray-300 rounded-lg text-gray-700 hover:bg-gray-50"
                                >
                                    {t('common.cancel')}
                                </button>
                                <button
                                    type="submit"
                                    disabled={loading}
                                    className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-gray-400"
                                >
                                    {loading ? t('common.saving') : t('common.save')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    )
}

export default ExpenseCategories

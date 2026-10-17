import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import { useRTL } from '../hooks/useRTL'
import { useHasPermission } from '../hooks/usePosSessionPermission'

export default function Categories() {
    const { t } = useTranslation()
    const { isRTL, dir } = useRTL()
    const { confirm } = useConfirmDialog()
    const canManageCatalog = useHasPermission('catalog.manage')
    const canDeleteCatalog = useHasPermission('catalog.delete')
    const canPublishCatalog = useHasPermission('catalog.publish')
    const [categories, setCategories] = useState([])
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState('')
    const [showModal, setShowModal] = useState(false)
    const [editingCategory, setEditingCategory] = useState(null)
    const [expandedCategories, setExpandedCategories] = useState({})
    const [syncing, setSyncing] = useState(false)
    const [syncMessage, setSyncMessage] = useState('')
    const [formData, setFormData] = useState({
        name: '',
        description: '',
        parentCategoryId: null,
    })

    useEffect(() => {
        fetchCategories()
    }, [])

    useEffect(() => {
        if (categories.length > 0) {
            const expanded = {}
            categories.forEach((category) => {
                if (category.children && category.children.length > 0) {
                    expanded[category.id] = true
                }
            })
            setExpandedCategories(expanded)
        }
    }, [categories])

    const fetchCategories = async () => {
        try {
            setLoading(true)
            const response = await api.get('/categories/tree')
            setCategories(response.data)
        } catch (err) {
            setError(t('categories.failedToLoad'))
            console.error(err)
        } finally {
            setLoading(false)
        }
    }

    const syncCategories = async () => {
        try {
            setSyncing(true)
            setSyncMessage('')
            setError('')
            const response = await api.post('/admin/sync/categories')
            setSyncMessage(t('categories.syncSuccess', { count: response.data.count }))
            setTimeout(() => setSyncMessage(''), 5000)
        } catch (err) {
            console.error('Sync failed:', err)
            setError(err.response?.data?.error || t('categories.syncFailed'))
        } finally {
            setSyncing(false)
        }
    }

    const handleSubmit = async (e) => {
        e.preventDefault()
        try {
            if (editingCategory) {
                await api.put(`/categories/${editingCategory.id}`, formData)
            } else {
                await api.post('/categories', formData)
            }
            setShowModal(false)
            resetForm()
            fetchCategories()
        } catch (err) {
            setError(err.response?.data?.message || t('categories.failedToSave'))
        }
    }

    const handleEdit = (category) => {
        setEditingCategory(category)
        setFormData({
            name: category.name,
            description: category.description || '',
            parentCategoryId: category.parentCategoryId || null,
        })
        setShowModal(true)
    }

    const handleDelete = async (id) => {
        const confirmed = await confirm({
            confirmLabel: t('common.delete'),
            description: t('categories.deleteConfirm'),
            title: t('common.warning'),
            variant: 'danger',
        })

        if (!confirmed) return

        try {
            await api.delete(`/categories/${id}`)
            fetchCategories()
        } catch (err) {
            setError(err.response?.data?.message || t('categories.failedToDelete'))
        }
    }

    const resetForm = () => {
        setFormData({ name: '', description: '', parentCategoryId: null })
        setEditingCategory(null)
    }

    const toggleCategory = (categoryId) => {
        setExpandedCategories((prev) => ({
            ...prev,
            [categoryId]: !prev[categoryId],
        }))
    }

    const expandAll = () => {
        const expanded = {}
        categories.forEach((category) => {
            if (category.children && category.children.length > 0) {
                expanded[category.id] = true
            }
        })
        setExpandedCategories(expanded)
    }

    const collapseAll = () => {
        setExpandedCategories({})
    }

    const hasExpandableCategories = categories.some((category) => category.children && category.children.length > 0)
    const expandCollapseSpacingClass = isRTL ? 'ml-2' : 'mr-2'
    const nestedCategoryClass = isRTL ? 'mr-6 border-r-2 border-primary-200' : 'ml-6 border-l-2 border-primary-200'

    const renderCategory = (category, level = 0) => {
        const hasChildren = category.children && category.children.length > 0
        const isExpanded = expandedCategories[category.id]

        return (
            <div key={category.id}>
                <div
                    className={`flex items-center justify-between border-b border-gray-100 p-4 transition-colors hover:bg-gray-50 ${
                        level === 0 ? 'bg-white' : `bg-gray-50 ${nestedCategoryClass}`
                    }`}
                >
                    <div className="flex items-center gap-3">
                        {level === 0 && hasChildren ? (
                            <button
                                onClick={() => toggleCategory(category.id)}
                                className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary-100 text-primary-700 transition-colors hover:bg-primary-200"
                                title={isExpanded ? t('categories.collapseAll') : t('categories.expandAll')}
                            >
                                <span className="text-lg font-bold">{isExpanded ? '-' : '+'}</span>
                            </button>
                        ) : (
                            <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-gray-100 text-sm font-semibold text-gray-500">
                                {level === 0 ? 'C' : 'S'}
                            </div>
                        )}

                        <div>
                            <div className="flex items-center gap-2 font-medium text-gray-900">
                                {category.name}
                                {level === 0 && hasChildren && (
                                    <span className="rounded-full bg-primary-100 px-2 py-0.5 text-xs text-primary-700">
                                        {t('categories.subCategoriesCount', { count: category.children.length })}
                                    </span>
                                )}
                            </div>
                            {category.description && (
                                <div className="text-sm text-gray-500">{category.description}</div>
                            )}
                            <div className="mt-1 text-xs text-gray-400">
                                {t('categories.productsCount', { count: category.productCount || 0 })}
                            </div>
                        </div>
                    </div>

                    {(canManageCatalog || canDeleteCatalog) && (
                    <div className="flex gap-2">
                        {canManageCatalog && level === 0 && (
                            <button
                                onClick={() => {
                                    setFormData({ name: '', description: '', parentCategoryId: category.id })
                                    setShowModal(true)
                                }}
                                className="rounded-lg bg-primary-50 px-3 py-1 text-sm text-primary-600 transition-colors hover:bg-primary-100"
                            >
                                {t('categories.addSubcategory')}
                            </button>
                        )}
                        {canManageCatalog && (
                            <button
                                onClick={() => handleEdit(category)}
                                className="rounded-lg bg-blue-50 px-3 py-1 text-sm text-blue-600 transition-colors hover:bg-blue-100"
                            >
                                {t('common.edit')}
                            </button>
                        )}
                        {canDeleteCatalog && (
                            <button
                                onClick={() => handleDelete(category.id)}
                                className="rounded-lg bg-red-50 px-3 py-1 text-sm text-red-600 transition-colors hover:bg-red-100"
                            >
                                {t('common.delete')}
                            </button>
                        )}
                    </div>
                    )}
                </div>

                {hasChildren && isExpanded && (
                    <div className="animate-fadeIn">
                        {category.children.map((child) => renderCategory(child, level + 1))}
                    </div>
                )}
            </div>
        )
    }

    return (
        <div dir={dir}>
            <div className="mb-6 flex items-center justify-between">
                <h2 className="text-2xl font-bold text-gray-900">{t('categories.title')}</h2>
                <div className="flex items-center gap-2">
                    {hasExpandableCategories && (
                        <div className={`flex gap-1 ${expandCollapseSpacingClass}`}>
                            <button
                                onClick={expandAll}
                                className="rounded-lg px-3 py-2 text-sm text-gray-600 transition-colors hover:bg-gray-100 hover:text-gray-900"
                                title={t('categories.expandAll')}
                            >
                                {t('categories.expandAll')}
                            </button>
                            <button
                                onClick={collapseAll}
                                className="rounded-lg px-3 py-2 text-sm text-gray-600 transition-colors hover:bg-gray-100 hover:text-gray-900"
                                title={t('categories.collapseAll')}
                            >
                                {t('categories.collapseAll')}
                            </button>
                        </div>
                    )}

                    {canPublishCatalog && (
                        <button
                            onClick={syncCategories}
                            disabled={syncing || categories.length === 0}
                            className="flex items-center gap-2 rounded-lg bg-emerald-50 px-4 py-2 text-sm font-medium text-emerald-700 transition-colors hover:bg-emerald-100 disabled:cursor-not-allowed disabled:opacity-50"
                            title={t('categories.syncTitle')}
                        >
                            {syncing ? t('common.syncing') : t('categories.syncToStorefront')}
                        </button>
                    )}

                    {canManageCatalog && (
                        <button
                            onClick={() => {
                                resetForm()
                                setShowModal(true)
                            }}
                            className="btn-primary"
                        >
                            + {t('categories.addCategory')}
                        </button>
                    )}
                </div>
            </div>

            {syncMessage && (
                <div className="mb-4 flex items-center gap-2 rounded border border-emerald-400 bg-emerald-100 p-3 text-emerald-700">
                    {syncMessage}
                </div>
            )}

            {error && (
                <div className="mb-4 rounded border border-red-400 bg-red-100 p-3 text-red-700">
                    {error}
                </div>
            )}

            <div className="card overflow-hidden">
                {loading ? (
                    <p className="py-8 text-center text-gray-500">{t('categories.loading')}</p>
                ) : categories.length === 0 ? (
                    <p className="py-8 text-center text-gray-500">{t('categories.noCategories')}</p>
                ) : (
                    <div className="divide-y divide-gray-200">
                        {categories.map((category) => renderCategory(category))}
                    </div>
                )}
            </div>

            {canManageCatalog && showModal && (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-50">
                    <div className="mx-4 w-full max-w-md rounded-lg bg-white shadow-xl">
                        <div className="p-6">
                            <h3 className="mb-4 text-xl font-bold">
                                {editingCategory ? t('categories.editCategory') : t('categories.addCategory')}
                            </h3>
                            <form onSubmit={handleSubmit}>
                                <div className="mb-4">
                                    <label className="label">{t('categories.nameRequired')}</label>
                                    <input
                                        type="text"
                                        value={formData.name}
                                        onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                                        className="input"
                                        required
                                    />
                                </div>
                                <div className="mb-4">
                                    <label className="label">{t('categories.description')}</label>
                                    <textarea
                                        value={formData.description}
                                        onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                                        className="input"
                                        rows="3"
                                    />
                                </div>
                                {!editingCategory && formData.parentCategoryId && (
                                    <div className="mb-4 rounded border border-blue-200 bg-blue-50 p-3 text-sm text-blue-800">
                                        {t('categories.subCategoryHelp')}
                                    </div>
                                )}
                                <div className="flex gap-3">
                                    <button
                                        type="button"
                                        onClick={() => {
                                            setShowModal(false)
                                            resetForm()
                                        }}
                                        className="btn-secondary flex-1"
                                    >
                                        {t('common.cancel')}
                                    </button>
                                    <button type="submit" className="btn-primary flex-1">
                                        {editingCategory ? t('categories.update') : t('categories.create')}
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

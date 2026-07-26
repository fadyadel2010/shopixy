import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import api from '../services/api'
import {
    archiveAdminPromotion,
    createAdminPromotion,
    getAdminPromotion,
    getAdminPromotionStats,
    listAdminPromotions,
    toggleAdminPromotion,
    updateAdminPromotion,
} from '../services/adminPromotionsApi'
import { useAuthStore } from '../store/authStore'
import { useSettings } from '../contexts/SettingsContext'
import { useRTL } from '../hooks/useRTL'
import { formatDate } from '../utils/dateFormat'

/**
 * Promotions (Offers) Management Page
 * 
 * PROMOTIONS ENGINE ARCHITECTURE:
 * - Promotions (Offers) are auto-applied discounts without codes
 * - Coupons are code-based (managed in Coupons.jsx - UNCHANGED)
 * - PromotionEngine calculates best discount at checkout
 * 
 * This page allows POS admins to:
 * - Create product/category offers
 * - Set priority for conflict resolution
 * - Schedule promotions with start/end dates
 * - Toggle active status
 */
export default function Promotions() {
    const { t } = useTranslation()
    const { confirm } = useConfirmDialog()
    const { formatCurrency } = useSettings()
    const { dir, isRTL, locale } = useRTL()
    const user = useAuthStore((state) => state.user)
    const [promotions, setPromotions] = useState([])
    const [selectedPromotion, setSelectedPromotion] = useState(null)
    const [detailsLoading, setDetailsLoading] = useState(false)
    const [loading, setLoading] = useState(true)
    const [showModal, setShowModal] = useState(false)
    const [editingPromotion, setEditingPromotion] = useState(null)
    const [filter, setFilter] = useState({
        activeOnly: false,
        search: '',
        scope: '',
        sortBy: 'priority',
        sortDirection: 'desc',
        page: 1,
        pageSize: 50,
    })
    const [products, setProducts] = useState([])
    const [categories, setCategories] = useState([])
    const [stats, setStats] = useState(null)

    const [formData, setFormData] = useState({
        name: '',
        description: '',
        discountType: 'Percentage',
        discountValue: '',
        maxDiscountAmount: '',
        scope: 'Product',
        appliesToId: '',
        startDate: '',
        endDate: '',
        priority: 0,
        isActive: true,
        isExclusive: false,
        minOrderAmount: '',
        maxUsage: '',
        bannerStyle: 'gradient'
    })
    const [error, setError] = useState('')
    const [success, setSuccess] = useState('')

    useEffect(() => {
        fetchPromotions()
        fetchStats()
        fetchProducts()
        fetchCategories()
    }, [user])

    useEffect(() => {
        fetchPromotions()
    }, [filter, user])

    const fetchPromotions = async () => {
        if (!user?.tenantId || !(user?.id || user?.userId)) {
            setPromotions([])
            setLoading(false)
            return
        }

        try {
            setLoading(true)
            const admittedPromotions = await listAdminPromotions(filter, user)
            setPromotions(admittedPromotions)
        } catch (err) {
            setError(t('common.failedToLoad'))
        } finally {
            setLoading(false)
        }
    }

    const fetchStats = async () => {
        if (!user?.tenantId || !(user?.id || user?.userId)) {
            setStats(null)
            return
        }

        try {
            const admittedStats = await getAdminPromotionStats(user)
            setStats(admittedStats)
        } catch (err) {
            console.error('Failed to load stats', err)
        }
    }

    const handleSort = (sortBy) => {
        setFilter((current) => ({
            ...current,
            sortBy,
            sortDirection: current.sortBy === sortBy && current.sortDirection === 'asc' ? 'desc' : 'asc',
            page: 1,
        }))
    }

    const handleOpenDetails = async (promotionId) => {
        if (!user?.tenantId || !(user?.id || user?.userId)) {
            setSelectedPromotion(null)
            setError(t('common.failedToLoad'))
            return
        }

        try {
            setDetailsLoading(true)
            setError('')
            const admittedPromotion = await getAdminPromotion(promotionId, user)
            setSelectedPromotion(admittedPromotion)
        } catch (err) {
            setSelectedPromotion(null)
            setError(t('common.failedToLoad'))
        } finally {
            setDetailsLoading(false)
        }
    }

    const fetchProducts = async () => {
        try {
            const response = await api.get('/products?pageSize=100')
            setProducts(response.data.items || response.data)
        } catch (err) {
            console.error('Failed to load products', err)
        }
    }

    const fetchCategories = async () => {
        try {
            const response = await api.get('/categories')
            setCategories(response.data)
        } catch (err) {
            console.error('Failed to load categories', err)
        }
    }

    const handleSubmit = async (e) => {
        e.preventDefault()
        setError('')

        // Validation
        if (!formData.startDate || !formData.endDate) {
            setError(t('promotions.datesRequired'))
            return
        }

        if (new Date(formData.endDate) <= new Date(formData.startDate)) {
            setError(t('promotions.invalidDates'))
            return
        }

        try {
            const data = {
                ...formData,
                discountValue: parseFloat(formData.discountValue) || 0,
                maxDiscountAmount: formData.maxDiscountAmount ? parseFloat(formData.maxDiscountAmount) : null,
                minOrderAmount: formData.minOrderAmount ? parseFloat(formData.minOrderAmount) : null,
                maxUsage: formData.maxUsage ? parseInt(formData.maxUsage) : null,
                priority: parseInt(formData.priority) || 0,
                appliesToId: formData.appliesToId || null
            }

            if (editingPromotion) {
                await updateAdminPromotion(editingPromotion, data, user)
                setSuccess(t('common.updatedSuccessfully'))
            } else {
                await createAdminPromotion(data, user)
                setSuccess(t('promotions.created'))
            }

            setShowModal(false)
            fetchPromotions()
            fetchStats()
            resetForm()
        } catch (err) {
            setError(err.response?.data?.message || t('common.error'))
        }
    }

    const handleEdit = (promotion) => {
        setEditingPromotion(promotion)
        setFormData({
            name: promotion.name,
            description: promotion.description || '',
            discountType: promotion.discountType,
            discountValue: promotion.discountValue.toString(),
            maxDiscountAmount: promotion.maxDiscountAmount?.toString() || '',
            scope: promotion.scope,
            appliesToId: promotion.appliesToId || '',
            startDate: promotion.startDate ? promotion.startDate.split('T')[0] : '',
            endDate: promotion.endDate ? promotion.endDate.split('T')[0] : '',
            priority: promotion.priority.toString(),
            isActive: promotion.isActive,
            isExclusive: promotion.isExclusive,
            minOrderAmount: promotion.minOrderAmount?.toString() || '',
            maxUsage: promotion.maxUsage?.toString() || '',
            bannerStyle: promotion.bannerStyle || 'gradient'
        })
        setShowModal(true)
    }

    const handleDelete = async (promotion) => {
        const confirmed = await confirm({
            confirmLabel: t('common.delete'),
            description: t('common.deleteConfirm', { item: t('promotions.promotion') }),
            title: t('common.warning'),
            variant: 'danger',
        })

        if (!confirmed) return

        try {
            await archiveAdminPromotion(promotion)
            setSuccess(t('common.deletedSuccessfully'))
            fetchPromotions()
            fetchStats()
        } catch (err) {
            setError(err.response?.data?.message || t('common.deleteFailed'))
        }
    }

    const handleToggle = async (promotion) => {
        try {
            await toggleAdminPromotion(promotion, user)
            fetchPromotions()
            fetchStats()
        } catch (err) {
            setError(err.response?.data?.message || t('common.error'))
        }
    }

    const resetForm = () => {
        setEditingPromotion(null)
        setFormData({
            name: '',
            description: '',
            discountType: 'Percentage',
            discountValue: '',
            maxDiscountAmount: '',
            scope: 'Product',
            appliesToId: '',
            startDate: '',
            endDate: '',
            priority: 0,
            isActive: true,
            isExclusive: false,
            minOrderAmount: '',
            maxUsage: '',
            bannerStyle: 'gradient'
        })
    }

    const openAddModal = () => {
        resetForm()
        setShowModal(true)
    }

    const formatDiscount = (promotion) => {
        if (promotion.discountType === 'Percentage' || promotion.discountType === 0) {
            return `${promotion.discountValue}%`
        }
        return formatCurrency(promotion.discountValue, promotion.currency)
    }

    const getScopeDisplay = (promotion) => {
        switch (promotion.scope) {
            case 'Product':
            case 0:
                return promotion.appliesToId
                    ? products.find(p => p.id === promotion.appliesToId)?.name || t('promotions.specificProduct')
                    : t('promotions.allProducts')
            case 'Category':
            case 1:
                return promotion.appliesToId
                    ? categories.find(c => c.id === promotion.appliesToId)?.name || t('promotions.specificCategory')
                    : t('promotions.allCategories')
            case 'Cart':
            case 2:
                return t('promotions.entireCart')
            default:
                return promotion.scope
        }
    }

    const getStatusBadge = (promotion) => {
        const status = promotion.status

        if (status === 'Inactive') {
            return <span className="px-2 py-1 text-xs font-medium rounded-full bg-gray-100 text-gray-600">
                {t('common.inactive')}
            </span>
        }
        if (status === 'Scheduled') {
            return <span className="px-2 py-1 text-xs font-medium rounded-full bg-blue-100 text-blue-600">
                {t('promotions.scheduled')}
            </span>
        }
        if (status === 'Expired') {
            return <span className="px-2 py-1 text-xs font-medium rounded-full bg-red-100 text-red-600">
                {t('promotions.expired')}
            </span>
        }
        return <span className="px-2 py-1 text-xs font-medium rounded-full bg-green-100 text-green-600">
            {t('common.active')}
        </span>
    }

    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'

    return (
        <div dir={dir} className="p-6">
            {/* Header */}
            <div className="mb-6">
                <h1 className="text-2xl font-bold text-gray-900">
                    {t('promotions.title')}
                </h1>
                <p className="text-gray-500 mt-1">
                    {t('promotions.subtitle')}
                </p>
            </div>

            {/* Stats Cards */}
            {stats && (
                <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-6">
                    <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-4">
                        <div className="text-sm text-gray-500">{t('promotions.totalPromotions')}</div>
                        <div className="text-2xl font-bold text-gray-900">{stats.totalPromotions}</div>
                    </div>
                    <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-4">
                        <div className="text-sm text-gray-500">{t('promotions.activeNow')}</div>
                        <div className="text-2xl font-bold text-green-600">{stats.activePromotions}</div>
                    </div>
                    <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-4">
                        <div className="text-sm text-gray-500">{t('promotions.scheduled')}</div>
                        <div className="text-2xl font-bold text-blue-600">{stats.scheduledPromotions}</div>
                    </div>
                    <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-4">
                        <div className="text-sm text-gray-500">{t('promotions.totalDiscountGiven')}</div>
                        <div className="text-2xl font-bold text-purple-600">{formatCurrency(stats.totalDiscountGiven, stats.currency)}</div>
                    </div>
                </div>
            )}

            {/* Alerts */}
            {error && (
                <div className="mb-4 p-4 bg-red-50 border border-red-200 text-red-600 rounded-lg">
                    {error}
                    <button onClick={() => setError('')} className="float-right font-bold">&times;</button>
                </div>
            )}
            {success && (
                <div className="mb-4 p-4 bg-green-50 border border-green-200 text-green-600 rounded-lg">
                    {success}
                    <button onClick={() => setSuccess('')} className="float-right font-bold">&times;</button>
                </div>
            )}

            {/* Filters and Actions */}
            <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-4 mb-6">
                <div className="flex flex-wrap gap-4 items-center justify-between">
                    <div className="flex gap-4 flex-wrap">
                        <input
                            type="text"
                            placeholder={t('common.search')}
                            className="px-4 py-2 border border-gray-200 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                            value={filter.search}
                            onChange={(e) => setFilter({ ...filter, search: e.target.value, page: 1 })}
                        />
                        <select
                            className="px-4 py-2 border border-gray-200 rounded-lg focus:ring-2 focus:ring-blue-500"
                            value={filter.scope}
                            onChange={(e) => setFilter({ ...filter, scope: e.target.value, page: 1 })}
                        >
                            <option value="">{t('promotions.allScopes')}</option>
                            <option value="Product">{t('promotions.scopeProduct')}</option>
                            <option value="Category">{t('promotions.scopeCategory')}</option>
                            <option value="Cart">{t('promotions.scopeCart')}</option>
                        </select>
                        <label className="flex items-center gap-2">
                            <input
                                type="checkbox"
                                checked={filter.activeOnly}
                                onChange={(e) => setFilter({ ...filter, activeOnly: e.target.checked, page: 1 })}
                                className="w-4 h-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                            />
                            <span className="text-sm text-gray-600">{t('common.activeOnly')}</span>
                        </label>
                    </div>
                    <button
                        onClick={openAddModal}
                        className="px-4 py-2 bg-gradient-to-r from-blue-600 to-blue-700 text-white rounded-lg hover:from-blue-700 hover:to-blue-800 transition-all shadow-md"
                    >
                        + {t('promotions.addPromotion')}
                    </button>
                </div>
            </div>

            {/* Promotions Table */}
            <div className="bg-white rounded-xl shadow-sm border border-gray-100 overflow-hidden">
                {loading ? (
                    <div className="p-8 text-center text-gray-500">
                        <div className="animate-spin w-8 h-8 border-4 border-blue-500 border-t-transparent rounded-full mx-auto mb-4"></div>
                        {t('common.loading')}
                    </div>
                ) : promotions.length === 0 ? (
                    <div className="p-8 text-center text-gray-500">
                        <div className="text-4xl mb-2">🏷️</div>
                        {t('promotions.noPromotions')}
                    </div>
                ) : (
                    <table className="min-w-full">
                        <thead className="bg-gray-50 border-b border-gray-100">
                            <tr>
                                <th className={`px-6 py-4 text-xs font-semibold text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    <button type="button" onClick={() => handleSort('name')} className="font-semibold uppercase">
                                        {t('common.name')}
                                    </button>
                                </th>
                                <th className={`px-6 py-4 text-xs font-semibold text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    {t('common.discount')}
                                </th>
                                <th className={`px-6 py-4 text-xs font-semibold text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    {t('promotions.scope')}
                                </th>
                                <th className={`px-6 py-4 text-xs font-semibold text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    <button type="button" onClick={() => handleSort('priority')} className="font-semibold uppercase">
                                        {t('promotions.priority')}
                                    </button>
                                </th>
                                <th className={`px-6 py-4 text-xs font-semibold text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    <button type="button" onClick={() => handleSort('startDate')} className="font-semibold uppercase">
                                        {t('common.dates')}
                                    </button>
                                </th>
                                <th className={`px-6 py-4 text-xs font-semibold text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    {t('common.status')}
                                </th>
                                <th className={`px-6 py-4 text-xs font-semibold text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    {t('promotions.usage')}
                                </th>
                                <th className="px-6 py-4 text-center text-xs font-semibold text-gray-500 uppercase tracking-wider">
                                    {t('common.actions')}
                                </th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100">
                            {promotions.map(promotion => (
                                <tr key={promotion.id} className="hover:bg-gray-50 transition-colors">
                                    <td className="px-6 py-4">
                                        <div className="font-medium text-gray-900">{promotion.name}</div>
                                        {promotion.description && (
                                            <div className="text-sm text-gray-500 truncate max-w-xs">
                                                {promotion.description}
                                            </div>
                                        )}
                                    </td>
                                    <td className="px-6 py-4">
                                        <span className="px-3 py-1 bg-gradient-to-r from-purple-500 to-pink-500 text-white text-sm font-bold rounded-full">
                                            {formatDiscount(promotion)}
                                        </span>
                                    </td>
                                    <td className="px-6 py-4 text-sm text-gray-600">
                                        {getScopeDisplay(promotion)}
                                    </td>
                                    <td className="px-6 py-4">
                                        <span className="px-2 py-1 bg-gray-100 text-gray-700 text-sm rounded">
                                            {promotion.priority}
                                        </span>
                                    </td>
                                    <td className="px-6 py-4 text-sm text-gray-600">
                                        <div>{formatDate(promotion.startDate, locale)}</div>
                                        <div className="text-gray-400">→ {formatDate(promotion.endDate, locale)}</div>
                                    </td>
                                    <td className="px-6 py-4">
                                        {getStatusBadge(promotion)}
                                    </td>
                                    <td className="px-6 py-4 text-sm text-gray-600">
                                        {promotion.currentUsage}
                                        {promotion.maxUsage && <span className="text-gray-400">/{promotion.maxUsage}</span>}
                                    </td>
                                    <td className="px-6 py-4">
                                        <div className="flex justify-center gap-2">
                                            <button
                                                onClick={() => handleOpenDetails(promotion.id)}
                                                className="p-2 bg-gray-100 text-gray-600 rounded-lg hover:bg-gray-200 transition-colors"
                                                title={t('common.view')}
                                            >
                                                👁
                                            </button>
                                            <button
                                                onClick={() => handleToggle(promotion)}
                                                className={`p-2 rounded-lg transition-colors ${promotion.isActive
                                                    ? 'bg-green-100 text-green-600 hover:bg-green-200'
                                                    : 'bg-gray-100 text-gray-600 hover:bg-gray-200'
                                                    }`}
                                                title={promotion.isActive ? t('common.deactivate') : t('common.activate')}
                                            >
                                                {promotion.isActive ? '✓' : '○'}
                                            </button>
                                            <button
                                                onClick={() => handleEdit(promotion)}
                                                className="p-2 bg-blue-100 text-blue-600 rounded-lg hover:bg-blue-200 transition-colors"
                                                title={t('common.edit')}
                                            >
                                                ✏️
                                            </button>
                                            <button
                                                onClick={() => handleDelete(promotion)}
                                                className="p-2 bg-red-100 text-red-600 rounded-lg hover:bg-red-200 transition-colors"
                                                title={t('common.delete')}
                                            >
                                                🗑️
                                            </button>
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </div>

            <div className="flex items-center justify-between mt-4 text-sm text-gray-600">
                <button
                    type="button"
                    disabled={filter.page <= 1 || loading}
                    onClick={() => setFilter({ ...filter, page: Math.max(1, filter.page - 1) })}
                    className="px-3 py-2 border border-gray-200 rounded-lg disabled:opacity-50"
                >
                    {t('common.previous')}
                </button>
                <span>{t('common.page')} {filter.page}</span>
                <button
                    type="button"
                    disabled={loading || promotions.length < filter.pageSize}
                    onClick={() => setFilter({ ...filter, page: filter.page + 1 })}
                    className="px-3 py-2 border border-gray-200 rounded-lg disabled:opacity-50"
                >
                    {t('common.next')}
                </button>
            </div>

            {selectedPromotion && (
                <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
                    <div className="bg-white rounded-2xl shadow-xl w-full max-w-xl p-6">
                        <div className="flex items-start justify-between border-b border-gray-100 pb-4 mb-4">
                            <div>
                                <h2 className="text-xl font-bold text-gray-900">{selectedPromotion.name}</h2>
                                <p className="text-sm text-gray-500">{selectedPromotion.eligibilitySummary}</p>
                            </div>
                            <button onClick={() => setSelectedPromotion(null)} className="text-gray-500 hover:text-gray-700">&times;</button>
                        </div>
                        {detailsLoading ? (
                            <div className="py-6 text-center text-gray-500">{t('common.loading')}</div>
                        ) : (
                            <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-sm">
                                <div><span className="text-gray-500">{t('common.status')}:</span> {selectedPromotion.status}</div>
                                <div><span className="text-gray-500">{t('common.discount')}:</span> {formatDiscount(selectedPromotion)}</div>
                                <div><span className="text-gray-500">{t('promotions.scope')}:</span> {getScopeDisplay(selectedPromotion)}</div>
                                <div><span className="text-gray-500">{t('promotions.priority')}:</span> {selectedPromotion.priority}</div>
                                <div><span className="text-gray-500">{t('coupons.startDate')}:</span> {formatDate(selectedPromotion.startDate, locale)}</div>
                                <div><span className="text-gray-500">{t('coupons.endDate')}:</span> {formatDate(selectedPromotion.endDate, locale)}</div>
                                <div><span className="text-gray-500">{t('promotions.usage')}:</span> {selectedPromotion.currentUsage}{selectedPromotion.maxUsage ? `/${selectedPromotion.maxUsage}` : ''}</div>
                                <div><span className="text-gray-500">{t('common.created')}:</span> {formatDate(selectedPromotion.createdAt, locale)}</div>
                            </div>
                        )}
                    </div>
                </div>
            )}

            {/* Modal */}
            {showModal && (
                <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
                    <div className="bg-white rounded-2xl shadow-xl w-full max-w-2xl max-h-[90vh] overflow-y-auto">
                        <div className="p-6 border-b border-gray-100">
                            <h2 className="text-xl font-bold text-gray-900">
                                {editingPromotion
                                    ? t('promotions.editPromotion')
                                    : t('promotions.addPromotion')
                                }
                            </h2>
                        </div>
                        <form onSubmit={handleSubmit} className="p-6 space-y-6">
                            {/* Name & Description */}
                            <div className="grid grid-cols-1 gap-4">
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('common.name')} *
                                    </label>
                                    <input
                                        type="text"
                                        required
                                        value={formData.name}
                                        onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                                        className="w-full px-4 py-3 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                                        placeholder="Summer Sale 20% Off"
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('common.description')}
                                    </label>
                                    <textarea
                                        value={formData.description}
                                        onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                                        className="w-full px-4 py-3 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                                        rows="2"
                                    />
                                </div>
                            </div>

                            {/* Discount Type & Value */}
                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('coupons.discountType')} *
                                    </label>
                                    <select
                                        required
                                        value={formData.discountType}
                                        onChange={(e) => setFormData({ ...formData, discountType: e.target.value })}
                                        className="w-full px-4 py-3 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500"
                                    >
                                        <option value="Percentage">{t('coupons.percentage')}</option>
                                        <option value="FixedAmount">{t('coupons.fixed')}</option>
                                    </select>
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('coupons.discountValue')} *
                                    </label>
                                    <input
                                        type="number"
                                        required
                                        min="0"
                                        step="0.01"
                                        value={formData.discountValue}
                                        onChange={(e) => setFormData({ ...formData, discountValue: e.target.value })}
                                        className="w-full px-4 py-3 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                                        placeholder={formData.discountType === 'Percentage' ? '20' : '50'}
                                    />
                                </div>
                            </div>

                            {/* Scope */}
                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('promotions.scope')} *
                                    </label>
                                    <select
                                        required
                                        value={formData.scope}
                                        onChange={(e) => setFormData({ ...formData, scope: e.target.value, appliesToId: '' })}
                                        className="w-full px-4 py-3 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500"
                                    >
                                        <option value="Product">{t('promotions.scopeProduct')}</option>
                                        <option value="Category">{t('promotions.scopeCategory')}</option>
                                        <option value="Cart">{t('promotions.scopeCart')}</option>
                                    </select>
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('promotions.appliesTo')}
                                    </label>
                                    {formData.scope === 'Product' ? (
                                        <select
                                            value={formData.appliesToId}
                                            onChange={(e) => setFormData({ ...formData, appliesToId: e.target.value })}
                                            className="w-full px-4 py-3 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500"
                                        >
                                            <option value="">{t('promotions.allProducts')}</option>
                                            {products.map(p => (
                                                <option key={p.id} value={p.id}>{p.name}</option>
                                            ))}
                                        </select>
                                    ) : formData.scope === 'Category' ? (
                                        <select
                                            value={formData.appliesToId}
                                            onChange={(e) => setFormData({ ...formData, appliesToId: e.target.value })}
                                            className="w-full px-4 py-3 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500"
                                        >
                                            <option value="">{t('promotions.allCategories')}</option>
                                            {categories.map(c => (
                                                <option key={c.id} value={c.id}>{c.name}</option>
                                            ))}
                                        </select>
                                    ) : (
                                        <input
                                            type="text"
                                            disabled
                                            value={t('promotions.entireCart')}
                                            className="w-full px-4 py-3 border border-gray-200 rounded-xl bg-gray-50"
                                        />
                                    )}
                                </div>
                            </div>

                            {/* Banner Style (Category promotions only) */}
                            {formData.scope === 'Category' && (
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-3">
                                        {t('promotions.bannerStyle')}
                                    </label>
                                    <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                                        {/* Gradient */}
                                        <button
                                            type="button"
                                            onClick={() => setFormData({ ...formData, bannerStyle: 'gradient' })}
                                            className={`p-4 rounded-xl border-2 transition-all ${formData.bannerStyle === 'gradient' ? 'border-blue-500 ring-2 ring-blue-200 scale-105' : 'border-gray-200 hover:border-blue-300 hover:shadow-md'}`}
                                        >
                                            <div className="h-16 bg-gradient-to-r from-purple-600 via-pink-500 to-red-500 rounded-lg mb-3 flex items-center justify-center relative overflow-hidden">
                                                <span className="text-white text-xs font-bold">🎨 SALE</span>
                                                <div className="absolute inset-0 bg-white/10"></div>
                                            </div>
                                            <span className="text-sm font-semibold text-gray-800">{t('promotions.bannerStyles.gradient')}</span>
                                        </button>
                                        {/* Solid */}
                                        <button
                                            type="button"
                                            onClick={() => setFormData({ ...formData, bannerStyle: 'solid' })}
                                            className={`p-4 rounded-xl border-2 transition-all ${formData.bannerStyle === 'solid' ? 'border-blue-500 ring-2 ring-blue-200 scale-105' : 'border-gray-200 hover:border-blue-300 hover:shadow-md'}`}
                                        >
                                            <div className="h-16 bg-indigo-600 rounded-lg mb-3 flex items-center justify-center">
                                                <span className="text-white text-xs font-bold">💎 OFFER</span>
                                            </div>
                                            <span className="text-sm font-semibold text-gray-800">{t('promotions.bannerStyles.solid')}</span>
                                        </button>
                                        {/* Minimal */}
                                        <button
                                            type="button"
                                            onClick={() => setFormData({ ...formData, bannerStyle: 'minimal' })}
                                            className={`p-4 rounded-xl border-2 transition-all ${formData.bannerStyle === 'minimal' ? 'border-blue-500 ring-2 ring-blue-200 scale-105' : 'border-gray-200 hover:border-blue-300 hover:shadow-md'}`}
                                        >
                                            <div className="h-16 bg-gray-50 border-2 border-indigo-400 border-dashed rounded-lg mb-3 flex items-center justify-center">
                                                <span className="text-indigo-600 text-xs font-bold">✨ DEAL</span>
                                            </div>
                                            <span className="text-sm font-semibold text-gray-800">{t('promotions.bannerStyles.minimal')}</span>
                                        </button>
                                        {/* Festive */}
                                        <button
                                            type="button"
                                            onClick={() => setFormData({ ...formData, bannerStyle: 'festive' })}
                                            className={`p-4 rounded-xl border-2 transition-all ${formData.bannerStyle === 'festive' ? 'border-blue-500 ring-2 ring-blue-200 scale-105' : 'border-gray-200 hover:border-blue-300 hover:shadow-md'}`}
                                        >
                                            <div className="h-16 bg-gradient-to-r from-amber-400 via-orange-500 to-red-600 rounded-lg mb-3 flex items-center justify-center relative overflow-hidden">
                                                <span className="text-white text-xs font-bold">🔥 HOT!</span>
                                                <div className="absolute -top-1 -right-1 w-4 h-4 bg-yellow-300 rounded-full animate-pulse"></div>
                                            </div>
                                            <span className="text-sm font-semibold text-gray-800">{t('promotions.bannerStyles.festive')}</span>
                                        </button>
                                    </div>
                                </div>
                            )}

                            {/* Dates */}
                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('coupons.startDate')} *
                                    </label>
                                    <input
                                        type="date"
                                        required
                                        value={formData.startDate}
                                        onChange={(e) => setFormData({ ...formData, startDate: e.target.value })}
                                        className="w-full px-4 py-3 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('coupons.endDate')} *
                                    </label>
                                    <input
                                        type="date"
                                        required
                                        value={formData.endDate}
                                        onChange={(e) => setFormData({ ...formData, endDate: e.target.value })}
                                        className="w-full px-4 py-3 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                                    />
                                </div>
                            </div>

                            {/* Priority & Limits */}
                            <div className="grid grid-cols-3 gap-4">
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('promotions.priority')}
                                    </label>
                                    <input
                                        type="number"
                                        min="0"
                                        value={formData.priority}
                                        onChange={(e) => setFormData({ ...formData, priority: e.target.value })}
                                        className="w-full px-4 py-3 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                                        placeholder="0"
                                    />
                                    <p className="text-xs text-gray-500 mt-1">{t('promotions.priorityHint')}</p>
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('coupons.maxDiscountAmount')}
                                    </label>
                                    <input
                                        type="number"
                                        min="0"
                                        step="0.01"
                                        value={formData.maxDiscountAmount}
                                        onChange={(e) => setFormData({ ...formData, maxDiscountAmount: e.target.value })}
                                        className="w-full px-4 py-3 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                                        placeholder={t('common.noLimit')}
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('coupons.maxUsage')}
                                    </label>
                                    <input
                                        type="number"
                                        min="0"
                                        value={formData.maxUsage}
                                        onChange={(e) => setFormData({ ...formData, maxUsage: e.target.value })}
                                        className="w-full px-4 py-3 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                                        placeholder={t('common.unlimited')}
                                    />
                                </div>
                            </div>

                            {/* Min Order */}
                            <div>
                                <label className="block text-sm font-medium text-gray-700 mb-2">
                                    {t('coupons.minOrderAmount')}
                                </label>
                                <input
                                    type="number"
                                    min="0"
                                    step="0.01"
                                    value={formData.minOrderAmount}
                                    onChange={(e) => setFormData({ ...formData, minOrderAmount: e.target.value })}
                                    className="w-full px-4 py-3 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                                    placeholder={t('common.noMinimum')}
                                />
                            </div>

                            {/* Toggles */}
                            <div className="flex gap-6">
                                <label className="flex items-center gap-2">
                                    <input
                                        type="checkbox"
                                        checked={formData.isActive}
                                        onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                                        className="w-5 h-5 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                                    />
                                    <span className="text-sm text-gray-700">{t('common.active')}</span>
                                </label>
                                <label className="flex items-center gap-2">
                                    <input
                                        type="checkbox"
                                        checked={formData.isExclusive}
                                        onChange={(e) => setFormData({ ...formData, isExclusive: e.target.checked })}
                                        className="w-5 h-5 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                                    />
                                    <span className="text-sm text-gray-700">{t('promotions.exclusive')}</span>
                                </label>
                            </div>

                            {/* Submit */}
                            <div className="flex justify-end gap-4 pt-4 border-t border-gray-100">
                                <button
                                    type="button"
                                    onClick={() => setShowModal(false)}
                                    className="px-6 py-3 text-gray-600 hover:bg-gray-100 rounded-xl transition-colors"
                                >
                                    {t('common.cancel')}
                                </button>
                                <button
                                    type="submit"
                                    className="px-6 py-3 bg-gradient-to-r from-blue-600 to-blue-700 text-white rounded-xl hover:from-blue-700 hover:to-blue-800 transition-all shadow-md"
                                >
                                    {editingPromotion ? t('common.update') : t('common.create')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    )
}

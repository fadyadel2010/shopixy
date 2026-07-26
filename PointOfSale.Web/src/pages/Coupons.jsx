import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import {
    archiveAdminCoupon,
    createAdminCoupon,
    getAdminCoupon,
    listAdminCoupons,
    toggleAdminCoupon,
    updateAdminCoupon,
} from '../services/adminCouponsApi'
import { useAuthStore } from '../store/authStore'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import { useSettings } from '../contexts/SettingsContext'
import { useRTL } from '../hooks/useRTL'
import { formatDate } from '../utils/dateFormat'

export default function Coupons() {
    const { t } = useTranslation()
    const { confirm } = useConfirmDialog()
    const { formatCurrency, settings } = useSettings()
    const { dir, isRTL, locale } = useRTL()
    const user = useAuthStore((state) => state.user)
    const [coupons, setCoupons] = useState([])
    const [selectedCoupon, setSelectedCoupon] = useState(null)
    const [detailsLoading, setDetailsLoading] = useState(false)
    const [loading, setLoading] = useState(true)
    const [showModal, setShowModal] = useState(false)
    const [editingCoupon, setEditingCoupon] = useState(null)
    const [filter, setFilter] = useState({
        activeOnly: false,
        search: '',
        sortBy: 'createdAt',
        sortDirection: 'desc',
        page: 1,
        pageSize: 50,
    })
    const [formData, setFormData] = useState({
        code: '',
        description: '',
        discountType: 'Percentage', // String value for JsonStringEnumConverter
        discountValue: '',
        minOrderAmount: '',
        maxDiscountAmount: '',
        maxUsage: '',
        maxUsagePerCustomer: '',
        startDate: '',
        endDate: '',
        isActive: true,
        firstOrderOnly: false
    })
    const [error, setError] = useState('')
    const [success, setSuccess] = useState('')

    useEffect(() => {
        fetchCoupons()
    }, [filter, user])

    const fetchCoupons = async () => {
        if (!user?.tenantId || !(user?.id || user?.userId)) {
            setCoupons([])
            setLoading(false)
            return
        }

        try {
            setLoading(true)
            const admittedCoupons = await listAdminCoupons(filter, user)
            setCoupons(admittedCoupons)
        } catch (err) {
            setError(t('common.failedToLoad'))
        } finally {
            setLoading(false)
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

    const handleOpenDetails = async (couponId) => {
        if (!user?.tenantId || !(user?.id || user?.userId)) {
            setSelectedCoupon(null)
            setError(t('common.failedToLoad'))
            return
        }

        try {
            setDetailsLoading(true)
            setError('')
            const admittedCoupon = await getAdminCoupon(couponId, user)
            setSelectedCoupon(admittedCoupon)
        } catch (err) {
            setSelectedCoupon(null)
            setError(t('common.failedToLoad'))
        } finally {
            setDetailsLoading(false)
        }
    }

    const handleSubmit = async (e) => {
        e.preventDefault()
        setError('')

        try {
            const data = {
                ...formData,
                discountValue: parseFloat(formData.discountValue) || 0,
                minOrderAmount: formData.minOrderAmount ? parseFloat(formData.minOrderAmount) : null,
                maxDiscountAmount: formData.maxDiscountAmount ? parseFloat(formData.maxDiscountAmount) : null,
                maxUsage: formData.maxUsage ? parseInt(formData.maxUsage) : null,
                maxUsagePerCustomer: formData.maxUsagePerCustomer ? parseInt(formData.maxUsagePerCustomer) : null,
                startDate: formData.startDate || null,
                endDate: formData.endDate || null
            }

            if (editingCoupon) {
                await updateAdminCoupon(editingCoupon, data, user)
                setSuccess(t('common.updatedSuccessfully'))
            } else {
                await createAdminCoupon(data, user)
                setSuccess(t('coupons.created'))
            }

            setShowModal(false)
            fetchCoupons()
            resetForm()
        } catch (err) {
            setError(err.response?.data?.message || t('common.error'))
        }
    }

    const handleEdit = (coupon) => {
        setEditingCoupon(coupon)
        setFormData({
            code: coupon.code,
            description: coupon.description || '',
            discountType: coupon.discountType,
            discountValue: coupon.discountValue.toString(),
            minOrderAmount: coupon.minOrderAmount?.toString() || '',
            maxDiscountAmount: coupon.maxDiscountAmount?.toString() || '',
            maxUsage: coupon.maxUsage?.toString() || '',
            maxUsagePerCustomer: coupon.maxUsagePerCustomer?.toString() || '',
            startDate: coupon.startDate ? coupon.startDate.split('T')[0] : '',
            endDate: coupon.endDate ? coupon.endDate.split('T')[0] : '',
            isActive: coupon.isActive,
            firstOrderOnly: coupon.firstOrderOnly
        })
        setShowModal(true)
    }

    const handleDelete = async (id) => {
        const confirmed = await confirm({
            confirmLabel: t('common.delete'),
            description: t('common.deleteConfirm', { item: t('coupons.coupon') }),
            title: t('common.warning'),
            variant: 'danger',
        })

        if (!confirmed) return

        try {
            const coupon = coupons.find((candidate) => candidate.id === id)
            await archiveAdminCoupon(coupon)
            setSuccess(t('common.deletedSuccessfully'))
            fetchCoupons()
        } catch (err) {
            setError(err.response?.data?.message || t('common.deleteFailed'))
        }
    }

    const handleToggle = async (coupon) => {
        try {
            await toggleAdminCoupon(coupon, user)
            fetchCoupons()
        } catch (err) {
            setError(err.response?.data?.message || t('common.error'))
        }
    }

    const resetForm = () => {
        setEditingCoupon(null)
        setFormData({
            code: '',
            description: '',
            discountType: 'Percentage', // String value for JsonStringEnumConverter
            discountValue: '',
            minOrderAmount: '',
            maxDiscountAmount: '',
            maxUsage: '',
            maxUsagePerCustomer: '',
            startDate: '',
            endDate: '',
            isActive: true,
            firstOrderOnly: false
        })
    }

    const openAddModal = () => {
        resetForm()
        setShowModal(true)
    }

    const formatCouponCurrency = (coupon, amount) => formatCurrency(amount, coupon.currency)

    const formatDiscount = (coupon) => {
        // Handle both string enum names and numeric values
        if (coupon.discountType === 'Percentage' || coupon.discountType === 0) {
            return `${coupon.discountValue}%`
        }
        return formatCouponCurrency(coupon, coupon.discountValue)
    }

    const getCouponStatus = (coupon) => {
        if (coupon.status === 'Inactive') return { label: t('common.inactive'), color: 'bg-gray-100 text-gray-800' }
        if (coupon.status === 'Expired') return { label: t('coupons.expired'), color: 'bg-red-100 text-red-800' }
        return { label: t('common.active'), color: 'bg-green-100 text-green-800' }
    }

    const actionSpacingClass = isRTL ? 'ml-2' : 'mr-2'

    return (
        <div dir={dir} className="p-6">
            {/* Header */}
            <div className="flex justify-between items-center mb-6">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">{t('coupons.title')}</h1>
                    <p className="text-sm text-gray-600">{t('coupons.subtitle')}</p>
                </div>
                <button onClick={openAddModal} className="btn-primary">
                    + {t('coupons.addCoupon')}
                </button>
            </div>

            {/* Filters */}
            <div className="card mb-6">
                <div className="flex gap-4 items-center">
                    <div className="flex-1">
                        <input
                            type="text"
                            placeholder={t('coupons.searchPlaceholder')}
                            value={filter.search}
                            onChange={(e) => setFilter({ ...filter, search: e.target.value, page: 1 })}
                            className="input"
                        />
                    </div>
                    <label className="flex items-center gap-2">
                        <input
                            type="checkbox"
                            checked={filter.activeOnly}
                            onChange={(e) => setFilter({ ...filter, activeOnly: e.target.checked, page: 1 })}
                            className="rounded"
                        />
                        <span className="text-sm">{t('coupons.activeOnly')}</span>
                    </label>
                </div>
            </div>

            {/* Messages */}
            {error && (
                <div className="mb-4 p-3 bg-red-100 border border-red-400 text-red-700 rounded-lg">
                    {error}
                </div>
            )}
            {success && (
                <div className="mb-4 p-3 bg-green-100 border border-green-400 text-green-700 rounded-lg">
                    {success}
                    <button onClick={() => setSuccess('')} className="float-right">×</button>
                </div>
            )}

            {/* Coupons List */}
            <div className="card">
                {loading ? (
                    <div className="flex justify-center py-12">
                        <div className="animate-spin h-8 w-8 border-4 border-primary-600 border-t-transparent rounded-full"></div>
                    </div>
                ) : coupons.length === 0 ? (
                    <div className="text-center py-12 text-gray-500">
                        <span className="text-4xl">🎟️</span>
                        <p className="mt-4">{t('coupons.noCoupons')}</p>
                    </div>
                ) : (
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                                <tr>
                                    <th className="px-6 py-3 text-start text-xs font-medium text-gray-500 uppercase">
                                        <button type="button" onClick={() => handleSort('code')}>{t('coupons.code')}</button>
                                    </th>
                                    <th className="px-6 py-3 text-start text-xs font-medium text-gray-500 uppercase">
                                        <button type="button" onClick={() => handleSort('discountValue')}>{t('coupons.discount')}</button>
                                    </th>
                                    <th className="px-6 py-3 text-start text-xs font-medium text-gray-500 uppercase">{t('coupons.minOrder')}</th>
                                    <th className="px-6 py-3 text-start text-xs font-medium text-gray-500 uppercase">
                                        <button type="button" onClick={() => handleSort('currentUsage')}>{t('coupons.usage')}</button>
                                    </th>
                                    <th className="px-6 py-3 text-start text-xs font-medium text-gray-500 uppercase">
                                        <button type="button" onClick={() => handleSort('endDate')}>{t('coupons.validity')}</button>
                                    </th>
                                    <th className="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase">
                                        <button type="button" onClick={() => handleSort('status')}>{t('common.status')}</button>
                                    </th>
                                    <th className="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase">{t('common.actions')}</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-200">
                                {coupons.map((coupon) => {
                                    const status = getCouponStatus(coupon)
                                    return (
                                        <tr key={coupon.id} className="hover:bg-gray-50">
                                            <td className="px-6 py-4">
                                                <button
                                                    type="button"
                                                    onClick={() => handleOpenDetails(coupon.id)}
                                                    className="font-mono font-bold text-lg text-primary-600 hover:text-primary-800"
                                                >
                                                    {coupon.code}
                                                </button>
                                                <div className="text-sm text-gray-700">{coupon.name}</div>
                                                {coupon.description && (
                                                    <div className="text-sm text-gray-500">{coupon.description}</div>
                                                )}
                                                {coupon.firstOrderOnly && (
                                                    <span className="text-xs bg-purple-100 text-purple-800 px-2 py-0.5 rounded">
                                                        {t('coupons.firstOrderOnly')}
                                                    </span>
                                                )}
                                            </td>
                                            <td className="px-6 py-4">
                                                <div className="text-lg font-bold text-green-600">{formatDiscount(coupon)}</div>
                                                {coupon.maxDiscountAmount && (
                                                    <div className="text-xs text-gray-500">
                                                        max: {formatCouponCurrency(coupon, coupon.maxDiscountAmount)}
                                                    </div>
                                                )}
                                            </td>
                                            <td className="px-6 py-4 text-gray-900">
                                                {coupon.minOrderAmount
                                                    ? formatCouponCurrency(coupon, coupon.minOrderAmount)
                                                    : '-'}
                                            </td>
                                            <td className="px-6 py-4">
                                                <div className="text-gray-900">
                                                    {coupon.currentUsage} / {coupon.maxUsage || '∞'}
                                                </div>
                                                {coupon.maxUsagePerCustomer && (
                                                    <div className="text-xs text-gray-500">
                                                        {coupon.maxUsagePerCustomer} {t('coupons.perCustomer')}
                                                    </div>
                                                )}
                                            </td>
                                            <td className="px-6 py-4 text-sm text-gray-900">
                                                {coupon.startDate && (
                                                    <div>{t('common.from')}: {formatDate(coupon.startDate, locale)}</div>
                                                )}
                                                {coupon.endDate && (
                                                    <div>{t('common.to')}: {formatDate(coupon.endDate, locale)}</div>
                                                )}
                                                {!coupon.startDate && !coupon.endDate && t('common.notApplicable')}
                                            </td>
                                            <td className="px-6 py-4 text-center">
                                                <span className={`px-2 py-1 text-xs font-semibold rounded-full ${status.color}`}>
                                                    {status.label}
                                                </span>
                                            </td>
                                            <td className="px-6 py-4 text-center">
                                                <button
                                                    type="button"
                                                    onClick={() => handleOpenDetails(coupon.id)}
                                                    className={`text-gray-600 hover:text-gray-900 ${actionSpacingClass}`}
                                                >
                                                    {t('common.view')}
                                                </button>
                                                <button
                                                    onClick={() => handleToggle(coupon)}
                                                    className={`text-yellow-600 hover:text-yellow-900 ${actionSpacingClass}`}
                                                    title={coupon.isActive ? t('coupons.deactivate') : t('coupons.activate')}
                                                >
                                                    {coupon.isActive ? '🔒' : '🔓'}
                                                </button>
                                                <button
                                                    onClick={() => handleEdit(coupon)}
                                                    className={`text-primary-600 hover:text-primary-900 ${actionSpacingClass}`}
                                                >
                                                    {t('common.edit')}
                                                </button>
                                                <button
                                                    onClick={() => handleDelete(coupon.id)}
                                                    className="text-red-600 hover:text-red-900"
                                                >
                                                    {t('common.delete')}
                                                </button>
                                            </td>
                                        </tr>
                                    )
                                })}
                            </tbody>
                        </table>
                    </div>
                )}
                <div className="flex items-center justify-between border-t border-gray-200 px-6 py-4">
                    <button
                        type="button"
                        className="btn-secondary"
                        disabled={filter.page <= 1 || loading}
                        onClick={() => setFilter({ ...filter, page: Math.max(1, filter.page - 1) })}
                    >
                        {t('common.previous')}
                    </button>
                    <span className="text-sm text-gray-600">{filter.page}</span>
                    <button
                        type="button"
                        className="btn-secondary"
                        disabled={coupons.length < filter.pageSize || loading}
                        onClick={() => setFilter({ ...filter, page: filter.page + 1 })}
                    >
                        {t('common.next')}
                    </button>
                </div>
            </div>

            {/* Details Modal */}
            {(selectedCoupon || detailsLoading) && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4">
                    <div className="bg-white rounded-xl shadow-2xl w-full max-w-2xl">
                        <div className="p-6 border-b flex items-center justify-between">
                            <h2 className="text-xl font-bold">{selectedCoupon?.code || t('coupons.coupon')}</h2>
                            <button
                                type="button"
                                onClick={() => setSelectedCoupon(null)}
                                className="text-gray-500 hover:text-gray-900"
                            >
                                {t('common.close')}
                            </button>
                        </div>
                        <div className="p-6">
                            {detailsLoading ? (
                                <div className="flex justify-center py-12">
                                    <div className="animate-spin h-8 w-8 border-4 border-primary-600 border-t-transparent rounded-full"></div>
                                </div>
                            ) : selectedCoupon && (
                                <div className="grid grid-cols-2 gap-4 text-sm">
                                    <div>
                                        <div className="text-gray-500">{t('coupons.code')}</div>
                                        <div className="font-mono font-bold text-primary-700">{selectedCoupon.code}</div>
                                    </div>
                                    <div>
                                        <div className="text-gray-500">{t('common.status')}</div>
                                        <div>{selectedCoupon.status}</div>
                                    </div>
                                    <div>
                                        <div className="text-gray-500">{t('coupons.discount')}</div>
                                        <div>{formatDiscount(selectedCoupon)}</div>
                                    </div>
                                    <div>
                                        <div className="text-gray-500">{t('common.currency')}</div>
                                        <div>{selectedCoupon.currency}</div>
                                    </div>
                                    <div>
                                        <div className="text-gray-500">{t('coupons.usage')}</div>
                                        <div>{selectedCoupon.currentUsage} / {selectedCoupon.maxUsage || 'unlimited'}</div>
                                    </div>
                                    <div>
                                        <div className="text-gray-500">{t('coupons.maxPerCustomer')}</div>
                                        <div>{selectedCoupon.maxUsagePerCustomer || 'unlimited'}</div>
                                    </div>
                                    <div>
                                        <div className="text-gray-500">{t('common.from')}</div>
                                        <div>{selectedCoupon.startDate ? formatDate(selectedCoupon.startDate, locale) : '-'}</div>
                                    </div>
                                    <div>
                                        <div className="text-gray-500">{t('common.to')}</div>
                                        <div>{selectedCoupon.endDate ? formatDate(selectedCoupon.endDate, locale) : '-'}</div>
                                    </div>
                                    <div className="col-span-2">
                                        <div className="text-gray-500">{t('common.description')}</div>
                                        <div>{selectedCoupon.description || '-'}</div>
                                    </div>
                                    <div className="col-span-2">
                                        <div className="text-gray-500">{t('coupons.eligibility')}</div>
                                        <div>{selectedCoupon.eligibilitySummary}</div>
                                    </div>
                                </div>
                            )}
                        </div>
                    </div>
                </div>
            )}

            {/* Modal */}
            {showModal && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4">
                    <div className="bg-white rounded-xl shadow-2xl w-full max-w-2xl max-h-[90vh] overflow-y-auto">
                        <div className="p-6 border-b">
                            <h2 className="text-xl font-bold">
                                {editingCoupon ? t('coupons.editCoupon') : t('coupons.addCoupon')}
                            </h2>
                        </div>

                        <form onSubmit={handleSubmit} className="p-6 space-y-4">
                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="label">{t('coupons.code')} *</label>
                                    <input
                                        type="text"
                                        value={formData.code}
                                        onChange={(e) => setFormData({ ...formData, code: e.target.value.toUpperCase() })}
                                        className="input font-mono"
                                        placeholder="e.g., SAVE20"
                                        required
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('coupons.discountType')} *</label>
                                    <select
                                        value={formData.discountType}
                                        onChange={(e) => setFormData({ ...formData, discountType: e.target.value })}
                                        className="input"
                                    >
                                        <option value="Percentage">{t('coupons.percentage')}</option>
                                        <option value="FixedAmount">{t('coupons.fixedAmount')}</option>
                                    </select>
                                </div>
                            </div>

                            <div>
                                <label className="label">{t('common.description')}</label>
                                <textarea
                                    value={formData.description}
                                    onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                                    className="input"
                                    rows="2"
                                    placeholder={t('coupons.descriptionPlaceholder')}
                                />
                            </div>

                            <div className="grid grid-cols-3 gap-4">
                                <div>
                                    <label className="label">
                                        {t('coupons.discountValue')} {formData.discountType === 'Percentage' ? '(%)' : `(${settings?.currencySymbol || '$'})`} *
                                    </label>
                                    <input
                                        type="number"
                                        step="0.01"
                                        value={formData.discountValue}
                                        onChange={(e) => setFormData({ ...formData, discountValue: e.target.value })}
                                        className="input"
                                        required
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('coupons.minOrder')} ({settings?.currencySymbol || '$'})</label>
                                    <input
                                        type="number"
                                        step="0.01"
                                        value={formData.minOrderAmount}
                                        onChange={(e) => setFormData({ ...formData, minOrderAmount: e.target.value })}
                                        className="input"
                                        placeholder={t('common.optional')}
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('coupons.maxDiscount')} ({settings?.currencySymbol || '$'})</label>
                                    <input
                                        type="number"
                                        step="0.01"
                                        value={formData.maxDiscountAmount}
                                        onChange={(e) => setFormData({ ...formData, maxDiscountAmount: e.target.value })}
                                        className="input"
                                        placeholder={t('common.optional')}
                                    />
                                </div>
                            </div>

                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="label">{t('coupons.maxUsage')}</label>
                                    <input
                                        type="number"
                                        value={formData.maxUsage}
                                        onChange={(e) => setFormData({ ...formData, maxUsage: e.target.value })}
                                        className="input"
                                        placeholder={t('coupons.unlimited')}
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('coupons.maxPerCustomer')}</label>
                                    <input
                                        type="number"
                                        value={formData.maxUsagePerCustomer}
                                        onChange={(e) => setFormData({ ...formData, maxUsagePerCustomer: e.target.value })}
                                        className="input"
                                        placeholder={t('coupons.unlimited')}
                                    />
                                </div>
                            </div>

                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="label">{t('coupons.startDate')}</label>
                                    <input
                                        type="date"
                                        value={formData.startDate}
                                        onChange={(e) => setFormData({ ...formData, startDate: e.target.value })}
                                        className="input"
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('coupons.endDate')}</label>
                                    <input
                                        type="date"
                                        value={formData.endDate}
                                        onChange={(e) => setFormData({ ...formData, endDate: e.target.value })}
                                        className="input"
                                    />
                                </div>
                            </div>

                            <div className="flex gap-6">
                                <label className="flex items-center gap-2">
                                    <input
                                        type="checkbox"
                                        checked={formData.isActive}
                                        onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                                        className="rounded"
                                    />
                                    <span>{t('common.active')}</span>
                                </label>
                                <label className="flex items-center gap-2">
                                    <input
                                        type="checkbox"
                                        checked={formData.firstOrderOnly}
                                        onChange={(e) => setFormData({ ...formData, firstOrderOnly: e.target.checked })}
                                        className="rounded"
                                    />
                                    <span>{t('coupons.firstOrderOnlyLabel')}</span>
                                </label>
                            </div>

                            <div className="flex gap-3 pt-4">
                                <button
                                    type="button"
                                    onClick={() => setShowModal(false)}
                                    className="flex-1 btn-secondary"
                                >
                                    {t('common.cancel')}
                                </button>
                                <button type="submit" className="flex-1 btn-primary">
                                    {editingCoupon ? t('common.update') : t('common.save')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    )
}

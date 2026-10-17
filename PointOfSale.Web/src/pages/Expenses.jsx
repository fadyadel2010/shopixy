import { useState, useEffect, useRef, useCallback, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import api from '../services/api'
import { useToast } from '../components/Toast'
import { useRTL } from '../hooks/useRTL'
import { formatDate } from '../utils/dateFormat'
import { toSafeNumber } from '../utils/financialMath'
import AsyncEntitySelect from '../components/AsyncEntitySelect'
import { buildIdempotencyHeaders, stableStringify } from '../utils/requestSafety'

export default function Expenses() {
    const { t } = useTranslation()
    const { isRTL, dir, locale } = useRTL()
    const { confirm } = useConfirmDialog()
    const { formatCurrency } = useSettings()
    const { showSuccess, showError } = useToast()
    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const actionAlignClass = isRTL ? 'text-left' : 'text-right'
    const actionSpacingClass = isRTL ? 'ml-4' : 'mr-4'
    const checkboxLabelSpacingClass = isRTL ? 'mr-2' : 'ml-2'
    const [expenses, setExpenses] = useState([])
    const [categories, setCategories] = useState([])
    const [loading, setLoading] = useState(false)
    const [isSubmitting, setIsSubmitting] = useState(false)
    const [showModal, setShowModal] = useState(false)
    const [editingExpense, setEditingExpense] = useState(null)
    const [page, setPage] = useState(1)
    const [totalPages, setTotalPages] = useState(1)
    const [filters, setFilters] = useState({
        categoryId: '',
        paymentMethod: '',
        fromDate: '',
        toDate: '',
        isApproved: ''
    })
    const [formData, setFormData] = useState({
        expenseCategoryId: '',
        expenseDate: new Date().toISOString().split('T')[0],
        amount: '',
        description: '',
        notes: '',
        paymentMethod: 'Cash',
        supplierId: '',
        supplierName: '',
        cashboxId: '',
        cashboxName: '',
        bankId: '',
        bankName: '',
        referenceNumber: '',
        isRecurring: false,
        recurringFrequency: '',
        requiresApproval: false
    })
    const submissionLockRef = useRef(false)
    const lastSubmissionRef = useRef(null)
    const expensesRequestIdRef = useRef(0)
    const categoriesRequestIdRef = useRef(0)

    const loadExpenses = useCallback(async () => {
        const requestId = ++expensesRequestIdRef.current
        setLoading(true)
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '20',
                ...Object.fromEntries(Object.entries(filters).filter(([, v]) => v !== ''))
            })
            const response = await api.get(`/expenses?${params}`)

            if (requestId !== expensesRequestIdRef.current) {
                return
            }

            setExpenses(response.data.items)
            setTotalPages(response.data.totalPages)
        } catch (error) {
            if (requestId !== expensesRequestIdRef.current) {
                return
            }

            console.error('Error loading expenses:', error)
            showError(t('expenses.failedToLoad'))
        } finally {
            if (requestId === expensesRequestIdRef.current) {
                setLoading(false)
            }
        }
    }, [filters, page, showError, t])

    const loadCategories = useCallback(async () => {
        const requestId = ++categoriesRequestIdRef.current
        try {
            const response = await api.get('/expensecategories')

            if (requestId !== categoriesRequestIdRef.current) {
                return
            }

            setCategories(response.data.filter(c => c.isActive))
        } catch (error) {
            if (requestId !== categoriesRequestIdRef.current) {
                return
            }

            console.error('Error loading categories:', error)
        }
    }, [])

    useEffect(() => {
        void loadCategories()
    }, [loadCategories])

    useEffect(() => {
        void loadExpenses()
    }, [loadExpenses])

    useEffect(() => {
        return () => {
            expensesRequestIdRef.current += 1
            categoriesRequestIdRef.current += 1
        }
    }, [])

    useEffect(() => {
        setFormData(current => {
            if (current.paymentMethod === 'Cash') {
                if (!current.bankId && !current.bankName) {
                    return current
                }

                return {
                    ...current,
                    bankId: '',
                    bankName: '',
                }
            }

            if (current.paymentMethod === 'BankTransfer') {
                if (!current.cashboxId && !current.cashboxName) {
                    return current
                }

                return {
                    ...current,
                    cashboxId: '',
                    cashboxName: '',
                }
            }

            if (!current.cashboxId && !current.cashboxName && !current.bankId && !current.bankName) {
                return current
            }

            return {
                ...current,
                cashboxId: '',
                cashboxName: '',
                bankId: '',
                bankName: '',
            }
        })
    }, [formData.paymentMethod])

    // PERFORMANCE: Create async loaders for dropdowns instead of loading all with pageSize=1000
    const loadSuppliersAsync = useCallback(async ({ search, page, pageSize = 20 }) => {
        const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
        if (search) params.set('search', search)
        const response = await api.get(`/suppliers?${params}`)
        return { items: response.data.items || [], hasMore: page < (response.data.totalPages || 1) }
    }, [])

    const loadCashboxesAsync = useCallback(async ({ search, page, pageSize = 20 }) => {
        const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString(), isActive: 'true' })
        if (search) params.set('search', search)
        const response = await api.get(`/cashbox?${params}`)
        return { items: response.data.items || [], hasMore: page < (response.data.totalPages || 1) }
    }, [])

    const loadBanksAsync = useCallback(async ({ search, page, pageSize = 20 }) => {
        const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
        if (search) params.set('search', search)
        const response = await api.get(`/banks?${params}`)
        return { items: response.data.items || [], hasMore: page < (response.data.totalPages || 1) }
    }, [])

    // PERFORMANCE: Memoized selected entity lookups for display
    const selectedSupplier = useMemo(() => 
        formData.supplierId ? { id: formData.supplierId, name: formData.supplierName || '' } : null,
        [formData.supplierId, formData.supplierName]
    )
    const selectedCashbox = useMemo(() =>
        formData.cashboxId ? { id: formData.cashboxId, name: formData.cashboxName || '' } : null,
        [formData.cashboxId, formData.cashboxName]
    )
    const selectedBank = useMemo(() =>
        formData.bankId ? { id: formData.bankId, name: formData.bankName || '' } : null,
        [formData.bankId, formData.bankName]
    )

    const updateFilters = (nextUpdates) => {
        setPage(1)
        setFilters(current => ({
            ...current,
            ...nextUpdates,
        }))
    }

    const handleSubmit = async (e) => {
        e.preventDefault()
        if (submissionLockRef.current) {
            return
        }

        submissionLockRef.current = true
        setIsSubmitting(true)
        try {
            const amount = toSafeNumber(formData.amount, Number.NaN)

            if (!Number.isFinite(amount) || amount <= 0) {
                submissionLockRef.current = false
                setIsSubmitting(false)
                showError(t('treasury.invalidAmount'))
                return
            }

            const payload = {
                ...formData,
                amount,
                supplierId: formData.supplierId || null,
                cashboxId: formData.cashboxId || null,
                bankId: formData.bankId || null,
                recurringFrequency: formData.isRecurring ? formData.recurringFrequency : null
            }
            const fingerprint = stableStringify({
                editingExpenseId: editingExpense?.id || null,
                payload,
            })
            const idempotencyKey = lastSubmissionRef.current?.fingerprint === fingerprint
                ? lastSubmissionRef.current.key
                : crypto.randomUUID()

            lastSubmissionRef.current = {
                key: idempotencyKey,
                fingerprint,
            }

            if (editingExpense) {
                await api.put(`/expenses/${editingExpense.id}`, payload, {
                    headers: buildIdempotencyHeaders(idempotencyKey),
                })
                showSuccess(t('expenses.updated'))
            } else {
                await api.post('/expenses', payload, {
                    headers: buildIdempotencyHeaders(idempotencyKey),
                })
                showSuccess(t('expenses.created'))
            }
            setShowModal(false)
            resetForm()
            await loadExpenses()
        } catch (error) {
            console.error('Error saving expense:', error)
            showError(error.response?.data?.message || t('expenses.failedToSave'))
        } finally {
            submissionLockRef.current = false
            setIsSubmitting(false)
        }
    }

    const handleEdit = (expense) => {
        setEditingExpense(expense)
        setFormData({
            expenseCategoryId: expense.expenseCategoryId,
            expenseDate: new Date(expense.expenseDate).toISOString().split('T')[0],
            amount: expense.amount.toString(),
            description: expense.description,
            notes: expense.notes || '',
            paymentMethod: expense.paymentMethod,
            supplierId: expense.supplierId || '',
            supplierName: expense.supplierName || '',
            cashboxId: expense.cashboxId || '',
            cashboxName: expense.cashboxName || '',
            bankId: expense.bankId || '',
            bankName: expense.bankName || '',
            referenceNumber: expense.referenceNumber || '',
            isRecurring: expense.isRecurring,
            recurringFrequency: expense.recurringFrequency || '',
            requiresApproval: expense.requiresApproval
        })
        setShowModal(true)
    }

    const handleDelete = async (id) => {
        const confirmed = await confirm({
            confirmLabel: t('common.delete'),
            description: t('expenses.deleteConfirm'),
            title: t('common.warning'),
            variant: 'danger',
        })

        if (!confirmed) return

        try {
            await api.delete(`/expenses/${id}`)
            showSuccess(t('expenses.deleted'))
            loadExpenses()
        } catch (error) {
            console.error('Error deleting expense:', error)
            showError(error.response?.data?.message || t('expenses.failedToDelete'))
        }
    }

    const handleApprove = async (id, approved) => {
        try {
            await api.post(`/expenses/${id}/approve`, { expenseId: id, approved })
            showSuccess(approved ? t('expenses.approved') : t('expenses.rejected'))
            loadExpenses()
        } catch (error) {
            console.error('Error approving expense:', error)
            showError(t('common.error'))
        }
    }

    const resetForm = () => {
        lastSubmissionRef.current = null
        setFormData({
            expenseCategoryId: '',
            expenseDate: new Date().toISOString().split('T')[0],
            amount: '',
            description: '',
            notes: '',
            paymentMethod: 'Cash',
            supplierId: '',
            supplierName: '',
            cashboxId: '',
            cashboxName: '',
            bankId: '',
            bankName: '',
            referenceNumber: '',
            isRecurring: false,
            recurringFrequency: '',
            requiresApproval: false
        })
        setEditingExpense(null)
    }

    const handleCloseModal = () => {
        if (isSubmitting || submissionLockRef.current) {
            return
        }

        setShowModal(false)
        resetForm()
    }

    return (
        <div dir={dir} className="space-y-6">
            {/* Header */}
            <div className="flex justify-between items-center">
                <h1 className="text-2xl font-bold text-gray-900">{t('expenses.title')}</h1>
                <button
                    onClick={() => setShowModal(true)}
                    className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700"
                >
                    {t('expenses.addExpense')}
                </button>
            </div>

            {/* Filters */}
            <div className="bg-white rounded-lg shadow p-6">
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-4">
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('expenses.category')}
                        </label>
                        <select
                            value={filters.categoryId}
                            onChange={(e) => updateFilters({ categoryId: e.target.value })}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        >
                            <option value="">{t('expenses.allCategories')}</option>
                            {categories.map(cat => (
                                <option key={cat.id} value={cat.id}>{cat.name}</option>
                            ))}
                        </select>
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('expenses.paymentMethod')}
                        </label>
                        <select
                            value={filters.paymentMethod}
                            onChange={(e) => updateFilters({ paymentMethod: e.target.value })}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        >
                            <option value="">{t('expenses.allPaymentMethods')}</option>
                            <option value="Cash">{t('expenses.cash')}</option>
                            <option value="Card">{t('expenses.card')}</option>
                            <option value="BankTransfer">{t('expenses.bankTransfer')}</option>
                            <option value="Cheque">{t('expenses.cheque')}</option>
                        </select>
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('expenses.fromDate')}
                        </label>
                        <input
                            type="date"
                            value={filters.fromDate}
                            onChange={(e) => updateFilters({ fromDate: e.target.value })}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        />
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('expenses.toDate')}
                        </label>
                        <input
                            type="date"
                            value={filters.toDate}
                            onChange={(e) => updateFilters({ toDate: e.target.value })}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        />
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('common.status')}
                        </label>
                        <select
                            value={filters.isApproved}
                            onChange={(e) => updateFilters({ isApproved: e.target.value })}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        >
                            <option value="">{t('common.all')}</option>
                            <option value="true">{t('expenses.approved')}</option>
                            <option value="false">{t('expenses.pending')}</option>
                        </select>
                    </div>
                </div>
            </div>

            {/* Expenses Table */}
            <div className="bg-white rounded-lg shadow overflow-hidden">
                {loading && expenses.length > 0 && (
                    <div className="border-b border-gray-200 bg-gray-50 px-6 py-2 text-xs text-gray-500">
                        {t('common.loading')}
                    </div>
                )}
                {loading && expenses.length === 0 ? (
                    <div className="p-12 text-center text-gray-500">
                        {t('common.loading')}
                    </div>
                ) : expenses.length === 0 ? (
                    <div className="p-12 text-center text-gray-500">
                        {t('expenses.noExpenses')}
                    </div>
                ) : (
                    <>
                        <div className="overflow-x-auto">
                            <table className="min-w-full divide-y divide-gray-200">
                                <thead className="bg-gray-50">
                                    <tr>
                                        <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                            {t('common.date')}
                                        </th>
                                        <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                            {t('expenses.category')}
                                        </th>
                                        <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                            {t('expenses.description')}
                                        </th>
                                        <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                            {t('expenses.amount')}
                                        </th>
                                        <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                            {t('expenses.paymentMethod')}
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
                                    {expenses.map((expense) => (
                                        <tr key={expense.id} className="hover:bg-gray-50">
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                {formatDate(expense.expenseDate, locale)}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                {expense.categoryName}
                                            </td>
                                            <td className="px-6 py-4 text-sm text-gray-900">
                                                {expense.description}
                                                {expense.supplierName && (
                                                    <div className="text-xs text-gray-500">
                                                        {expense.supplierName}
                                                    </div>
                                                )}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-right font-medium text-gray-900">
                                                {formatCurrency(expense.amount)}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                <span className="px-2 py-1 text-xs font-medium rounded-full bg-blue-100 text-blue-800">
                                                    {t(`expenses.${expense.paymentMethod.toLowerCase()}`)}
                                                </span>
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap">
                                                {expense.requiresApproval ? (
                                                    <span className={`px-2 py-1 text-xs font-medium rounded-full ${expense.isApproved
                                                        ? 'bg-green-100 text-green-800'
                                                        : 'bg-yellow-100 text-yellow-800'
                                                        }`}>
                                                        {expense.isApproved ? t('expenses.approved') : t('expenses.pending')}
                                                    </span>
                                                ) : (
                                                    <span className="px-2 py-1 text-xs font-medium rounded-full bg-gray-100 text-gray-800">
                                                        {t('common.notApplicable')}
                                                    </span>
                                                )}
                                            </td>
                                            <td className={`px-6 py-4 whitespace-nowrap text-sm font-medium ${actionAlignClass}`}>
                                                {!expense.isApproved && (
                                                    <>
                                                        <button
                                                            onClick={() => handleEdit(expense)}
                                                            className={`text-blue-600 hover:text-blue-900 ${actionSpacingClass}`}
                                                        >
                                                            {t('common.edit')}
                                                        </button>
                                                        <button
                                                            onClick={() => handleDelete(expense.id)}
                                                            className={`text-red-600 hover:text-red-900 ${actionSpacingClass}`}
                                                        >
                                                            {t('common.delete')}
                                                        </button>
                                                    </>
                                                )}
                                                {expense.requiresApproval && !expense.isApproved && (
                                                    <button
                                                        onClick={() => handleApprove(expense.id, true)}
                                                        className="text-green-600 hover:text-green-900"
                                                    >
                                                        {t('expenses.approve')}
                                                    </button>
                                                )}
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>

                        {/* Pagination */}
                        {totalPages > 1 && (
                            <div className="bg-gray-50 px-6 py-4 flex items-center justify-between border-t border-gray-200">
                                <div className="text-sm text-gray-700">
                                    {t('cashbox.page')} {page} {t('cashbox.of')} {totalPages}
                                </div>
                                <div className="flex gap-2">
                                    <button
                                        onClick={() => setPage(page - 1)}
                                        disabled={page === 1}
                                        className="px-4 py-2 border border-gray-300 rounded-lg text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                                    >
                                        {t('cashbox.previous')}
                                    </button>
                                    <button
                                        onClick={() => setPage(page + 1)}
                                        disabled={page === totalPages}
                                        className="px-4 py-2 border border-gray-300 rounded-lg text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                                    >
                                        {t('cashbox.next')}
                                    </button>
                                </div>
                            </div>
                        )}
                    </>
                )}
            </div>

            {/* Modal */}
            {showModal && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 overflow-y-auto">
                    <div className="bg-white rounded-lg p-6 w-full max-w-2xl m-4">
                        <h2 className="text-xl font-bold text-gray-900 mb-4">
                            {editingExpense ? t('expenses.editExpense') : t('expenses.newExpense')}
                        </h2>
                        <form onSubmit={handleSubmit} className="space-y-4">
                            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('expenses.category')} *
                                    </label>
                                    <select
                                        value={formData.expenseCategoryId}
                                        onChange={(e) => setFormData({ ...formData, expenseCategoryId: e.target.value })}
                                        className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                        required
                                    >
                                        <option value="">{t('expenses.selectCategory')}</option>
                                        {categories.map(cat => (
                                            <option key={cat.id} value={cat.id}>{cat.name}</option>
                                        ))}
                                    </select>
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('expenses.expenseDate')} *
                                    </label>
                                    <input
                                        type="date"
                                        value={formData.expenseDate}
                                        onChange={(e) => setFormData({ ...formData, expenseDate: e.target.value })}
                                        className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                        required
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('expenses.amount')} *
                                    </label>
                                    <input
                                        type="number"
                                        step="0.01"
                                        value={formData.amount}
                                        onChange={(e) => setFormData({ ...formData, amount: e.target.value })}
                                        className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                        required
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('expenses.paymentMethod')} *
                                    </label>
                                    <select
                                        value={formData.paymentMethod}
                                        onChange={(e) => setFormData({ ...formData, paymentMethod: e.target.value })}
                                        className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                        required
                                    >
                                        <option value="Cash">{t('expenses.cash')}</option>
                                        <option value="Card">{t('expenses.card')}</option>
                                        <option value="BankTransfer">{t('expenses.bankTransfer')}</option>
                                        <option value="Cheque">{t('expenses.cheque')}</option>
                                    </select>
                                </div>
                            </div>

                            <div>
                                <label className="block text-sm font-medium text-gray-700 mb-2">
                                    {t('expenses.description')} *
                                </label>
                                <input
                                    type="text"
                                    value={formData.description}
                                    onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                    required
                                />
                            </div>

                            {formData.paymentMethod === 'Cash' && (
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('expenses.cashbox')}
                                    </label>
                                    <AsyncEntitySelect
                                        value={selectedCashbox}
                                        onChange={(cb) => setFormData({ 
                                            ...formData, 
                                            cashboxId: cb?.id || '', 
                                            cashboxName: cb?.name || '' 
                                        })}
                                        loadOptions={loadCashboxesAsync}
                                        placeholder={t('expenses.selectCashbox')}
                                        className="w-full"
                                    />
                                </div>
                            )}

                            {formData.paymentMethod === 'BankTransfer' && (
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('expenses.bank')}
                                    </label>
                                    <AsyncEntitySelect
                                        value={selectedBank}
                                        onChange={(bank) => setFormData({ 
                                            ...formData, 
                                            bankId: bank?.id || '', 
                                            bankName: bank?.bankName || bank?.name || '' 
                                        })}
                                        loadOptions={loadBanksAsync}
                                        getOptionLabel={(bank) => bank?.bankName || bank?.name || ''}
                                        placeholder={t('expenses.selectBank')}
                                        className="w-full"
                                    />
                                </div>
                            )}

                            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('expenses.supplier')}
                                    </label>
                                    <AsyncEntitySelect
                                        value={selectedSupplier}
                                        onChange={(sup) => setFormData({ 
                                            ...formData, 
                                            supplierId: sup?.id || '', 
                                            supplierName: sup?.name || '' 
                                        })}
                                        loadOptions={loadSuppliersAsync}
                                        placeholder={t('expenses.selectSupplier')}
                                        className="w-full"
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
                                        {t('expenses.referenceNumber')}
                                    </label>
                                    <input
                                        type="text"
                                        value={formData.referenceNumber}
                                        onChange={(e) => setFormData({ ...formData, referenceNumber: e.target.value })}
                                        className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                    />
                                </div>
                            </div>

                            <div>
                                <label className="block text-sm font-medium text-gray-700 mb-2">
                                    {t('expenses.notes')}
                                </label>
                                <textarea
                                    value={formData.notes}
                                    onChange={(e) => setFormData({ ...formData, notes: e.target.value })}
                                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                    rows="2"
                                />
                            </div>

                            <div className="flex gap-6">
                                <label className="flex items-center">
                                    <input
                                        type="checkbox"
                                        checked={formData.isRecurring}
                                        onChange={(e) => setFormData({ ...formData, isRecurring: e.target.checked })}
                                        className="w-4 h-4 text-blue-600 rounded focus:ring-blue-500"
                                    />
                                    <span className={`${checkboxLabelSpacingClass} text-sm text-gray-700`}>
                                        {t('expenses.isRecurring')}
                                    </span>
                                </label>
                                {formData.isRecurring && (
                                    <select
                                        value={formData.recurringFrequency}
                                        onChange={(e) => setFormData({ ...formData, recurringFrequency: e.target.value })}
                                        className="px-3 py-1 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                    >
                                        <option value="">{t('expenses.recurringFrequency')}</option>
                                        <option value="Monthly">{t('expenses.monthly')}</option>
                                        <option value="Quarterly">{t('expenses.quarterly')}</option>
                                        <option value="Yearly">{t('expenses.yearly')}</option>
                                    </select>
                                )}
                                <label className="flex items-center">
                                    <input
                                        type="checkbox"
                                        checked={formData.requiresApproval}
                                        onChange={(e) => setFormData({ ...formData, requiresApproval: e.target.checked })}
                                        className="w-4 h-4 text-blue-600 rounded focus:ring-blue-500"
                                    />
                                    <span className={`${checkboxLabelSpacingClass} text-sm text-gray-700`}>
                                        {t('expenses.requiresApproval')}
                                    </span>
                                </label>
                            </div>

                            <div className="flex justify-end gap-2 mt-6 pt-4 border-t">
                                <button
                                    type="button"
                                    onClick={handleCloseModal}
                                    disabled={isSubmitting}
                                    className="px-4 py-2 border border-gray-300 rounded-lg text-gray-700 hover:bg-gray-50"
                                >
                                    {t('common.cancel')}
                                </button>
                                <button
                                    type="submit"
                                    disabled={isSubmitting}
                                    className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-gray-400"
                                >
                                    {isSubmitting ? t('common.saving') : t('common.save')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    )
}

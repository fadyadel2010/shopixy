import { useState, useEffect, useRef, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { useSettings } from '../contexts/SettingsContext'
import api, { isRequestCanceled } from '../services/api'
import { useToast } from '../components/Toast'
import CashboxTransactionModal from '../components/CashboxTransactionModal'
import OpenCloseCashboxModal from '../components/OpenCloseCashboxModal'
import { useRTL } from '../hooks/useRTL'
import { formatDateTime } from '../utils/dateFormat'

export default function Cashbox() {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()
    const { dir, isRTL, locale } = useRTL()
    const navigate = useNavigate()
    const [cashboxes, setCashboxes] = useState([])
    const [selectedCashbox, setSelectedCashbox] = useState(null)
    const [transactions, setTransactions] = useState([])
    const [summary, setSummary] = useState(null)
    const [loading, setLoading] = useState(true)
    const [cashboxesLoading, setCashboxesLoading] = useState(true)
    const [page, setPage] = useState(1)
    const [totalPages, setTotalPages] = useState(1)
    const [startDate, setStartDate] = useState('')
    const [endDate, setEndDate] = useState('')
    const [transactionType, setTransactionType] = useState('')
    const [showTransactionModal, setShowTransactionModal] = useState(false)
    const [transactionModalType, setTransactionModalType] = useState('cash-in')
    const [showOpenCloseModal, setShowOpenCloseModal] = useState(false)
    const [openCloseMode, setOpenCloseMode] = useState('open')
    const cashboxesRequestControllerRef = useRef(null)
    const cashboxesRequestSequenceRef = useRef(0)
    const transactionsRequestControllerRef = useRef(null)
    const transactionsRequestSequenceRef = useRef(0)
    const summaryRequestControllerRef = useRef(null)
    const summaryRequestSequenceRef = useRef(0)

    const { showSuccess, showError } = useToast()

    function beginAbortableRequest(controllerRef) {
        controllerRef.current?.abort()
        const controller = new AbortController()
        controllerRef.current = controller
        return controller
    }

    const fetchCashboxes = useCallback(async (signal, requestSequence) => {
        setCashboxesLoading(true)
        try {
            const response = await api.get('/cashbox', { signal })

            if (signal.aborted || requestSequence !== cashboxesRequestSequenceRef.current) {
                return
            }

            const nextCashboxes = response.data.items || []
            setCashboxes(nextCashboxes)
            setSelectedCashbox((currentSelectedCashbox) => {
                if (currentSelectedCashbox) {
                    // Clear selection if the current cashbox is no longer in the list
                    const foundCashbox = nextCashboxes.find(c => c.id === currentSelectedCashbox.id)
                    return foundCashbox || null
                }

                const activeCashbox = nextCashboxes.find(c => c.isActive)
                return activeCashbox || nextCashboxes[0] || null
            })
        } catch (error) {
            if (signal.aborted || isRequestCanceled(error)) {
                return
            }

            console.error('Error fetching cashboxes:', error)
            showError(t('cashbox.failedToLoad'))
        } finally {
            if (!signal.aborted && requestSequence === cashboxesRequestSequenceRef.current) {
                setCashboxesLoading(false)
            }
        }
    }, [showError, t])

    const fetchTransactions = useCallback(async (cashboxId, signal, requestSequence) => {
        setLoading(true)
        try {
            const params = {
                page,
                pageSize: 50,
                startDate: startDate || undefined,
                endDate: endDate || undefined,
                transactionType: transactionType || undefined
            }

            const response = await api.get(`/cashbox/${cashboxId}/transactions`, { params, signal })

            if (signal.aborted || requestSequence !== transactionsRequestSequenceRef.current) {
                return
            }

            setTransactions(response.data.items)
            setTotalPages(response.data.totalPages)
        } catch (error) {
            if (signal.aborted || isRequestCanceled(error)) {
                return
            }

            console.error('Error fetching transactions:', error)
            showError(t('cashbox.failedToLoadTransactions'))
        } finally {
            if (!signal.aborted && requestSequence === transactionsRequestSequenceRef.current) {
                setLoading(false)
            }
        }
    }, [endDate, page, showError, startDate, t, transactionType])

    const fetchSummary = useCallback(async (cashboxId, signal, requestSequence) => {
        try {
            const today = new Date().toISOString().split('T')[0]
            const params = {
                startDate: today,
                endDate: today
            }

            const response = await api.get(`/cashbox/${cashboxId}/summary`, { params, signal })

            if (signal.aborted || requestSequence !== summaryRequestSequenceRef.current) {
                return
            }

            setSummary(response.data)
        } catch (error) {
            if (signal.aborted || isRequestCanceled(error)) {
                return
            }

            console.error('Error fetching summary:', error)
            // Set default summary to allow page to work
            setSummary({
                totalCashIn: 0,
                totalCashOut: 0,
                netChange: 0,
                transactionsByType: {}
            })
        }
    }, [])

    const refreshCashboxes = useCallback(() => {
        const controller = beginAbortableRequest(cashboxesRequestControllerRef)
        const requestSequence = ++cashboxesRequestSequenceRef.current
        void fetchCashboxes(controller.signal, requestSequence)
    }, [fetchCashboxes])

    const refreshTransactions = useCallback((cashboxId = selectedCashbox?.id) => {
        if (!cashboxId) {
            return
        }

        const controller = beginAbortableRequest(transactionsRequestControllerRef)
        const requestSequence = ++transactionsRequestSequenceRef.current
        void fetchTransactions(cashboxId, controller.signal, requestSequence)
    }, [fetchTransactions, selectedCashbox?.id])

    const refreshSummary = useCallback((cashboxId = selectedCashbox?.id) => {
        if (!cashboxId) {
            return
        }

        const controller = beginAbortableRequest(summaryRequestControllerRef)
        const requestSequence = ++summaryRequestSequenceRef.current
        void fetchSummary(cashboxId, controller.signal, requestSequence)
    }, [fetchSummary, selectedCashbox?.id])

    const abortPendingRequests = useCallback(() => {
        cashboxesRequestControllerRef.current?.abort()
        transactionsRequestControllerRef.current?.abort()
        summaryRequestControllerRef.current?.abort()
    }, [])

    useEffect(() => {
        refreshCashboxes()
        return abortPendingRequests
    }, [refreshCashboxes, abortPendingRequests])

    useEffect(() => {
        if (selectedCashbox?.id) {
            refreshTransactions(selectedCashbox?.id)
            refreshSummary(selectedCashbox?.id)
        }
    }, [refreshSummary, refreshTransactions, selectedCashbox?.id])

    const handleOpenCashbox = () => {
        setOpenCloseMode('open')
        setShowOpenCloseModal(true)
    }

    const handleCloseCashbox = () => {
        setOpenCloseMode('close')
        setShowOpenCloseModal(true)
    }

    const handleOpenCloseSuccess = () => {
        setShowOpenCloseModal(false)
        refreshCashboxes()
        if (selectedCashbox) {
            refreshTransactions()
            refreshSummary()
        }
    }

    const handleTransactionSuccess = () => {
        setShowTransactionModal(false)
        refreshCashboxes()
        refreshTransactions()
        refreshSummary()
    }

    const openTransactionModal = (type) => {
        setTransactionModalType(type)
        setShowTransactionModal(true)
    }

    const getTransactionTypeColor = (type) => {
        switch (type) {
            case 'Sale':
            case 'ManualCashIn':
            case 'OpeningBalance':
            case 'CustomerPayment':
                return 'text-green-600'
            case 'SaleRefund':
            case 'PurchasePayment':
            case 'ManualCashOut':
            case 'Expense':
            case 'SupplierPayment':
                return 'text-red-600'
            default:
                return 'text-gray-600'
        }
    }

    const formatTransactionType = (type) => {
        return type.replace(/([A-Z])/g, ' $1').trim()
    }

    const [showCreateCashbox, setShowCreateCashbox] = useState(false)
    const [newCashboxName, setNewCashboxName] = useState('')

    const handleCreateCashbox = async (e) => {
        e.preventDefault()
        try {
            await api.post('/cashbox', {
                name: newCashboxName,
                warehouseId: null, // System-wide cashbox
                description: 'System-wide cashbox'
            })
            showSuccess(t('cashbox.cashboxCreated'))
            setShowCreateCashbox(false)
            setNewCashboxName('')
            refreshCashboxes()
        } catch (error) {
            console.error('Error creating cashbox:', error)
            showError(error.response?.data?.message || t('cashbox.failedToCreate'))
        }
    }

    if (cashboxesLoading && !selectedCashbox && !showCreateCashbox) {
        return (
            <div className="h-full flex items-center justify-center" dir={dir}>
                <div className="text-center text-gray-500">{t('cashbox.loading')}</div>
            </div>
        )
    }

    if (!selectedCashbox && !showCreateCashbox) {
        return (
            <div className="h-full flex items-center justify-center">
                <div className="text-center">
                    <div className="mb-6">
                        <div className="w-24 h-24 bg-primary-100 rounded-full flex items-center justify-center mx-auto mb-4">
                            <span className="text-5xl">💰</span>
                        </div>
                        <h2 className="text-2xl font-semibold text-gray-800 mb-2">{t('cashbox.noCashboxFound')}</h2>
                        <p className="text-gray-500 mb-6">{t('cashbox.createCashboxPrompt')}</p>
                    </div>
                    <button
                        onClick={() => setShowCreateCashbox(true)}
                        className="btn-primary px-8 py-3 text-lg"
                    >
                        {t('cashbox.createFirstCashbox')}
                    </button>
                </div>
            </div>
        )
    }

    if (showCreateCashbox) {
        return (
            <div dir={dir} className="h-full flex items-center justify-center">
                <div className="bg-white rounded-lg shadow-xl p-8 w-full max-w-md">
                    <h2 className="text-2xl font-bold text-gray-800 mb-6">{t('cashbox.createSystemCashbox')}</h2>
                    <form onSubmit={handleCreateCashbox} className="space-y-4">
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {t('cashbox.cashboxName')}
                            </label>
                            <input
                                type="text"
                                value={newCashboxName}
                                onChange={(e) => setNewCashboxName(e.target.value)}
                                className="input w-full"
                                placeholder={t('cashbox.cashboxNamePlaceholder')}
                                required
                                autoFocus
                            />
                            <p className="text-xs text-gray-500 mt-1">
                                {t('cashbox.cashboxDescription')}
                            </p>
                        </div>
                        <div className="flex gap-3 pt-4">
                            <button
                                type="button"
                                onClick={() => setShowCreateCashbox(false)}
                                className="btn-secondary flex-1"
                            >
                                {t('common.cancel')}
                            </button>
                            <button
                                type="submit"
                                className="btn-primary flex-1"
                            >
                                {t('cashbox.createCashbox')}
                            </button>
                        </div>
                    </form>
                </div>
            </div>
        )
    }

    return (
        <div dir={dir} className="h-full flex flex-col">
            {/* Header */}
            <div className="mb-6 flex justify-between items-center">
                <div className="flex items-center gap-4">
                    <h1 className="text-2xl font-bold text-gray-800">{t('cashbox.title')}</h1>
                    <select
                        value={selectedCashbox?.id || ''}
                        onChange={(e) => {
                            const cashbox = cashboxes.find(c => c.id === e.target.value)
                            setSelectedCashbox(cashbox || null)
                        }}
                        className="input"
                    >
                        {cashboxes.map(cb => (
                            <option key={cb.id} value={cb.id}>
                                {cb.name} {cb.isActive ? `(${t('cashbox.active')})` : ''}
                            </option>
                        ))}
                    </select>
                </div>
                <div className="flex gap-2">
                    {selectedCashbox?.isActive ? (
                        <button onClick={handleCloseCashbox} className="btn-secondary">
                            {t('cashbox.closeCashbox')}
                        </button>
                    ) : (
                        <button onClick={handleOpenCashbox} className="btn-primary">
                            🔓 {t('cashbox.openCashbox')}
                        </button>
                    )}
                </div>
            </div>

            {/* Cashbox Closed Warning */}
            {!selectedCashbox?.isActive && (
                <div className="bg-amber-50 border-l-4 border-amber-500 p-4 mb-6 rounded-lg">
                    <div className="flex items-center">
                        <div className="flex-shrink-0">
                            <span className="text-2xl">⚠️</span>
                        </div>
                        <div className={isRTL ? 'mr-3' : 'ml-3'}>
                            <h3 className="text-sm font-medium text-amber-800">
                                {t('cashbox.cashboxClosed')}
                            </h3>
                            <p className="text-sm text-amber-700 mt-1">
                                {t('cashbox.cashboxClosedMessage')}
                            </p>
                        </div>
                    </div>
                </div>
            )}

            {/* Summary Cards */}
            <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-6">
                <div className="bg-white p-6 rounded-lg shadow">
                    <div className="text-sm font-medium text-gray-500 mb-1">{t('cashbox.currentBalance')}</div>
                    <div className="text-3xl font-bold text-gray-900">
                        {formatCurrency(selectedCashbox?.currentBalance ?? 0)}
                    </div>
                    <div className="text-xs text-gray-500 mt-1">
                        {t('cashbox.expected')}: {formatCurrency(selectedCashbox?.expectedBalance ?? 0)}
                    </div>
                </div>
                <div className="bg-white p-6 rounded-lg shadow">
                    <div className="text-sm font-medium text-gray-500 mb-1">{t('cashbox.cashInToday')}</div>
                    <div className="text-3xl font-bold text-green-600">
                        {formatCurrency(summary?.totalCashIn || 0)}
                    </div>
                </div>
                <div className="bg-white p-6 rounded-lg shadow">
                    <div className="text-sm font-medium text-gray-500 mb-1">{t('cashbox.cashOutToday')}</div>
                    <div className="text-3xl font-bold text-red-600">
                        {formatCurrency(summary?.totalCashOut || 0)}
                    </div>
                </div>
                <div className="bg-white p-6 rounded-lg shadow">
                    <div className="text-sm font-medium text-gray-500 mb-1">{t('cashbox.netChangeToday')}</div>
                    <div className={`text-3xl font-bold ${summary?.netChange >= 0 ? 'text-green-600' : 'text-red-600'}`}>
                        {formatCurrency(summary?.netChange || 0)}
                    </div>
                </div>
            </div>

            {/* Quick Actions */}
            {selectedCashbox?.isActive && (
                <div className="bg-white p-4 rounded-lg shadow mb-6">
                    <h2 className="text-lg font-semibold text-gray-800 mb-3">{t('cashbox.quickActions')}</h2>
                    <div className="flex gap-3">
                        <button
                            onClick={() => openTransactionModal('cash-in')}
                            className="btn-primary"
                        >
                            💵 {t('cashbox.cashIn')}
                        </button>
                        <button
                            onClick={() => openTransactionModal('cash-out')}
                            className="btn-secondary"
                        >
                            💸 {t('cashbox.cashOut')}
                        </button>
                        <button
                            onClick={() => navigate('/expenses')}
                            className="btn-secondary"
                        >
                            📝 {t('cashbox.recordExpense')}
                        </button>
                    </div>
                </div>
            )}

            {/* Filters */}
            <div className="bg-white p-4 rounded-lg shadow mb-6">
                <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">{t('cashbox.startDate')}</label>
                        <input
                            type="date"
                            value={startDate}
                            onChange={(e) => setStartDate(e.target.value)}
                            className="input w-full"
                        />
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">{t('cashbox.endDate')}</label>
                        <input
                            type="date"
                            value={endDate}
                            onChange={(e) => setEndDate(e.target.value)}
                            className="input w-full"
                        />
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">{t('cashbox.transactionType')}</label>
                        <select
                            value={transactionType}
                            onChange={(e) => setTransactionType(e.target.value)}
                            className="input w-full"
                        >
                            <option value="">{t('cashbox.allTypes')}</option>
                            <option value="Sale">{t('cashbox.sale')}</option>
                            <option value="SaleRefund">{t('cashbox.saleRefund')}</option>
                            <option value="PurchasePayment">{t('cashbox.purchasePayment')}</option>
                            <option value="ManualCashIn">{t('cashbox.manualCashIn')}</option>
                            <option value="ManualCashOut">{t('cashbox.manualCashOut')}</option>
                            <option value="Expense">{t('cashbox.expense')}</option>
                        </select>
                    </div>
                    <div className="flex items-end">
                        <button
                            onClick={() => {
                                setStartDate('')
                                setEndDate('')
                                setTransactionType('')
                                setPage(1)
                            }}
                            className="btn-secondary w-full"
                        >
                            {t('cashbox.clearFilters')}
                        </button>
                    </div>
                </div>
            </div>

            {/* Transactions Table */}
            <div className="bg-white rounded-lg shadow flex-1 overflow-hidden flex flex-col">
                <div className="overflow-x-auto flex-1">
                    <table className="w-full">
                        <thead className="bg-gray-50 border-b border-gray-200">
                            <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('cashbox.dateTime')}</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('cashbox.type')}</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('cashbox.description')}</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('cashbox.reference')}</th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">{t('cashbox.amount')}</th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">{t('cashbox.balanceAfter')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {loading ? (
                                <tr>
                                    <td colSpan="6" className="px-6 py-4 text-center text-gray-500">{t('cashbox.loading')}</td>
                                </tr>
                            ) : transactions.length === 0 ? (
                                <tr>
                                    <td colSpan="6" className="px-6 py-4 text-center text-gray-500">{t('cashbox.noTransactions')}</td>
                                </tr>
                            ) : (
                                transactions.map((txn) => (
                                    <tr key={txn.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            {formatDateTime(txn.transactionDate, locale)}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm">
                                            <span className={`font-medium ${getTransactionTypeColor(txn.transactionType)}`}>
                                                {formatTransactionType(txn.transactionType)}
                                            </span>
                                        </td>
                                        <td className="px-6 py-4 text-sm text-gray-900">
                                            {txn.description}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            {txn.referenceNumber || '-'}
                                        </td>
                                        <td className={`px-6 py-4 whitespace-nowrap text-sm font-medium text-right ${txn.amount >= 0 ? 'text-green-600' : 'text-red-600'}`}>
                                            {txn.amount >= 0 ? '+' : ''}{formatCurrency(txn.amount)}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-right font-medium">
                                            {formatCurrency(txn.balanceAfter)}
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>

                {/* Pagination */}
                <div className="bg-gray-50 px-4 py-3 border-t border-gray-200 flex items-center justify-between">
                    <div className="flex-1 flex justify-between sm:hidden">
                        <button
                            onClick={() => setPage(p => Math.max(1, p - 1))}
                            disabled={page === 1}
                            className="btn-secondary text-sm"
                        >
                            {t('cashbox.previous')}
                        </button>
                        <button
                            onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                            disabled={page === totalPages}
                            className="btn-secondary text-sm"
                        >
                            {t('cashbox.next')}
                        </button>
                    </div>
                    <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
                        <div>
                            <p className="text-sm text-gray-700">
                                {t('cashbox.page')} <span className="font-medium">{page}</span> {t('cashbox.of')} <span className="font-medium">{totalPages}</span>
                            </p>
                        </div>
                        <div>
                            <nav className="relative z-0 inline-flex rounded-md shadow-sm -space-x-px">
                                <button
                                    onClick={() => setPage(p => Math.max(1, p - 1))}
                                    disabled={page === 1}
                                    className="relative inline-flex items-center px-2 py-2 rounded-l-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:bg-gray-100"
                                >
                                    {t('cashbox.previous')}
                                </button>
                                <button
                                    onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                                    disabled={page === totalPages}
                                    className="relative inline-flex items-center px-2 py-2 rounded-r-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:bg-gray-100"
                                >
                                    {t('cashbox.next')}
                                </button>
                            </nav>
                        </div>
                    </div>
                </div>
            </div>

            {/* Modals */}
            {showTransactionModal && selectedCashbox?.id && (
                <CashboxTransactionModal
                    cashboxId={selectedCashbox?.id}
                    type={transactionModalType}
                    onClose={() => setShowTransactionModal(false)}
                    onSuccess={handleTransactionSuccess}
                />
            )}

            {showOpenCloseModal && selectedCashbox && (
                <OpenCloseCashboxModal
                    cashbox={selectedCashbox}
                    mode={openCloseMode}
                    onClose={() => setShowOpenCloseModal(false)}
                    onSuccess={handleOpenCloseSuccess}
                />
            )}
        </div>
    )
}

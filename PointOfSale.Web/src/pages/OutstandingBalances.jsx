import { useState, useEffect, useRef, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import { soaApi } from '../services/soaApi'
import { useToast } from '../components/Toast'
import { useNavigate } from 'react-router-dom'
import { useRTL } from '../hooks/useRTL'
import { formatDate } from '../utils/dateFormat'

const OutstandingBalances = () => {
    const { t } = useTranslation()
    const { isRTL, dir, locale } = useRTL()
    const { formatCurrency } = useSettings()
    const { showError } = useToast()
    const navigate = useNavigate()
    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const [activeTab, setActiveTab] = useState('customers')
    const [balances, setBalances] = useState([])
    const [loading, setLoading] = useState(false)
    const [searchTerm, setSearchTerm] = useState('')
    const [committedSearch, setCommittedSearch] = useState('')
    const [page, setPage] = useState(1)
    const pageSize = 50
    const [totalCount, setTotalCount] = useState(0)
    const [totalPages, setTotalPages] = useState(0)
    const [totalDebit, setTotalDebit] = useState(0)
    const [totalCredit, setTotalCredit] = useState(0)
    const [refreshCounter, setRefreshCounter] = useState(0)
    
    // AbortController ref to cancel stale requests
    const abortControllerRef = useRef(null)

    const loadOutstandingBalances = useCallback(async () => {
        // Abort any previous pending request
        if (abortControllerRef.current) {
            abortControllerRef.current.abort()
        }
        
        // Create new AbortController for this request
        const controller = new AbortController()
        abortControllerRef.current = controller
        
        setLoading(true)
        try {
            const partyType = activeTab === 'customers' ? 'Customer' : 'Supplier'
            const data = await soaApi.getOutstandingBalances(partyType, {
                page,
                pageSize,
                search: committedSearch,
                signal: controller.signal,
            })
            
            // Only update state if this request wasn't aborted (still current)
            if (!controller.signal.aborted) {
                setBalances(data.items || [])
                setTotalCount(data.totalCount || 0)
                setTotalPages(data.totalPages || 0)
                setTotalDebit(data.totalDebit || 0)
                setTotalCredit(data.totalCredit || 0)
            }
        } catch (error) {
            // Don't show error toast for aborted requests
            if (error.name !== 'AbortError' && !controller.signal.aborted) {
                showError(t('accounts.outstandingBalancesFetchError'))
            }
        } finally {
            // Only clear loading if this request wasn't aborted
            if (!controller.signal.aborted) {
                setLoading(false)
            }
        }
    }, [activeTab, page, committedSearch, refreshCounter, showError, t])

    useEffect(() => {
        loadOutstandingBalances()
        
        // Cleanup: abort any pending request when component unmounts or dependencies change
        return () => {
            if (abortControllerRef.current) {
                abortControllerRef.current.abort()
            }
        }
    }, [loadOutstandingBalances])

    useEffect(() => {
        const handle = window.setTimeout(() => {
            setPage(1)
            setCommittedSearch(searchTerm.trim())
        }, 250)

        return () => window.clearTimeout(handle)
    }, [searchTerm])

    return (
        <div dir={dir} className="space-y-6">
            <div className="flex justify-between items-center">
                <h1 className="text-2xl font-bold text-gray-900">{t('accounts.outstandingBalances')}</h1>
                <button
                    onClick={() => {
                        if (page === 1) {
                            setRefreshCounter(prev => prev + 1)
                            return
                        }

                        setPage(1)
                    }}
                    className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700"
                >
                    {t('common.refresh')}
                </button>
            </div>

            {/* Tabs */}
            <div className="bg-white rounded-lg shadow">
                <div className="border-b border-gray-200">
                    <nav className="flex -mb-px">
                        <button
                            onClick={() => {
                                setPage(1)
                                setActiveTab('customers')
                            }}
                            className={`px-6 py-3 text-sm font-medium border-b-2 ${activeTab === 'customers'
                                ? 'border-blue-500 text-blue-600'
                                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                }`}
                        >
                            {t('accounts.customersOwing')}
                        </button>
                        <button
                            onClick={() => {
                                setPage(1)
                                setActiveTab('suppliers')
                            }}
                            className={`px-6 py-3 text-sm font-medium border-b-2 ${activeTab === 'suppliers'
                                ? 'border-blue-500 text-blue-600'
                                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                }`}
                        >
                            {t('accounts.suppliersOwing')}
                        </button>
                    </nav>
                </div>

                <div className="p-6">
                    {/* Summary Cards */}
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
                        <div className="bg-blue-50 p-4 rounded-lg border border-blue-200">
                            <p className="text-sm text-gray-600 mb-1">{t('common.total')} {activeTab === 'customers' ? t('accounts.customer') : t('accounts.supplier')}</p>
                            <p className="text-2xl font-bold text-gray-900">{totalCount}</p>
                        </div>
                        <div className="bg-red-50 p-4 rounded-lg border border-red-200">
                            <p className="text-sm text-gray-600 mb-1">{t('accounts.totalDebit')}</p>
                            <p className="text-2xl font-bold text-red-600">{formatCurrency(totalDebit)}</p>
                        </div>
                        <div className="bg-green-50 p-4 rounded-lg border border-green-200">
                            <p className="text-sm text-gray-600 mb-1">{t('accounts.totalCredit')}</p>
                            <p className="text-2xl font-bold text-green-600">{formatCurrency(totalCredit)}</p>
                        </div>
                    </div>

                    {/* Search */}
                    <div className="mb-6">
                        <input
                            type="text"
                            placeholder={`${t('common.search')} ${activeTab === 'customers' ? t('accounts.customer') : t('accounts.supplier')}...`}
                            value={searchTerm}
                            onChange={(e) => setSearchTerm(e.target.value)}
                            className="w-full md:w-96 px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        />
                    </div>

                    {/* Loading */}
                    {loading && (
                        <div className="text-center py-12">
                            <p className="text-gray-500">{t('common.loading')}</p>
                        </div>
                    )}

                    {/* Balances Table */}
                    {!loading && balances.length > 0 && (
                        <div className="overflow-x-auto">
                            <table className={`w-full text-sm text-gray-500 ${tableTextAlignClass}`}>
                                <thead className="text-xs text-gray-700 uppercase bg-gray-50">
                                    <tr>
                                        <th scope="col" className="px-6 py-3">{t('common.name')}</th>
                                        <th scope="col" className="px-6 py-3">{t('accounts.totalInvoiced')}</th>
                                        <th scope="col" className="px-6 py-3">{t('accounts.totalPaid')}</th>
                                        <th scope="col" className="px-6 py-3">{t('accounts.remaining')}</th>
                                        <th scope="col" className="px-6 py-3">{t('accounts.lastPaymentDate')}</th>
                                        {activeTab === 'suppliers' && (
                                            <th scope="col" className="px-6 py-3">{t('accounts.dueDate')}</th>
                                        )}
                                        <th scope="col" className="px-6 py-3">{t('common.actions')}</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {balances.map((balance) => (
                                        <tr
                                            key={balance.partyId}
                                            className="bg-white border-b hover:bg-gray-50 cursor-pointer"
                                            onClick={() => navigate(`/statement-of-account?partyType=${balance.partyType}&partyId=${balance.partyId}`)}
                                        >
                                            <td className="px-6 py-4 font-medium text-gray-900 whitespace-nowrap">
                                                {balance.partyName}
                                            </td>
                                            <td className="px-6 py-4">
                                                {formatCurrency(balance.totalInvoiced)}
                                            </td>
                                            <td className="px-6 py-4">
                                                {formatCurrency(balance.totalPaid)}
                                            </td>
                                            <td className="px-6 py-4">
                                                <span className={`font-bold ${(balance.balanceType === 'Debit' && activeTab === 'customers') ||
                                                    (balance.balanceType === 'Credit' && activeTab === 'suppliers')
                                                    ? 'text-red-600' // Owing money (Customers Debit, Suppliers Credit)
                                                    : 'text-green-600'
                                                    }`}>
                                                    {formatCurrency(balance.balance)}
                                                </span>
                                            </td>
                                            <td className="px-6 py-4">
                                                {balance.lastPaymentDate ? formatDate(balance.lastPaymentDate, locale) : '-'}
                                            </td>
                                            {activeTab === 'suppliers' && (
                                                <td className="px-6 py-4">
                                                    {balance.dueDate ? (
                                                        <span className={new Date(balance.dueDate) < new Date() ? 'text-red-600 font-bold' : ''}>
                                                            {formatDate(balance.dueDate, locale)}
                                                        </span>
                                                    ) : '-'}
                                                </td>
                                            )}
                                            <td className="px-6 py-4">
                                                <button
                                                    onClick={(e) => {
                                                        e.stopPropagation()
                                                        navigate(`/payments?partyType=${balance.partyType}&partyId=${balance.partyId}`)
                                                    }}
                                                    className="font-medium text-blue-600 hover:underline"
                                                >
                                                    {activeTab === 'customers' ? t('accounts.receivePayment') : t('accounts.makePayment')}
                                                </button>
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    )}

                    {!loading && totalPages > 1 && (
                        <div className="mt-4 flex items-center justify-between border-t border-gray-200 pt-4">
                            <div className="text-sm text-gray-600">
                                {totalCount > 0
                                    ? `${(page - 1) * pageSize + 1}-${Math.min(page * pageSize, totalCount)} / ${totalCount}`
                                    : '0 / 0'}
                            </div>
                            <div className="flex gap-2">
                                <button
                                    onClick={() => setPage((currentPage) => Math.max(1, currentPage - 1))}
                                    disabled={page === 1 || loading}
                                    className="px-4 py-2 border border-gray-300 rounded-lg text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                                >
                                    {t('common.previous')}
                                </button>
                                <button
                                    onClick={() => setPage((currentPage) => Math.min(totalPages, currentPage + 1))}
                                    disabled={page >= totalPages || loading}
                                    className="px-4 py-2 border border-gray-300 rounded-lg text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                                >
                                    {t('common.next')}
                                </button>
                            </div>
                        </div>
                    )}

                    {/* No Data */}
                    {!loading && balances.length === 0 && (
                        <div className="text-center py-12">
                            <svg
                                className="mx-auto h-12 w-12 text-gray-400"
                                fill="none"
                                stroke="currentColor"
                                viewBox="0 0 24 24"
                            >
                                <path
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeWidth={2}
                                    d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"
                                />
                            </svg>
                            <p className="mt-4 text-gray-500">
                                {committedSearch
                                    ? t('common.noData')
                                    : t('accounts.noOutstandingBalances')}
                            </p>
                        </div>
                    )}
                </div>
            </div>
        </div>
    )
}

export default OutstandingBalances

import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'
import { useSettings } from '../contexts/SettingsContext'
import { useToast } from './Toast'
import { searchCustomers } from '../services/entityLookup'
import { useAsyncLookup } from '../hooks/useAsyncLookup'

export default function CustomerSelectModal({ onSelect, onClose }) {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()
    const { showError } = useToast()
    const {
        query: search,
        setQuery: setSearch,
        options: customers,
        loading,
        error,
        hasMore,
        loadMore,
    } = useAsyncLookup({
        loadOptions: searchCustomers,
        enabled: true,
        loadOnEmptyQuery: true,
    })
    const [showCreateForm, setShowCreateForm] = useState(false)
    const [isCreating, setIsCreating] = useState(false)
    const [newCustomer, setNewCustomer] = useState({
        name: '',
        phone: '',
        email: '',
    })
    const isMountedRef = useRef(true)
    const isCreatingRef = useRef(false)

    useEffect(() => {
        isMountedRef.current = true

        return () => {
            isMountedRef.current = false
        }
    }, [])

    const commitSelection = (customer) => {
        if (!isMountedRef.current) {
            return
        }

        onSelect(customer)
        onClose()
    }

    const handleClose = () => {
        if (isCreatingRef.current) {
            return
        }

        onClose()
    }

    const handleSelectCustomer = (customer) => {
        if (isCreatingRef.current) {
            return
        }

        commitSelection(customer)
    }

    const handleWalkIn = () => {
        if (isCreatingRef.current || !isMountedRef.current) {
            return
        }

        commitSelection(null)
    }

    const handleCreateCustomer = async (event) => {
        event.preventDefault()

        if (isCreatingRef.current || !isMountedRef.current) {
            return
        }

        isCreatingRef.current = true
        setIsCreating(true)

        try {
            const response = await api.post('/customers', {
                name: newCustomer.name,
                phone: newCustomer.phone,
                email: newCustomer.email || null,
                address: null,
                taxNumber: null,
                notes: null,
            })

            commitSelection(response.data)
        } catch (error) {
            // Log only non-sensitive metadata in production
            if (process.env.NODE_ENV === 'development') {
                console.error('Failed to create customer:', error, error.response?.data)
            } else {
                console.error('Failed to create customer:', {
                    message: error.message,
                    name: error.name,
                    status: error.response?.status,
                })
            }
            if (isMountedRef.current) {
                showError(t('customerSelectModal.failedToCreate'))
            }
        } finally {
            isCreatingRef.current = false

            if (isMountedRef.current) {
                setIsCreating(false)
            }
        }
    }

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-50 p-4">
            <div className="flex max-h-[85vh] w-full max-w-2xl flex-col overflow-hidden rounded-lg bg-white shadow-xl">
                <div className="flex-1 overflow-y-auto p-6">
                    <div className="mb-6 flex items-start justify-between">
                        <div>
                            <h3 className="text-2xl font-bold text-gray-900">{t('customerSelectModal.title')}</h3>
                            <p className="mt-1 text-sm text-gray-500">{t('customerSelectModal.pressToClose')}</p>
                        </div>
                        <button
                            type="button"
                            onClick={handleClose}
                            disabled={isCreating}
                            aria-label={t('common.close')}
                            className="text-gray-400 hover:text-gray-600 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                            <svg className="h-6 w-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    </div>

                    <button
                        type="button"
                        onClick={handleWalkIn}
                        disabled={isCreating}
                        className="mb-4 w-full rounded-lg border-2 border-primary-300 bg-primary-50 p-4 transition-colors hover:bg-primary-100 disabled:cursor-not-allowed disabled:opacity-50"
                    >
                        <div className="flex items-center gap-3">
                            <div className="flex h-12 w-12 items-center justify-center rounded-full bg-primary-100 text-primary-700">
                                <svg className="h-6 w-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5.121 17.804A10.94 10.94 0 0112 15c2.608 0 5.004.915 6.879 2.447M15 11a3 3 0 11-6 0 3 3 0 016 0z" />
                                </svg>
                            </div>
                            <div className="text-left">
                                <div className="font-semibold text-primary-900">{t('customerSelectModal.walkInCustomer')}</div>
                                <div className="text-sm text-primary-700">{t('customerSelectModal.noAccountNeeded')}</div>
                            </div>
                        </div>
                    </button>

                    <div className="mb-4">
                        <input
                            type="text"
                            placeholder={t('customerSelectModal.searchPlaceholder')}
                            value={search}
                            onChange={(event) => setSearch(event.target.value)}
                            disabled={isCreating}
                            className="input w-full"
                            autoFocus
                        />
                    </div>

                    <div className="mb-4">
                        <h4 className="mb-2 text-sm font-semibold text-gray-700">
                            {search.trim() ? t('customerSelectModal.searchResults') : t('customerSelectModal.availableCustomers')}
                        </h4>

                        {loading && customers.length === 0 ? (
                            <div className="py-8 text-center text-gray-500">{t('common.loading')}</div>
                        ) : customers.length === 0 ? (
                            <div className="py-8 text-center text-gray-500">
                                {error || (search.trim() ? t('customerSelectModal.noCustomersFound') : t('customerSelectModal.noAvailableCustomers'))}
                            </div>
                        ) : (
                            <div className="overflow-hidden rounded border border-gray-200">
                                <div className="max-h-64 overflow-y-auto">
                                    {customers.map((customer) => (
                                        <button
                                            type="button"
                                            key={customer.id}
                                            onClick={() => handleSelectCustomer(customer)}
                                            disabled={isCreating}
                                            className="w-full border-b border-gray-100 p-3 text-left transition-colors hover:bg-gray-50 last:border-b-0 disabled:cursor-not-allowed disabled:opacity-50"
                                        >
                                            <div className="flex items-start justify-between gap-3">
                                                <div>
                                                    <div className="font-medium text-gray-900">{customer.name}</div>
                                                    <div className="text-sm text-gray-500">
                                                        {customer.phone} {customer.email ? `• ${customer.email}` : ''}
                                                    </div>
                                                </div>
                                                {customer.balance > 0 ? (
                                                    <div className="text-sm font-semibold text-red-600">
                                                        {t('customerSelectModal.balance')}: {formatCurrency(customer.balance)}
                                                    </div>
                                                ) : null}
                                            </div>
                                        </button>
                                    ))}
                                </div>

                                {hasMore ? (
                                    <button
                                        type="button"
                                        onClick={loadMore}
                                        disabled={loading || isCreating}
                                        className="w-full border-t border-gray-200 px-4 py-3 text-sm font-medium text-primary-700 hover:bg-primary-50 disabled:cursor-not-allowed disabled:opacity-50"
                                    >
                                        {loading ? t('common.loading') : (t('common.loadMore') || 'Load more')}
                                    </button>
                                ) : !loading && !error ? (
                                    <div role="status" className="border-t border-gray-100 px-4 py-2 text-center text-xs text-gray-500">
                                        {t('common.endOfResults')}
                                    </div>
                                ) : null}

                                {error && customers.length > 0 ? (
                                    <div role="alert" className="border-t border-red-100 bg-red-50 px-4 py-3 text-center text-sm text-red-700">
                                        {error}
                                    </div>
                                ) : null}
                            </div>
                        )}
                    </div>

                    {!showCreateForm ? (
                        <button
                            type="button"
                            onClick={() => setShowCreateForm(true)}
                            disabled={isCreating}
                            className="w-full btn-secondary disabled:cursor-not-allowed disabled:opacity-50"
                        >
                            {t('customerSelectModal.createNewCustomer')}
                        </button>
                    ) : (
                        <div className="card bg-gray-50">
                            <h4 className="mb-3 font-semibold">{t('customerSelectModal.createCustomerTitle')}</h4>
                            <form onSubmit={handleCreateCustomer} className="space-y-3">
                                <div>
                                    <label className="label">{t('customerSelectModal.nameRequired')}</label>
                                    <input
                                        type="text"
                                        value={newCustomer.name}
                                        onChange={(event) => setNewCustomer(currentCustomer => ({ ...currentCustomer, name: event.target.value }))}
                                        disabled={isCreating}
                                        className="input w-full"
                                        required
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('customerSelectModal.phoneRequired')}</label>
                                    <input
                                        type="tel"
                                        value={newCustomer.phone}
                                        onChange={(event) => setNewCustomer(currentCustomer => ({ ...currentCustomer, phone: event.target.value }))}
                                        disabled={isCreating}
                                        className="input w-full"
                                        required
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('customerSelectModal.emailOptional')}</label>
                                    <input
                                        type="email"
                                        value={newCustomer.email}
                                        onChange={(event) => setNewCustomer(currentCustomer => ({ ...currentCustomer, email: event.target.value }))}
                                        disabled={isCreating}
                                        className="input w-full"
                                    />
                                </div>
                                <div className="flex gap-2">
                                    <button
                                        type="button"
                                        onClick={() => setShowCreateForm(false)}
                                        disabled={isCreating}
                                        className="flex-1 btn-secondary disabled:cursor-not-allowed disabled:opacity-50"
                                    >
                                        {t('common.cancel')}
                                    </button>
                                    <button
                                        type="submit"
                                        disabled={isCreating}
                                        className="flex-1 btn-primary disabled:cursor-not-allowed disabled:opacity-50"
                                    >
                                        {isCreating ? t('common.loading') : t('customerSelectModal.createAndSelect')}
                                    </button>
                                </div>
                            </form>
                        </div>
                    )}
                </div>
            </div>
        </div>
    )
}

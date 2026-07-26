import React, { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus } from 'lucide-react'
import AccountCard from '../components/AccountCard'
import { useWhatsAppApi } from '../hooks/useWhatsAppApi'
import { useToast } from '../../../components/Toast'

export default function WhatsAppAccountsPage() {
    const { t } = useTranslation()
    const { getAccounts, loading, error } = useWhatsAppApi()
    const { showError } = useToast()
    const [accounts, setAccounts] = useState([])

    const fetchAccounts = async () => {
        try {
            const data = await getAccounts()
            setAccounts(data || [])
        } catch (err) {
            // Error handled by hook state, but we show toast too
            showError(err.message)
        }
    }

    useEffect(() => {
        fetchAccounts()
    }, [])

    if (loading && accounts.length === 0) {
        return <div className="p-8 text-center text-gray-500">{t('whatsapp.accounts.loading')}</div>
    }

    if (error && accounts.length === 0) {
        return (
            <div className="p-8 text-center">
                <p className="text-red-500 mb-4">{error}</p>
                <button onClick={fetchAccounts} className="text-indigo-600 hover:text-indigo-900">{t('common.retry')}</button>
            </div>
        )
    }

    return (
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
            <div className="sm:flex sm:items-center">
                <div className="sm:flex-auto">
                    <h1 className="text-2xl font-semibold text-gray-900">{t('whatsapp.accounts.title')}</h1>
                    <p className="mt-2 text-sm text-gray-700">
                        {t('whatsapp.accounts.subtitle')}
                    </p>
                </div>
                <div className="mt-4 sm:mt-0 sm:ml-16 sm:flex-none">
                    <button
                        type="button"
                        disabled
                        className="inline-flex items-center justify-center rounded-md border border-transparent bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
                        title={t('whatsapp.accounts.contactSupport')}
                    >
                        <Plus className="-ml-1 mr-2 h-5 w-5" />
                        {t('whatsapp.accounts.actions.addAccount')}
                    </button>
                </div>
            </div>

            <div className="mt-8 grid gap-6 sm:grid-cols-1 lg:grid-cols-2">
                {accounts.length > 0 ? (
                    accounts.map(account => (
                        <AccountCard
                            key={account.id}
                            account={account}
                            onRefresh={fetchAccounts}
                        />
                    ))
                ) : (
                    <div className="col-span-full text-center py-12 bg-white rounded-lg border border-dashed border-gray-300">
                        <p className="text-gray-500">{t('whatsapp.accounts.emptyTitle')}</p>
                        <p className="text-sm text-gray-400 mt-1">{t('whatsapp.accounts.contactSupport')}</p>
                    </div>
                )}
            </div>

            <div className="mt-8 bg-blue-50 border-l-4 border-blue-400 p-4">
                <div className="flex">
                    <div className="flex-shrink-0">
                        <svg className="h-5 w-5 text-blue-400" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                            <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z" clipRule="evenodd" />
                        </svg>
                    </div>
                    <div className="ml-3">
                        <p className="text-sm text-blue-700">
                            <strong>{t('whatsapp.accounts.noteTitle')}:</strong> {t('whatsapp.accounts.noteDescription')}
                        </p>
                    </div>
                </div>
            </div>
        </div>
    )
}

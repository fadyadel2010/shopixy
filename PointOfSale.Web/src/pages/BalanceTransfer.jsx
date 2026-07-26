import { useState, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import { useToast } from '../components/Toast'
import api from '../services/api'
import { getLocaleForLanguage } from '../utils/dateFormat'
import { toSafeNumber } from '../utils/financialMath'

export default function BalanceTransfer() {
    const { t, i18n } = useTranslation()
    const { formatCurrency, settings } = useSettings()
    const { showSuccess, showError } = useToast()
    const [accounts, setAccounts] = useState([])
    const [cashboxes, setCashboxes] = useState([])
    const [loading, setLoading] = useState(false)
    const [loadError, setLoadError] = useState(null)
    const isRTL = i18n.language === 'ar'
    const submissionLockRef = useRef(false)
    const requestIdRef = useRef(0)
    const currencyLocale = getLocaleForLanguage(i18n.resolvedLanguage || i18n.language)
    const currencyAffixes = (() => {
        const fallbackToken = settings?.currencySymbol || settings?.currencyCode || ''

        try {
            const currencyParts = new Intl.NumberFormat(currencyLocale, {
                style: 'currency',
                currency: settings?.currencyCode || 'EGP',
                minimumFractionDigits: 2,
                maximumFractionDigits: 2,
            }).formatToParts(0)
            const firstNumericPartIndex = currencyParts.findIndex((part) => part.type === 'integer' || part.type === 'fraction')
            let lastNumericPartIndex = -1

            for (let index = currencyParts.length - 1; index >= 0; index -= 1) {
                if (currencyParts[index].type === 'integer' || currencyParts[index].type === 'fraction') {
                    lastNumericPartIndex = index
                    break
                }
            }

            if (firstNumericPartIndex === -1 || lastNumericPartIndex === -1) {
                return { leading: fallbackToken, trailing: '' }
            }

            const leading = currencyParts
                .slice(0, firstNumericPartIndex)
                .map((part) => part.value)
                .join('')
            const trailing = currencyParts
                .slice(lastNumericPartIndex + 1)
                .map((part) => part.value)
                .join('')

            if (leading || trailing) {
                return { leading, trailing }
            }

            const currencyToken = currencyParts
                .filter((part) => part.type === 'currency')
                .map((part) => part.value)
                .join('')

            return { leading: currencyToken || fallbackToken, trailing: '' }
        } catch {
            return { leading: fallbackToken, trailing: '' }
        }
    })()

    const [formData, setFormData] = useState({
        sourceType: 'Account', // Account or Cashbox
        sourceId: '',
        destinationType: 'Cashbox', // Account or Cashbox
        destinationId: '',
        amount: '',
        description: ''
    })

    useEffect(() => {
        fetchData().catch((err) => {
            setLoadError(err)
            showError(t('balanceTransfer.loadError', { defaultValue: 'Failed to load accounts. Please refresh the page.' }))
        })

        return () => {
            requestIdRef.current += 1
        }
    }, [])

    const fetchData = async () => {
        const requestId = ++requestIdRef.current
        try {
            const [accountsRes, cashboxesRes] = await Promise.all([
                api.get('/financialaccounts'),
                api.get('/cashbox')
            ])

            if (requestId !== requestIdRef.current) {
                return
            }

            setAccounts(accountsRes.data)

            // Cashbox endpoint might return paginated result or list. 
            const cashboxData = cashboxesRes.data
            setCashboxes(Array.isArray(cashboxData) ? cashboxData : (cashboxData.items || []))
        } catch (err) {
            if (requestId !== requestIdRef.current) {
                return
            }

            console.error(err)
            throw err
        }
    }

    const handleSubmit = async (e) => {
        e.preventDefault()
        if (submissionLockRef.current) {
            return
        }

        submissionLockRef.current = true
        setLoading(true)
        try {
            if (!formData.sourceId) {
                showError(t('accounts.selectFromAccount'))
                return
            }

            if (!formData.destinationId) {
                showError(t('accounts.selectToAccount'))
                return
            }

            if (formData.sourceId === formData.destinationId && formData.sourceType === formData.destinationType) {
                showError(t('accounts.sameAccountError'))
                return
            }

            const amount = toSafeNumber(formData.amount, Number.NaN)
            if (!Number.isFinite(amount) || amount <= 0) {
                showError(t('treasury.invalidAmount'))
                return
            }

            // Perform the transfer in its own try/catch
            try {
                await api.post('/financialaccounts/transfer', {
                    sourceId: formData.sourceId,
                    sourceType: formData.sourceType,
                    destinationId: formData.destinationId,
                    destinationType: formData.destinationType,
                    amount,
                    description: formData.description,
                    date: new Date().toISOString()
                })
            } catch (err) {
                showError(err.response?.data?.message || t('accounts.failedToTransfer'))
                return
            }

            // Transfer succeeded - show success and clear form immediately
            showSuccess(t('accounts.balanceTransferred'))
            setFormData({
                sourceType: 'Account',
                sourceId: '',
                destinationType: 'Cashbox',
                destinationId: '',
                amount: '',
                description: ''
            })

            // Refresh data in separate try/catch - failure is non-critical
            try {
                await fetchData()
            } catch (refreshErr) {
                console.warn('Failed to refresh balances after transfer:', refreshErr)
            }
        } finally {
            submissionLockRef.current = false
            setLoading(false)
        }
    }

    const getSourceBalance = () => {
        if (!formData.sourceId) return null
        if (formData.sourceType === 'Account') {
            const acc = accounts.find(a => a.id === formData.sourceId)
            return acc ? acc.balance : 0
        } else {
            const cb = cashboxes.find(c => c.id === formData.sourceId)
            return cb ? cb.currentBalance : 0
        }
    }

    const getDestBalance = () => {
        if (!formData.destinationId) return null
        if (formData.destinationType === 'Account') {
            const acc = accounts.find(a => a.id === formData.destinationId)
            return acc ? acc.balance : 0
        } else {
            const cb = cashboxes.find(c => c.id === formData.destinationId)
            return cb ? cb.currentBalance : 0
        }
    }

    return (
        <div className="max-w-6xl mx-auto space-y-8" dir={isRTL ? 'rtl' : 'ltr'}>
            {loadError && (
                <div className="bg-red-50 border border-red-200 rounded-lg p-4 text-red-700">
                    {t('balanceTransfer.loadError', { defaultValue: 'Failed to load accounts. Please refresh the page.' })}
                </div>
            )}
            <div className="flex flex-col md:flex-row justify-between items-start md:items-center">
                <div>
                    <h1 className="text-3xl font-bold text-gray-900">{t('accounts.balanceTransfer')}</h1>
                    <p className="text-gray-500 mt-1">{t('accounts.balanceTransferSubtitle') || 'Move funds between your accounts and cashboxes'}</p>
                </div>
            </div>

            <div className="bg-white rounded-2xl shadow-xl overflow-hidden border border-gray-100">
                <div className="p-8">
                    <form onSubmit={handleSubmit} className="space-y-8">

                        <div className="flex flex-col md:flex-row gap-8 items-center">
                            {/* Source Section */}
                            <div className="flex-1 w-full bg-red-50 rounded-xl p-6 border border-red-100 transition-all hover:shadow-md">
                                <h3 className="text-lg font-bold text-red-800 mb-4 flex items-center gap-2">
                                    <span className="bg-red-200 p-1.5 rounded-lg text-red-700">📤</span>
                                    {t('accounts.fromAccount')}
                                </h3>

                                <div className="space-y-4">
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1.5">{t('accounts.sourceType') || 'Source Type'}</label>
                                        <select
                                            value={formData.sourceType}
                                            onChange={e => setFormData({ ...formData, sourceType: e.target.value, sourceId: '' })}
                                            className="w-full px-4 py-2.5 bg-white border border-gray-300 rounded-lg focus:ring-2 focus:ring-red-500 focus:border-red-500 transition-shadow"
                                        >
                                            <option value="Account">{t('accounts.mainAccounts')}</option>
                                            <option value="Cashbox">{t('nav.cashbox')}</option>
                                        </select>
                                    </div>

                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1.5">{t('accounts.selectSource') || 'Select Account/Cashbox'}</label>
                                        <select
                                            value={formData.sourceId}
                                            onChange={e => setFormData({ ...formData, sourceId: e.target.value })}
                                            className="w-full px-4 py-2.5 bg-white border border-gray-300 rounded-lg focus:ring-2 focus:ring-red-500 focus:border-red-500 transition-shadow"
                                        >
                                            <option value="">{t('accounts.selectAccount')}</option>
                                            {formData.sourceType === 'Account' ? (
                                                accounts.map(a => {
                                                    const displayName = a.title || a.accountName || a.name
                                                    const alias = a.name && a.name !== displayName ? `(${a.name})` : ''

                                                    return (
                                                        <option key={a.id} value={a.id}>{displayName} {alias} - {formatCurrency(a.balance || 0)}</option>
                                                    )
                                                })
                                            ) : (
                                                cashboxes.map(c => (
                                                    <option key={c.id} value={c.id}>{c.name} - {formatCurrency(c.currentBalance || 0)}</option>
                                                ))
                                            )}
                                        </select>
                                    </div>

                                    {formData.sourceId && (
                                        <div className="bg-white/50 rounded-lg p-3 flex justify-between items-center text-sm">
                                            <span className="text-gray-600">{t('common.currentBalance')}:</span>
                                            <span className={`font-bold ${getSourceBalance() < 0 ? 'text-red-600' : 'text-gray-900'}`}>
                                                {formatCurrency(getSourceBalance() ?? 0)}
                                            </span>
                                        </div>
                                    )}
                                </div>
                            </div>

                            {/* Arrow Indicator */}
                            <div className="flex-shrink-0 text-gray-400">
                                {isRTL ? (
                                    <svg className="w-10 h-10 transform rotate-180 md:rotate-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10 19l-7-7m0 0l7-7m-7 7h18" />
                                    </svg>
                                ) : (
                                    <svg className="w-10 h-10 transform rotate-90 md:rotate-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M14 5l7 7m0 0l-7 7m7-7H3" />
                                    </svg>
                                )}
                            </div>

                            {/* Destination Section */}
                            <div className="flex-1 w-full bg-green-50 rounded-xl p-6 border border-green-100 transition-all hover:shadow-md">
                                <h3 className="text-lg font-bold text-green-800 mb-4 flex items-center gap-2">
                                    <span className="bg-green-200 p-1.5 rounded-lg text-green-700">📥</span>
                                    {t('accounts.toAccount')}
                                </h3>

                                <div className="space-y-4">
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1.5">{t('accounts.destinationType') || 'Destination Type'}</label>
                                        <select
                                            value={formData.destinationType}
                                            onChange={e => setFormData({ ...formData, destinationType: e.target.value, destinationId: '' })}
                                            className="w-full px-4 py-2.5 bg-white border border-gray-300 rounded-lg focus:ring-2 focus:ring-green-500 focus:border-green-500 transition-shadow"
                                        >
                                            <option value="Account">{t('accounts.mainAccounts')}</option>
                                            <option value="Cashbox">{t('nav.cashbox')}</option>
                                        </select>
                                    </div>

                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1.5">{t('accounts.selectDestination') || 'Select Account/Cashbox'}</label>
                                        <select
                                            value={formData.destinationId}
                                            onChange={e => setFormData({ ...formData, destinationId: e.target.value })}
                                            className="w-full px-4 py-2.5 bg-white border border-gray-300 rounded-lg focus:ring-2 focus:ring-green-500 focus:border-green-500 transition-shadow"
                                        >
                                            <option value="">{t('accounts.selectAccount')}</option>
                                            {formData.destinationType === 'Account' ? (
                                                accounts.map(a => {
                                                    const displayName = a.title || a.accountName || a.name
                                                    const alias = a.name && a.name !== displayName ? `(${a.name})` : ''

                                                    return (
                                                        <option key={a.id} value={a.id}>{displayName} {alias} - {formatCurrency(a.balance || 0)}</option>
                                                    )
                                                })
                                            ) : (
                                                cashboxes.map(c => (
                                                    <option key={c.id} value={c.id}>{c.name} - {formatCurrency(c.currentBalance || 0)}</option>
                                                ))
                                            )}
                                        </select>
                                    </div>

                                    {formData.destinationId && (
                                        <div className="bg-white/50 rounded-lg p-3 flex justify-between items-center text-sm">
                                            <span className="text-gray-600">{t('common.currentBalance')}:</span>
                                            <span className={`font-bold ${getDestBalance() < 0 ? 'text-red-600' : 'text-gray-900'}`}>
                                                {formatCurrency(getDestBalance() ?? 0)}
                                            </span>
                                        </div>
                                    )}
                                </div>
                            </div>
                        </div>

                        <hr className="border-gray-100" />

                        {/* Transfer Details */}
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-8">
                            <div>
                                <label className="block text-sm font-medium text-gray-700 mb-2">{t('cashbox.amount')}</label>
                                <div className="flex items-center w-full bg-white border border-gray-300 rounded-lg shadow-sm focus-within:ring-2 focus-within:ring-blue-500 focus-within:border-blue-500 transition-shadow">
                                    {currencyAffixes.leading && (
                                        <span className="pl-3 text-gray-500 sm:text-lg font-bold whitespace-pre pointer-events-none">
                                            {currencyAffixes.leading}
                                        </span>
                                    )}
                                    <input
                                        type="number"
                                        step="0.01"
                                        min="0.01"
                                        required
                                        value={formData.amount}
                                        onChange={e => setFormData({ ...formData, amount: e.target.value })}
                                        className="min-w-0 flex-1 bg-transparent px-4 py-3 text-lg font-semibold border-0 rounded-lg focus:outline-none focus:ring-0"
                                        placeholder="0.00"
                                    />
                                    {currencyAffixes.trailing && (
                                        <span className="pr-3 text-gray-500 sm:text-lg font-bold whitespace-pre pointer-events-none">
                                            {currencyAffixes.trailing}
                                        </span>
                                    )}
                                </div>
                            </div>

                            <div>
                                <label className="block text-sm font-medium text-gray-700 mb-2">{t('cashbox.description')}</label>
                                <textarea
                                    rows={2}
                                    value={formData.description}
                                    onChange={e => setFormData({ ...formData, description: e.target.value })}
                                    className="w-full px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500 transition-shadow"
                                    placeholder={t('accounts.notesPlaceholder')}
                                />
                            </div>
                        </div>

                        <div className="flex justify-end pt-4">
                            <button
                                type="submit"
                                disabled={loading}
                                className="w-full md:w-auto px-8 py-3 bg-gradient-to-r from-blue-600 to-indigo-600 text-white font-bold rounded-xl shadow-lg hover:shadow-xl hover:from-blue-700 hover:to-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 transition-all disabled:opacity-50 disabled:cursor-not-allowed transform hover:-translate-y-0.5"
                            >
                                {loading ? (
                                    <div className="flex items-center gap-2">
                                        <svg className="animate-spin h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                                            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                                            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                                        </svg>
                                        {t('common.processing')}
                                    </div>
                                ) : (
                                    <div className="flex items-center gap-2">
                                        <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 10V3L4 14h7v7l9-11h-7z" />
                                        </svg>
                                        {t('accounts.transferBalance')}
                                    </div>
                                )}
                            </button>
                        </div>
                    </form>
                </div>
            </div>
        </div>
    )
}

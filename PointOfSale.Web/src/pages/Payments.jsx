import { useState, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import { paymentsApi, soaApi } from '../services/soaApi'
import { useToast } from '../components/Toast'
import api, { isRequestCanceled } from '../services/api'
import SOAPaymentModal from '../components/SOAPaymentModal'
import { useRTL } from '../hooks/useRTL'
import { formatDateTime } from '../utils/dateFormat'
import { toSafeNumber } from '../utils/financialMath'
import { getPartyBalanceColorClass } from '../utils/partyBalancePresentation'
import { stableStringify } from '../utils/requestSafety'
import {
    getSupplierCardErrorMessage,
    resolveSupplierCardAttempt,
    supplierCardFundingApi,
} from '../services/supplierCardFundingApi'

const Payments = () => {
    const { t } = useTranslation()
    const { isRTL, dir, locale } = useRTL()
    const { formatCurrency } = useSettings()
    const { showSuccess, showError } = useToast()
    const [activeTab, setActiveTab] = useState('receive') // 'receive' or 'make'
    const [parties, setParties] = useState([])
    const [selectedParty, setSelectedParty] = useState(null)
    const [partyBalance, setPartyBalance] = useState(null)
    const [paymentHistory, setPaymentHistory] = useState([])
    const [cardInstruments, setCardInstruments] = useState([])
    const [cardInstrumentsLoading, setCardInstrumentsLoading] = useState(false)
    const [cardInstrumentsError, setCardInstrumentsError] = useState('')
    const [showPaymentModal, setShowPaymentModal] = useState(false)
    const [loading, setLoading] = useState(false)
    const submissionLockRef = useRef(false)
    const supplierPaymentAttemptRef = useRef(null)

    const partyType = activeTab === 'receive' ? 'Customer' : 'Supplier'
    const isBusy = loading

    useEffect(() => {
        setSelectedParty(null)
        const controller = new AbortController()
        void loadParties(controller.signal)

        return () => controller.abort()
    }, [activeTab])

    useEffect(() => {
        if (activeTab !== 'make') {
            setCardInstruments([])
            setCardInstrumentsError('')
            setCardInstrumentsLoading(false)
            return
        }

        const controller = new AbortController()
        setCardInstrumentsLoading(true)
        setCardInstrumentsError('')
        supplierCardFundingApi.list({ activeOnly: true, signal: controller.signal })
            .then(setCardInstruments)
            .catch(error => {
                if (!isRequestCanceled(error)) {
                    const message = getSupplierCardErrorMessage(error, t, 'purchases.failedToLoadCardInstruments')
                    setCardInstruments([])
                    setCardInstrumentsError(message)
                    showError(message)
                }
            })
            .finally(() => {
                if (!controller.signal.aborted) setCardInstrumentsLoading(false)
            })
        return () => controller.abort()
    }, [activeTab, showError, t])

    useEffect(() => {
        if (selectedParty) {
            const controller = new AbortController()
            void Promise.all([
                loadPartyBalance(controller.signal),
                loadPaymentHistory(controller.signal),
            ])

            return () => controller.abort()
        } else {
            setPartyBalance(null)
            setPaymentHistory([])
        }
    }, [selectedParty, activeTab])

    const loadParties = async (signal) => {
        try {
            const endpoint = activeTab === 'receive' ? '/Customers' : '/Suppliers'
            const response = await api.get(endpoint, { signal })

            if (signal?.aborted) {
                return
            }

            setParties(response.data.items || response.data)
        } catch (error) {
            if (isRequestCanceled(error)) {
                return
            }

            showError(t('accounts.failedToLoadParties'))
        }
    }

    const loadPartyBalance = async (signal) => {
        try {
            const data = await soaApi.getPartyBalance(partyType, selectedParty, { signal })

            if (signal?.aborted) {
                return
            }

            setPartyBalance(data)
        } catch (error) {
            if (isRequestCanceled(error)) {
                return
            }

            showError(t('accounts.failedToLoadBalance'))
        }
    }

    const loadPaymentHistory = async (signal) => {
        try {
            const data = activeTab === 'receive'
                ? await paymentsApi.getCustomerPayments(selectedParty, { signal })
                : await paymentsApi.getSupplierPayments(selectedParty, { signal })

            if (signal?.aborted) {
                return
            }

            setPaymentHistory(data)
        } catch (error) {
            if (isRequestCanceled(error)) {
                return
            }

            showError(t('accounts.failedToLoadPaymentHistory'))
        }
    }

    const handlePaymentSubmit = async (paymentData) => {
        if (submissionLockRef.current) {
            return
        }

        submissionLockRef.current = true
        setLoading(true)
        const refreshController = new AbortController()
        try {
            const amount = toSafeNumber(paymentData.amount, Number.NaN)
            const hasCashAmount = paymentData.cashAmount !== undefined
            const cashAmount = hasCashAmount
                ? toSafeNumber(paymentData.cashAmount, Number.NaN)
                : undefined

            if (!Number.isFinite(amount) || amount <= 0 || (hasCashAmount && (!Number.isFinite(cashAmount) || cashAmount < 0))) {
                showError(t('treasury.invalidAmount'))
                return
            }

            const sanitizedPaymentData = {
                ...paymentData,
                amount,
                ...(hasCashAmount ? { cashAmount } : {}),
            }

            if (activeTab === 'receive') {
                await paymentsApi.receivePayment({
                    customerId: selectedParty,
                    ...sanitizedPaymentData,
                })
                showSuccess(t('accounts.paymentReceivedSuccess'))
            } else {
                const fingerprint = stableStringify({ supplierId: selectedParty, ...sanitizedPaymentData })
                const attempt = resolveSupplierCardAttempt(
                    supplierPaymentAttemptRef.current,
                    fingerprint,
                    'supplier-payment',
                )
                supplierPaymentAttemptRef.current = attempt
                await paymentsApi.makePayment({
                    supplierId: selectedParty,
                    ...sanitizedPaymentData,
                }, { idempotencyKey: attempt.idempotencyKey })
                supplierPaymentAttemptRef.current = null
                showSuccess(t('accounts.paymentMadeSuccess'))
            }
            setShowPaymentModal(false)
            await Promise.all([
                loadPartyBalance(refreshController.signal),
                loadPaymentHistory(refreshController.signal)
            ])
        } catch (error) {
            showError(activeTab === 'make'
                ? getSupplierCardErrorMessage(error, t, 'accounts.failedToProcessPayment')
                : (error.response?.data?.message || t('accounts.failedToProcessPayment')))
        } finally {
            submissionLockRef.current = false
            setLoading(false)
        }
    }

    return (
        <div className="space-y-6" dir={dir}>
            <div className={`flex items-center justify-between ${isRTL ? 'flex-row-reverse' : ''}`}>
                <h1 className={`text-2xl font-bold text-gray-900 ${isRTL ? 'text-right' : 'text-left'}`}>{t('accounts.payments')}</h1>
            </div>

            {/* Tabs */}
            <div className="bg-white rounded-lg shadow">
                <div className="border-b border-gray-200">
                    <nav className={`flex -mb-px ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <button
                            onClick={() => setActiveTab('receive')}
                            disabled={isBusy}
                            className={`px-6 py-3 text-sm font-medium border-b-2 ${activeTab === 'receive'
                                ? 'border-blue-500 text-blue-600'
                                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                } disabled:cursor-not-allowed disabled:opacity-60`}
                        >
                            {t('accounts.receivePayment')} ({t('accounts.customer')})
                        </button>
                        <button
                            onClick={() => setActiveTab('make')}
                            disabled={isBusy}
                            className={`px-6 py-3 text-sm font-medium border-b-2 ${activeTab === 'make'
                                ? 'border-blue-500 text-blue-600'
                                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                } disabled:cursor-not-allowed disabled:opacity-60`}
                        >
                            {t('accounts.makePayment')} ({t('accounts.supplier')})
                        </button>
                    </nav>
                </div>

                <div className="p-6">
                    {/* Party Selection */}
                    <div className="mb-6">
                        <label className={`block text-sm font-medium text-gray-700 mb-2 ${isRTL ? 'text-right' : 'text-left'}`}>
                            {t('accounts.selectParty')} {activeTab === 'receive' ? t('accounts.customer') : t('accounts.supplier')}
                        </label>
                        <select
                            value={selectedParty || ''}
                            onChange={(e) => setSelectedParty(e.target.value)}
                            className={`w-full md:w-96 px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 ${isRTL ? 'text-right' : 'text-left'}`}
                            disabled={isBusy}
                        >
                            <option value="">{t('accounts.chooseParty')} {activeTab === 'receive' ? t('accounts.customer') : t('accounts.supplier')}...</option>
                            {parties.map((party) => (
                                <option key={party.id} value={party.id}>
                                    {party.name}
                                </option>
                            ))}
                        </select>
                    </div>

                    {/* Balance Card */}
                    {partyBalance && (
                        <div className="mb-6">
                            <div className="bg-gradient-to-r from-blue-50 to-blue-100 rounded-lg p-6 border border-blue-200">
                                <div className={`flex justify-between items-center gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                    <div className={isRTL ? 'text-right' : 'text-left'}>
                                        <h3 className="text-lg font-semibold text-gray-900 mb-1">
                                            {partyBalance.partyName}
                                        </h3>
                                        <p className="text-sm text-gray-600">{t('accounts.currentBalance')}</p>
                                    </div>
                                    <div className={isRTL ? 'text-left' : 'text-right'}>
                                        <p className={`text-3xl font-bold ${getPartyBalanceColorClass(partyType, partyBalance.balanceType)}`}>
                                            {formatCurrency(partyBalance.balance)}
                                        </p>
                                        <p className="text-sm text-gray-600 mt-1">
                                            {partyBalance.balanceType === 'Debit' ? t('accounts.debit') : t('accounts.credit')}
                                        </p>
                                    </div>
                                </div>
                                <div className="mt-4">
                                    <button
                                        onClick={() => setShowPaymentModal(true)}
                                        disabled={isBusy}
                                        className="px-6 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-60"
                                    >
                                        {activeTab === 'receive' ? t('accounts.receivePayment') : t('accounts.makePayment')}
                                    </button>
                                </div>
                            </div>
                        </div>
                    )}

                    {/* Payment History */}
                    {selectedParty && paymentHistory.length > 0 && (
                        <div>
                            <h3 className="text-lg font-semibold text-gray-900 mb-4">{t('accounts.paymentHistory')}</h3>
                            <div className="overflow-x-auto">
                                <table className="min-w-full divide-y divide-gray-200">
                                    <thead className="bg-gray-50">
                                        <tr>
                                            <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${isRTL ? 'text-right' : 'text-left'}`}>
                                                {t('common.date')}
                                            </th>
                                            <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${isRTL ? 'text-right' : 'text-left'}`}>
                                                {t('accounts.referenceNumber')}
                                            </th>
                                            <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${isRTL ? 'text-left' : 'text-right'}`}>
                                                {t('common.amount')}
                                            </th>
                                            <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${isRTL ? 'text-right' : 'text-left'}`}>
                                                {t('accounts.notes')}
                                            </th>
                                        </tr>
                                    </thead>
                                    <tbody className="bg-white divide-y divide-gray-200">
                                        {paymentHistory.map((payment) => (
                                            <tr key={payment.id} className="hover:bg-gray-50">
                                                <td className={`px-6 py-4 whitespace-nowrap text-sm text-gray-900 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                    {formatDateTime(payment.transactionDate, locale)}
                                                </td>
                                                <td className={`px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                    {payment.referenceNumber}
                                                </td>
                                                <td className={`px-6 py-4 whitespace-nowrap text-sm font-bold text-green-600 ${isRTL ? 'text-left' : 'text-right'}`}>
                                                    {formatCurrency(payment.amount)}
                                                </td>
                                                <td className={`px-6 py-4 text-sm text-gray-500 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                    {payment.notes || t('common.none')}
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        </div>
                    )}

                    {/* No Party Selected */}
                    {!selectedParty && (
                        <div className="text-center py-12">
                            <p className={`text-gray-500 ${isRTL ? 'text-right' : 'text-left'}`}>
                                {t('accounts.selectPartyToView')}
                            </p>
                        </div>
                    )}

                    {/* No Payment History */}
                    {selectedParty && paymentHistory.length === 0 && (
                        <div className="text-center py-12">
                            <p className={`text-gray-500 ${isRTL ? 'text-right' : 'text-left'}`}>{t('accounts.noPaymentHistory')}</p>
                        </div>
                    )}
                </div>
            </div>

            {/* Payment Modal */}
            {showPaymentModal && (
                <SOAPaymentModal
                    isOpen={showPaymentModal}
                    onClose={() => setShowPaymentModal(false)}
                    onSubmit={handlePaymentSubmit}
                    partyName={partyBalance?.partyName}
                    currentBalance={partyBalance?.balance}
                    balanceType={partyBalance?.balanceType}
                    type={activeTab}
                    loading={loading}
                    cardInstruments={cardInstruments}
                    cardInstrumentsLoading={cardInstrumentsLoading}
                    cardInstrumentsError={cardInstrumentsError}
                />
            )}
        </div>
    )
}

export default Payments

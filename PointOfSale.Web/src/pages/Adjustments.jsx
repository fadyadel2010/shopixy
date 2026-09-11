import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { soaApi } from '../services/soaApi'
import { useToast } from '../components/Toast'
import api from '../services/api'
import AdjustmentModal from '../components/AdjustmentModal'
import { toSafeNumber } from '../utils/financialMath'
import { useFormatting } from '../hooks/useFormatting'
import { stableStringify } from '../utils/requestSafety'
import { getAdjustmentEffectTranslationKey, getPartyBalanceColorClass } from '../utils/partyBalancePresentation'

const createAdjustmentIdempotencyKey = () => {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return `soa-adjustment-${crypto.randomUUID()}`
    }

    return `soa-adjustment-${Date.now()}-${Math.random().toString(36).slice(2)}`
}

const Adjustments = () => {
    const { t } = useTranslation()
    const { formatCurrency } = useFormatting()
    const { showSuccess, showError } = useToast()
    const [partyType, setPartyType] = useState('Customer')
    const [parties, setParties] = useState([])
    const [selectedParty, setSelectedParty] = useState(null)
    const [partyBalance, setPartyBalance] = useState(null)
    const [showAdjustmentModal, setShowAdjustmentModal] = useState(false)
    const [loading, setLoading] = useState(false)
    const submissionLockRef = useRef(false)
    const lastSubmissionRef = useRef(null)
    const partiesRequestIdRef = useRef(0)
    const balanceRequestIdRef = useRef(0)

    const partyLabel = partyType === 'Customer' ? t('accounts.customer') : t('accounts.supplier')

    useEffect(() => {
        return () => {
            partiesRequestIdRef.current += 1
            balanceRequestIdRef.current += 1
        }
    }, [])

    const loadParties = useCallback(async (nextPartyType) => {
        const requestId = ++partiesRequestIdRef.current

        try {
            const endpoint = nextPartyType === 'Customer' ? '/Customers' : '/Suppliers'
            const response = await api.get(endpoint)

            if (requestId !== partiesRequestIdRef.current) {
                return
            }

            setParties(response.data.items || response.data)
        } catch (error) {
            if (requestId === partiesRequestIdRef.current) {
                showError(t('accounts.failedToLoadParties'))
            }
        }
    }, [showError, t])

    const loadPartyBalance = useCallback(async (partyId, nextPartyType) => {
        if (!partyId) {
            balanceRequestIdRef.current += 1
            setPartyBalance(null)
            return
        }

        const requestId = ++balanceRequestIdRef.current

        try {
            const data = await soaApi.getPartyBalance(nextPartyType, partyId)

            if (requestId !== balanceRequestIdRef.current) {
                return
            }

            setPartyBalance(data)
        } catch (error) {
            if (requestId === balanceRequestIdRef.current) {
                showError(t('accounts.failedToLoadBalance'))
            }
        }
    }, [showError, t])

    useEffect(() => {
        setParties([])
        setSelectedParty(null)
        setPartyBalance(null)
        void loadParties(partyType)
    }, [loadParties, partyType])

    useEffect(() => {
        if (selectedParty) {
            void loadPartyBalance(selectedParty, partyType)
        } else {
            balanceRequestIdRef.current += 1
            setPartyBalance(null)
        }
    }, [loadPartyBalance, partyType, selectedParty])

    const handleAdjustmentSubmit = async (adjustmentData) => {
        if (submissionLockRef.current) {
            return
        }

        submissionLockRef.current = true
        setLoading(true)
        try {
            const amount = toSafeNumber(adjustmentData.amount, Number.NaN)

            if (!Number.isFinite(amount) || amount <= 0) {
                showError(t('treasury.invalidAmount'))
                return
            }

            const payload = {
                partyType,
                partyId: selectedParty,
                ...adjustmentData,
                amount,
            }
            const fingerprint = stableStringify(payload)
            const idempotencyKey = lastSubmissionRef.current?.fingerprint === fingerprint
                ? lastSubmissionRef.current.key
                : createAdjustmentIdempotencyKey()

            lastSubmissionRef.current = {
                key: idempotencyKey,
                fingerprint,
            }

            await soaApi.createAdjustment(payload, { idempotencyKey })
            showSuccess(t('accounts.adjustmentCreated'))
            setShowAdjustmentModal(false)
            await loadPartyBalance(selectedParty, partyType)
        } catch (error) {
            showError(t('accounts.failedToCreateAdjustment'))
        } finally {
            submissionLockRef.current = false
            setLoading(false)
        }
    }

    return (
        <div className="space-y-6">
            <div className="flex justify-between items-center">
                <h1 className="text-2xl font-bold text-gray-900">{t('accounts.accountAdjustments')}</h1>
            </div>

            <div className="bg-white rounded-lg shadow p-6">
                <div className="space-y-6">
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('accounts.partyType')}
                        </label>
                        <div className="flex gap-4">
                            <label className="flex items-center gap-2">
                                <input
                                    type="radio"
                                    value="Customer"
                                    checked={partyType === 'Customer'}
                                    onChange={(event) => setPartyType(event.target.value)}
                                />
                                <span>{t('accounts.customer')}</span>
                            </label>
                            <label className="flex items-center gap-2">
                                <input
                                    type="radio"
                                    value="Supplier"
                                    checked={partyType === 'Supplier'}
                                    onChange={(event) => setPartyType(event.target.value)}
                                />
                                <span>{t('accounts.supplier')}</span>
                            </label>
                        </div>
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('accounts.selectPartyWithLabel', { label: partyLabel })}
                        </label>
                        <select
                            value={selectedParty || ''}
                            onChange={(event) => setSelectedParty(event.target.value)}
                            className="w-full md:w-96 px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        >
                            <option value="">{t('accounts.choosePartyWithLabel', { label: partyLabel })}</option>
                            {parties.map((party) => (
                                <option key={party.id} value={party.id}>
                                    {party.name}
                                </option>
                            ))}
                        </select>
                    </div>

                    {partyBalance && (
                        <div className="bg-gradient-to-r from-purple-50 to-purple-100 rounded-lg p-6 border border-purple-200">
                            <div className="flex justify-between items-center mb-4">
                                <div>
                                    <h3 className="text-lg font-semibold text-gray-900 mb-1">
                                        {partyBalance.partyName}
                                    </h3>
                                    <p className="text-sm text-gray-600">{t('accounts.currentBalance')}</p>
                                </div>
                                <div className="text-right">
                                    <p className={`text-3xl font-bold ${getPartyBalanceColorClass(partyType, partyBalance.balanceType)}`}>
                                        {formatCurrency(partyBalance.balance)}
                                    </p>
                                    <p className="text-sm text-gray-600 mt-1">
                                        {partyBalance.balanceType === 'Debit' ? t('accounts.debit') : t('accounts.credit')}
                                    </p>
                                </div>
                            </div>
                            <button
                                onClick={() => setShowAdjustmentModal(true)}
                                className="px-6 py-2 bg-purple-600 text-white rounded-lg hover:bg-purple-700"
                            >
                                {t('accounts.createAdjustment')}
                            </button>
                        </div>
                    )}

                    <div className="bg-blue-50 border border-blue-200 rounded-lg p-4">
                        <h4 className="font-semibold text-blue-900 mb-2">{t('accounts.adjustmentTypes')}:</h4>
                        <ul className="space-y-2 text-sm text-blue-800">
                            <li>
                                <strong>{t('accounts.creditNote')}:</strong> {t(getAdjustmentEffectTranslationKey(partyType, 'CreditNote'))}
                            </li>
                            <li>
                                <strong>{t('accounts.debitNote')}:</strong> {t(getAdjustmentEffectTranslationKey(partyType, 'DebitNote'))}
                            </li>
                            <li>
                                <strong>{t('accounts.creditAdjustment')}:</strong> {t(getAdjustmentEffectTranslationKey(partyType, 'CreditAdjustment'))}
                            </li>
                            <li>
                                <strong>{t('accounts.debitAdjustment')}:</strong> {t(getAdjustmentEffectTranslationKey(partyType, 'DebitAdjustment'))}
                            </li>
                        </ul>
                    </div>

                    {!selectedParty && (
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
                                    d="M12 6v6m0 0v6m0-6h6m-6 0H6"
                                />
                            </svg>
                            <p className="mt-4 text-gray-500">
                                {t('accounts.selectPartyToAdjust', { party: partyLabel })}
                            </p>
                        </div>
                    )}
                </div>
            </div>

            {showAdjustmentModal && (
                <AdjustmentModal
                    isOpen={showAdjustmentModal}
                    onClose={() => setShowAdjustmentModal(false)}
                    onSubmit={handleAdjustmentSubmit}
                    partyName={partyBalance?.partyName}
                    partyType={partyType}
                    loading={loading}
                />
            )}
        </div>
    )
}

export default Adjustments

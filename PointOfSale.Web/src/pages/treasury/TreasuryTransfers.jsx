/**
 * Treasury Transfers UI
 * 
 * ENTERPRISE FINANCIAL CONTROL SCREEN
 * 
 * This screen allows administrators to settle clearing balances and move funds
 * between accounts in a ledger-safe, auditable, and controlled way.
 * 
 * HARD RULES (Non-Negotiable):
 * ❌ UI NEVER modifies balances
 * ❌ UI NEVER touches legacy tables
 * ❌ UI NEVER writes directly to Ledger tables
 * ✅ All transfers go through ITreasuryOperationService.TransferAsync()
 * ✅ Every transfer creates a balanced journal entry
 * ✅ Trial Balance remains zero-variance after every transfer
 * 
 * PHASE: Treasury Operations Layer Completion
 */

import { useState, useEffect } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../../contexts/SettingsContext'
import { useToast } from '../../components/Toast'
import ReportTruthBadge from '../../components/ReportTruthBadge'
import api from '../../services/api'

export default function TreasuryTransfers() {
    const { t } = useTranslation()
    const navigate = useNavigate()
    const { showError, showSuccess } = useToast()
    const { formatCurrency } = useSettings()
    const location = useLocation()

    // Form state
    const [fromAccountCode, setFromAccountCode] = useState('')
    const [toAccountCode, setToAccountCode] = useState('')
    const [amount, setAmount] = useState('')
    const [description, setDescription] = useState('')
    const [reference, setReference] = useState('')
    
    // Data state
    const [accounts, setAccounts] = useState([])
    const [loading, setLoading] = useState(false)
    const [submitting, setSubmitting] = useState(false)
    
    // Validation state
    const [availableBalance, setAvailableBalance] = useState(null)
    const [validationError, setValidationError] = useState('')
    
    // Confirmation modal
    const [showConfirmation, setShowConfirmation] = useState(false)
    const [confirmationData, setConfirmationData] = useState(null)

    useEffect(() => {
        loadAccounts()
    }, [])

    useEffect(() => {
        // Pre-fill form from navigation state (from Clearing Aging "Settle" button)
        if (location.state) {
            const { fromAccountCode: from, toAccountCode: to, amount: amt, description: desc, reference: ref } = location.state
            if (from) setFromAccountCode(from)
            if (to) setToAccountCode(to)
            if (amt) setAmount(amt.toString())
            if (desc) setDescription(desc)
            if (ref) setReference(ref)
            
            // Clear navigation state after using it
            window.history.replaceState({}, document.title)
        }
    }, [location.state])

    useEffect(() => {
        // Real-time balance validation
        if (fromAccountCode) {
            const account = accounts.find(a => a.accountCode === fromAccountCode)
            setAvailableBalance(account?.currentBalance || 0)
            
            // Validate amount against balance
            if (amount && parseFloat(amount) > (account?.currentBalance || 0)) {
                setValidationError(t('treasury.insufficientBalance', 'Insufficient balance'))
            } else {
                setValidationError('')
            }
        } else {
            setAvailableBalance(null)
            setValidationError('')
        }
    }, [fromAccountCode, amount, accounts, t])

    const loadAccounts = async () => {
        try {
            setLoading(true)
            const response = await api.get('/treasury/accounts')
            setAccounts(response.data)
        } catch (error) {
            console.error('Failed to load accounts:', error)
            showError(t('treasury.loadAccountsError', 'Failed to load accounts'))
        } finally {
            setLoading(false)
        }
    }

    const validateForm = () => {
        if (!fromAccountCode) {
            setValidationError(t('treasury.selectFromAccount', 'Please select source account'))
            return false
        }
        
        if (!toAccountCode) {
            setValidationError(t('treasury.selectToAccount', 'Please select destination account'))
            return false
        }
        
        if (fromAccountCode === toAccountCode) {
            setValidationError(t('treasury.sameAccount', 'Source and destination must be different'))
            return false
        }
        
        if (!amount || parseFloat(amount) <= 0) {
            setValidationError(t('treasury.invalidAmount', 'Amount must be greater than zero'))
            return false
        }
        
        const fromAccount = accounts.find(a => a.accountCode === fromAccountCode)
        if (parseFloat(amount) > (fromAccount?.currentBalance || 0)) {
            setValidationError(t('treasury.insufficientBalance', 'Insufficient balance'))
            return false
        }
        
        if (!description) {
            setValidationError(t('treasury.descriptionRequired', 'Description is required'))
            return false
        }
        
        setValidationError('')
        return true
    }

    const handleSubmit = (e) => {
        e.preventDefault()
        
        if (!validateForm()) {
            return
        }
        
        // Prepare confirmation data
        const fromAccount = accounts.find(a => a.accountCode === fromAccountCode)
        const toAccount = accounts.find(a => a.accountCode === toAccountCode)
        
        setConfirmationData({
            fromAccount: `${fromAccount.accountCode} - ${fromAccount.name}`,
            toAccount: `${toAccount.accountCode} - ${toAccount.name}`,
            amount: parseFloat(amount),
            description,
            reference
        })
        
        setShowConfirmation(true)
    }

    const executeTransfer = async () => {
        try {
            setSubmitting(true)
            setShowConfirmation(false)
            
            const response = await api.post('/treasury/operations/transfer', {
                fromAccountCode,
                toAccountCode,
                amount: parseFloat(amount),
                description,
                reference: reference || null,
                operationId: crypto.randomUUID()
            })
            
            showSuccess(
                <div>
                    <div className="font-semibold">{t('treasury.transferSuccess', 'Transfer completed successfully')}</div>
                    <div className="text-sm mt-1">
                        {t('treasury.journalNumber', 'Journal')}: #{response.data.journalNumber}
                    </div>
                    <div className="text-sm">
                        {formatCurrency(parseFloat(amount))} {t('treasury.transferred', 'transferred')}
                    </div>
                </div>
            )
            
            // Reset form
            setFromAccountCode('')
            setToAccountCode('')
            setAmount('')
            setDescription('')
            setReference('')
            setAvailableBalance(null)
            
            // Reload accounts to refresh balances
            await loadAccounts()
            
        } catch (error) {
            console.error('Transfer failed:', error)
            showError(error.response?.data?.message || t('treasury.transferError', 'Transfer failed'))
        } finally {
            setSubmitting(false)
        }
    }

    const getAccountDisplay = (account) => {
        return `${account.accountCode} - ${account.name} (${formatCurrency(account.currentBalance)})`
    }

    if (loading) {
        return (
            <div className="flex items-center justify-center h-96">
                <div className="text-center">
                    <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary-600 mx-auto"></div>
                    <p className="mt-4 text-gray-600">{t('common.loading')}</p>
                </div>
            </div>
        )
    }

    return (
        <div className="space-y-6">
            {/* Phase C4: UI Truth Label */}
            <ReportTruthBadge type="financial" />

            {/* Header */}
            <div className="flex items-center justify-between">
                <div className="flex items-center gap-4">
                    <button onClick={() => navigate('/treasury')} className="text-gray-500 hover:text-gray-700">
                        ← {t('common.back')}
                    </button>
                    <div>
                        <h2 className="text-2xl font-bold text-gray-900">
                            💸 {t('treasury.transfers', 'Treasury Transfers')}
                        </h2>
                        <p className="text-gray-600">
                            {t('treasury.transfersSubtitle', 'Move funds between accounts with full audit trail')}
                        </p>
                    </div>
                </div>
            </div>

            {/* Architecture Guarantee Badge */}
            <div className="bg-green-50 border border-green-200 rounded-lg p-4">
                <div className="flex items-start gap-3">
                    <div className="text-green-600 text-2xl">✅</div>
                    <div className="flex-1">
                        <h3 className="font-semibold text-green-900">
                            {t('treasury.ledgerGuarantee', 'Ledger-First Architecture')}
                        </h3>
                        <ul className="text-sm text-green-700 mt-2 space-y-1">
                            <li>✓ {t('treasury.guarantee1', 'All transfers create balanced journal entries')}</li>
                            <li>✓ {t('treasury.guarantee2', 'No direct balance manipulation')}</li>
                            <li>✓ {t('treasury.guarantee3', 'Full audit trail in General Ledger')}</li>
                            <li>✓ {t('treasury.guarantee4', 'Trial Balance remains balanced')}</li>
                        </ul>
                    </div>
                </div>
            </div>

            {/* Transfer Form */}
            <div className="bg-white rounded-lg shadow-sm border border-gray-200 p-6">
                <h3 className="text-lg font-semibold text-gray-900 mb-4">
                    {t('treasury.newTransfer', 'New Transfer')}
                </h3>

                <form onSubmit={handleSubmit} className="space-y-4">
                    {/* From Account */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('treasury.fromAccount', 'From Account')} *
                        </label>
                        <select
                            value={fromAccountCode}
                            onChange={(e) => setFromAccountCode(e.target.value)}
                            className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
                            required
                        >
                            <option value="">{t('treasury.selectAccount', 'Select account...')}</option>
                            {accounts.map(account => (
                                <option key={account.accountCode} value={account.accountCode}>
                                    {getAccountDisplay(account)}
                                </option>
                            ))}
                        </select>
                        {availableBalance !== null && (
                            <p className="text-sm text-gray-600 mt-1">
                                {t('treasury.availableBalance', 'Available')}: {formatCurrency(availableBalance)}
                            </p>
                        )}
                    </div>

                    {/* To Account */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('treasury.toAccount', 'To Account')} *
                        </label>
                        <select
                            value={toAccountCode}
                            onChange={(e) => setToAccountCode(e.target.value)}
                            className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
                            required
                        >
                            <option value="">{t('treasury.selectAccount', 'Select account...')}</option>
                            {accounts
                                .filter(a => a.accountCode !== fromAccountCode)
                                .map(account => (
                                    <option key={account.accountCode} value={account.accountCode}>
                                        {getAccountDisplay(account)}
                                    </option>
                                ))}
                        </select>
                    </div>

                    {/* Amount */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('treasury.amount', 'Amount')} *
                        </label>
                        <input
                            type="number"
                            step="0.01"
                            min="0.01"
                            value={amount}
                            onChange={(e) => setAmount(e.target.value)}
                            className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
                            placeholder="0.00"
                            required
                        />
                    </div>

                    {/* Description */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('treasury.description', 'Description')} *
                        </label>
                        <input
                            type="text"
                            value={description}
                            onChange={(e) => setDescription(e.target.value)}
                            className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
                            placeholder={t('treasury.descriptionPlaceholder', 'e.g., COD settlement - Aramex')}
                            required
                        />
                    </div>

                    {/* Reference (Optional) */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('treasury.reference', 'Reference')} ({t('common.optional', 'Optional')})
                        </label>
                        <input
                            type="text"
                            value={reference}
                            onChange={(e) => setReference(e.target.value)}
                            className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
                            placeholder={t('treasury.referencePlaceholder', 'e.g., SETTLE-ARX-0001')}
                        />
                    </div>

                    {/* Validation Error */}
                    {validationError && (
                        <div className="bg-red-50 border border-red-200 rounded-md p-3">
                            <p className="text-sm text-red-700">⚠️ {validationError}</p>
                        </div>
                    )}

                    {/* Submit Button */}
                    <div className="flex gap-3 pt-4">
                        <button
                            type="submit"
                            disabled={submitting || !!validationError}
                            className="flex-1 bg-primary-600 text-white px-6 py-3 rounded-md hover:bg-primary-700 disabled:bg-gray-300 disabled:cursor-not-allowed font-medium"
                        >
                            {submitting ? t('common.processing', 'Processing...') : t('treasury.executeTransfer', 'Execute Transfer')}
                        </button>
                        <button
                            type="button"
                            onClick={() => {
                                setFromAccountCode('')
                                setToAccountCode('')
                                setAmount('')
                                setDescription('')
                                setReference('')
                                setValidationError('')
                            }}
                            className="px-6 py-3 border border-gray-300 rounded-md hover:bg-gray-50"
                        >
                            {t('common.clear', 'Clear')}
                        </button>
                    </div>
                </form>
            </div>

            {/* Confirmation Modal */}
            {showConfirmation && confirmationData && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4">
                        <div className="p-6">
                            <h3 className="text-lg font-semibold text-gray-900 mb-4">
                                ⚠️ {t('treasury.confirmTransfer', 'Confirm Transfer')}
                            </h3>
                            
                            <div className="space-y-3 mb-6">
                                <div className="flex justify-between text-sm">
                                    <span className="text-gray-600">{t('treasury.from', 'From')}:</span>
                                    <span className="font-medium">{confirmationData.fromAccount}</span>
                                </div>
                                <div className="flex justify-between text-sm">
                                    <span className="text-gray-600">{t('treasury.to', 'To')}:</span>
                                    <span className="font-medium">{confirmationData.toAccount}</span>
                                </div>
                                <div className="flex justify-between text-sm">
                                    <span className="text-gray-600">{t('treasury.amount', 'Amount')}:</span>
                                    <span className="font-bold text-lg text-primary-600">
                                        {formatCurrency(confirmationData.amount)}
                                    </span>
                                </div>
                                <div className="flex justify-between text-sm">
                                    <span className="text-gray-600">{t('treasury.description', 'Description')}:</span>
                                    <span className="font-medium">{confirmationData.description}</span>
                                </div>
                                {confirmationData.reference && (
                                    <div className="flex justify-between text-sm">
                                        <span className="text-gray-600">{t('treasury.reference', 'Reference')}:</span>
                                        <span className="font-medium">{confirmationData.reference}</span>
                                    </div>
                                )}
                            </div>

                            <div className="bg-yellow-50 border border-yellow-200 rounded-md p-3 mb-6">
                                <p className="text-sm text-yellow-800">
                                    {t('treasury.confirmationWarning', 'This will create a balanced journal entry in the General Ledger. This action cannot be undone.')}
                                </p>
                            </div>

                            <div className="flex gap-3">
                                <button
                                    onClick={executeTransfer}
                                    disabled={submitting}
                                    className="flex-1 bg-primary-600 text-white px-4 py-2 rounded-md hover:bg-primary-700 disabled:bg-gray-300 font-medium"
                                >
                                    {t('treasury.confirm', 'Confirm Transfer')}
                                </button>
                                <button
                                    onClick={() => setShowConfirmation(false)}
                                    disabled={submitting}
                                    className="flex-1 border border-gray-300 px-4 py-2 rounded-md hover:bg-gray-50"
                                >
                                    {t('common.cancel', 'Cancel')}
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            )}

            {/* Quick Actions */}
            <div className="bg-blue-50 border border-blue-200 rounded-lg p-4">
                <h3 className="font-semibold text-blue-900 mb-2">
                    {t('treasury.quickActions', 'Quick Actions')}
                </h3>
                <div className="flex flex-wrap gap-2">
                    <button
                        onClick={() => navigate('/treasury/clearing-aging')}
                        className="px-4 py-2 bg-white border border-blue-300 rounded-md hover:bg-blue-50 text-sm"
                    >
                        📊 {t('treasury.viewClearingAging', 'View Clearing Aging')}
                    </button>
                    <button
                        onClick={() => navigate('/reports/general-ledger')}
                        className="px-4 py-2 bg-white border border-blue-300 rounded-md hover:bg-blue-50 text-sm"
                    >
                        📖 {t('treasury.viewGeneralLedger', 'View General Ledger')}
                    </button>
                    <button
                        onClick={() => navigate('/reports/trial-balance')}
                        className="px-4 py-2 bg-white border border-blue-300 rounded-md hover:bg-blue-50 text-sm"
                    >
                        ⚖️ {t('treasury.viewTrialBalance', 'View Trial Balance')}
                    </button>
                </div>
            </div>
        </div>
    )
}

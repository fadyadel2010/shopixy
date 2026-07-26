/**
 * Confirm Action Modal Component
 * Phase P1: POS Admin - Online Customers Management
 * 
 * A reusable confirmation modal for destructive actions
 * with optional reason input for audit trail
 */

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AlertTriangle, X } from 'lucide-react'

export function ConfirmActionModal({
    isOpen,
    onClose,
    onConfirm,
    title,
    message,
    confirmLabel,
    cancelLabel,
    variant = 'danger', // 'danger' | 'warning' | 'info'
    requireReason = false,
    reasonLabel,
    reasonPlaceholder,
    loading = false,
}) {
    const { t } = useTranslation()
    const [reason, setReason] = useState('')

    if (!isOpen) return null

    const handleConfirm = () => {
        if (requireReason && !reason.trim()) {
            return // Don't allow empty reason if required
        }
        onConfirm(requireReason ? reason.trim() : undefined)
    }

    const handleClose = () => {
        setReason('')
        onClose()
    }

    const variantStyles = {
        danger: {
            iconBg: 'bg-red-100',
            iconColor: 'text-red-600',
            buttonBg: 'bg-red-600 hover:bg-red-700',
        },
        warning: {
            iconBg: 'bg-amber-100',
            iconColor: 'text-amber-600',
            buttonBg: 'bg-amber-600 hover:bg-amber-700',
        },
        info: {
            iconBg: 'bg-blue-100',
            iconColor: 'text-blue-600',
            buttonBg: 'bg-blue-600 hover:bg-blue-700',
        },
    }

    const styles = variantStyles[variant] || variantStyles.danger

    // Defaults using translation
    const finalConfirmLabel = confirmLabel || (variant === 'danger' ? t('common.delete') : t('common.submit'))
    const finalCancelLabel = cancelLabel || t('common.cancel')
    const finalReasonLabel = reasonLabel || t('onlineCustomers.sessions.reasonLabel')
    const finalReasonPlaceholder = reasonPlaceholder || t('onlineCustomers.sessions.reasonPlaceholder')

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
            <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4 overflow-hidden">
                {/* Header */}
                <div className="flex items-center justify-between p-4 border-b">
                    <div className="flex items-center gap-3">
                        <div className={`p-2 rounded-full ${styles.iconBg}`}>
                            <AlertTriangle className={`w-5 h-5 ${styles.iconColor}`} />
                        </div>
                        <h2 className="text-lg font-semibold text-gray-900">{title}</h2>
                    </div>
                    <button
                        onClick={handleClose}
                        className="text-gray-400 hover:text-gray-500"
                    >
                        <X className="w-5 h-5" />
                    </button>
                </div>

                {/* Content */}
                <div className="p-4">
                    <p className="text-gray-600 mb-4">{message}</p>

                    {requireReason && (
                        <div className="space-y-2">
                            <label className="block text-sm font-medium text-gray-700">
                                {finalReasonLabel}
                            </label>
                            <textarea
                                value={reason}
                                onChange={(e) => setReason(e.target.value)}
                                placeholder={finalReasonPlaceholder}
                                className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-primary-500 focus:border-transparent resize-none"
                                rows={3}
                            />
                        </div>
                    )}
                </div>

                {/* Actions */}
                <div className="flex justify-end gap-3 p-4 bg-gray-50 border-t">
                    <button
                        onClick={handleClose}
                        disabled={loading}
                        className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50"
                    >
                        {finalCancelLabel}
                    </button>
                    <button
                        onClick={handleConfirm}
                        disabled={loading || (requireReason && !reason.trim())}
                        className={`px-4 py-2 text-sm font-medium text-white rounded-lg disabled:opacity-50 ${styles.buttonBg}`}
                    >
                        {loading ? (
                            <span className="flex items-center gap-2">
                                <svg className="animate-spin h-4 w-4" viewBox="0 0 24 24">
                                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" fill="none" />
                                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                                </svg>
                                {t('common.loading')}
                            </span>
                        ) : finalConfirmLabel}
                    </button>
                </div>
            </div>
        </div>
    )
}

export default ConfirmActionModal

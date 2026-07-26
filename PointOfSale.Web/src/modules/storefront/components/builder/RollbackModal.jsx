/**
 * Rollback Modal
 * Phase 9.8: POS Integration
 * 
 * @param {Object} props
 * @param {boolean} props.isOpen
 * @param {Function} props.onClose
 * @param {Function} props.onConfirm
 * @param {import('../../types/builder').VersionHistoryEntry[]} props.versions
 * @param {boolean} props.isLoading
 * @param {boolean} props.isRollingBack
 */

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { RotateCcw, Check, X, Loader2 } from 'lucide-react'

export function RollbackModal({ isOpen, onClose, onConfirm, versions, isLoading, isRollingBack }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    const [selectedVersion, setSelectedVersion] = useState(null)
    const [reason, setReason] = useState('')
    const [showConfirmation, setShowConfirmation] = useState(false)

    if (!isOpen) return null

    const handleSelectVersion = (version) => {
        setSelectedVersion(version)
        setShowConfirmation(true)
    }

    const handleConfirm = () => {
        if (selectedVersion) {
            onConfirm(selectedVersion.version, reason)
        }
    }

    const handleBack = () => {
        setShowConfirmation(false)
        setReason('')
    }

    const formatDate = (dateString) => {
        return new Date(dateString).toLocaleString(isRTL ? 'ar-SA' : 'en-US', {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
        })
    }

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
            <div className="bg-white rounded-2xl w-full max-w-lg mx-4 shadow-2xl max-h-[80vh] flex flex-col">
                {/* Header */}
                <div className="p-6 border-b border-gray-200 flex items-center justify-between flex-shrink-0">
                    <div className="flex items-center gap-3">
                        <div className="w-10 h-10 bg-orange-100 rounded-lg flex items-center justify-center">
                            <RotateCcw size={20} className="text-orange-600" />
                        </div>
                        <div>
                            <h2 className="text-lg font-semibold text-gray-900">
                                {showConfirmation
                                    ? (isRTL ? 'تأكيد الاستعادة' : 'Confirm Rollback')
                                    : (isRTL ? 'سجل الإصدارات' : 'Version History')
                                }
                            </h2>
                            {!showConfirmation && (
                                <p className="text-sm text-gray-500">
                                    {isRTL ? 'اختر إصداراً للاستعادة' : 'Select a version to restore'}
                                </p>
                            )}
                        </div>
                    </div>
                    <button
                        onClick={onClose}
                        className="p-2 rounded-lg hover:bg-gray-100 transition-colors"
                    >
                        <X size={20} className="text-gray-500" />
                    </button>
                </div>

                {/* Content */}
                <div className="flex-1 overflow-y-auto p-6">
                    {showConfirmation ? (
                        <>
                            <div className="p-4 bg-gray-50 rounded-lg mb-4">
                                <p className="text-sm text-gray-600">
                                    {isRTL ? 'الاستعادة إلى معرّف الإصدار:' : 'Restoring to version ID:'}{' '}
                                    <strong className="text-gray-900">v{selectedVersion?.version}</strong>
                                </p>
                                <p className="text-xs text-gray-500 mt-1">
                                    {formatDate(selectedVersion?.publishedAt)}
                                </p>
                            </div>

                            <div>
                                <label className="block text-sm font-medium text-gray-700 mb-2">
                                    {isRTL ? 'سبب الاستعادة (اختياري)' : 'Reason for rollback (optional)'}
                                </label>
                                <textarea
                                    value={reason}
                                    onChange={(e) => setReason(e.target.value)}
                                    placeholder={isRTL ? 'مثال: استعادة التصميم السابق' : 'e.g., Reverting to previous design'}
                                    rows={3}
                                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                                />
                            </div>
                        </>
                    ) : isLoading ? (
                        <div className="flex items-center justify-center py-12">
                            <Loader2 size={32} className="text-indigo-600 animate-spin" />
                        </div>
                    ) : (
                        <div className="space-y-2">
                            {versions.map((version) => (
                                <button
                                    key={version.version}
                                    onClick={() => handleSelectVersion(version)}
                                    className="w-full p-4 text-left bg-gray-50 hover:bg-indigo-50 border border-gray-200 hover:border-indigo-300 rounded-lg transition-all group"
                                >
                                    <div className="flex items-center justify-between">
                                        <div>
                                            <p className="font-medium text-gray-900">
                                                {isRTL ? 'معرّف الإصدار' : 'Version ID'} v{version.version}
                                            </p>
                                            <p className="text-sm text-gray-500 mt-0.5">
                                                {formatDate(version.publishedAt)}
                                            </p>
                                            <p className="text-xs text-gray-400 mt-0.5">
                                                {isRTL ? 'بواسطة:' : 'By:'} {version.publishedBy} • {version.changeSummary || version.changeType}
                                            </p>
                                        </div>
                                        <div className="opacity-0 group-hover:opacity-100 transition-opacity">
                                            <Check size={20} className="text-indigo-600" />
                                        </div>
                                    </div>
                                </button>
                            ))}
                            {versions.length === 0 && (
                                <p className="text-center text-gray-500 py-8">
                                    {isRTL ? 'لا توجد إصدارات سابقة' : 'No previous versions found'}
                                </p>
                            )}
                        </div>
                    )}
                </div>

                {/* Actions */}
                <div className="p-6 border-t border-gray-200 flex justify-end gap-3 flex-shrink-0">
                    {showConfirmation ? (
                        <>
                            <button
                                onClick={handleBack}
                                disabled={isRollingBack}
                                className="px-4 py-2 text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors disabled:opacity-50"
                            >
                                {isRTL ? 'رجوع' : 'Back'}
                            </button>
                            <button
                                onClick={handleConfirm}
                                disabled={isRollingBack}
                                className="flex items-center gap-2 px-4 py-2 bg-orange-600 text-white rounded-lg hover:bg-orange-700 transition-colors disabled:opacity-50"
                            >
                                <RotateCcw size={16} />
                                {isRollingBack
                                    ? (isRTL ? 'جاري الاستعادة...' : 'Restoring...')
                                    : (isRTL ? 'تأكيد الاستعادة' : 'Confirm Rollback')
                                }
                            </button>
                        </>
                    ) : (
                        <button
                            onClick={onClose}
                            className="px-4 py-2 text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
                        >
                            {isRTL ? 'إغلاق' : 'Close'}
                        </button>
                    )}
                </div>
            </div>
        </div>
    )
}

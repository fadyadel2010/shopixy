/**
 * Version History Page
 * Phase 9.9: Storefront Admin Completion
 * 
 * Full UI for viewing version history and performing rollbacks.
 * CRITICAL: Rollback requires explicit confirmation with checkbox.
 */

import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
    History,
    RotateCcw,
    Loader2,
    AlertCircle,
    AlertTriangle,
    Check,
    X,
    ChevronRight,
    Clock,
    User,
    FileText
} from 'lucide-react'
import { storeConfigService } from '../services'
import { useToast } from '../../../components/Toast'
import { useAuthStore } from '../../../store/authStore'
import { deriveCurrentStoreIdentityPresentation } from '../../../auth/commerceHubVendorIdentityAuthority'
import {
    AuthoringActionBar,
    AuthoringEmptyState,
    AuthoringDialog,
    AuthoringFeedback,
    AuthoringLoadingState,
    AuthoringStatusBadge,
} from '../authoringPlatform'

export function VersionHistoryPage() {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const { showSuccess, showError } = useToast()
    const currentUser = useAuthStore((state) => state.user)
    const navigate = useNavigate()

    // State
    const [isLoading, setIsLoading] = useState(true)
    const [error, setError] = useState(null)
    /** @type {[import('../types/builder').VersionHistoryEntry[], Function]} */
    const [versions, setVersions] = useState([])

    // Rollback modal state
    const [rollbackModal, setRollbackModal] = useState({
        isOpen: false,
        version: null,
        isRollingBack: false,
        confirmed: false,
        reason: '',
    })

    // Load versions
    useEffect(() => {
        loadVersions()
    }, [])

    const loadVersions = async () => {
        setIsLoading(true)
        setError(null)
        try {
            const response = await storeConfigService.getVersionHistory()
            setVersions(response?.versions || [])
        } catch (err) {
            console.error('Failed to load versions:', err)
            setError(isRTL ? 'فشل تحميل سجل الإصدارات' : 'Failed to load version history')
        } finally {
            setIsLoading(false)
        }
    }

    const openRollbackModal = (version) => {
        setRollbackModal({
            isOpen: true,
            version,
            isRollingBack: false,
            confirmed: false,
            reason: '',
        })
    }

    const closeRollbackModal = () => {
        setRollbackModal({
            isOpen: false,
            version: null,
            isRollingBack: false,
            confirmed: false,
            reason: '',
        })
    }

    const handleRollback = async () => {
        if (!rollbackModal.confirmed) return

        const selectedVersion = versions.find((entry) => entry.version === rollbackModal.version?.version)
        if (!selectedVersion) {
            showError(isRTL ? 'الإصدار المحدد غير متاح.' : 'The selected version is no longer available.')
            return
        }

        setRollbackModal(prev => ({ ...prev, isRollingBack: true }))
        try {
            const currentConfig = deriveCurrentStoreIdentityPresentation(
                await storeConfigService.getStoreConfig(),
                currentUser,
            )
            const rowVersion = typeof currentConfig?.rowVersion === 'string' ? currentConfig.rowVersion.trim() : ''
            if (!rowVersion) {
                throw new Error('Rollback requires current backend RowVersion authority.')
            }

            const acceptedResponse = deriveCurrentStoreIdentityPresentation(
                await storeConfigService.rollback(selectedVersion.version, {
                    confirm: true,
                    reason: rollbackModal.reason || 'User-initiated rollback',
                }, {
                    headers: {
                        'If-Match': `"${rowVersion}"`,
                    },
                }),
                currentUser,
            )

            if (!acceptedResponse) {
                throw new Error('Rollback response authority was not admitted.')
            }
            showSuccess(
                isRTL
                    ? `تم الاستعادة للإصدار ${selectedVersion.version}`
                    : `Rolled back to version ${selectedVersion.version}`
            )
            closeRollbackModal()
            await loadVersions()
        } catch (err) {
            console.error('Rollback failed:', err)
            showError(isRTL ? 'فشل الاستعادة' : 'Rollback failed')
            setRollbackModal(prev => ({ ...prev, isRollingBack: false }))
        }
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

    const formatRelativeTime = (dateString) => {
        const now = new Date()
        const date = new Date(dateString)
        const diffMs = now - date
        const diffMins = Math.floor(diffMs / 60000)
        const diffHours = Math.floor(diffMs / 3600000)
        const diffDays = Math.floor(diffMs / 86400000)

        if (diffMins < 60) {
            return isRTL ? `منذ ${diffMins} دقيقة` : `${diffMins}m ago`
        } else if (diffHours < 24) {
            return isRTL ? `منذ ${diffHours} ساعة` : `${diffHours}h ago`
        } else {
            return isRTL ? `منذ ${diffDays} يوم` : `${diffDays}d ago`
        }
    }

    // Loading state
    if (isLoading) {
        return <AuthoringLoadingState label="Loading StoreConfig History…" />
    }

    // Error state
    if (error) {
        return <AuthoringFeedback kind="error" title="StoreConfig History unavailable" message={error} action={{ label: 'Retry', onClick: loadVersions }} />
    }

    return (
        <div className="max-w-4xl mx-auto">
            <AuthoringActionBar
                title="StoreConfig History"
                subtitle="View and roll back StoreConfig publication snapshots. CMS Page History and Theme catalog revisions are excluded."
                backAction={{ label: 'Back', onClick: () => navigate('/storefront') }}
                status={<AuthoringStatusBadge state="published">{versions.length} snapshots</AuthoringStatusBadge>}
            />

            {/* Warning Banner */}
            <div className="mb-6 p-4 bg-yellow-50 border border-yellow-200 rounded-xl flex gap-3">
                <AlertTriangle size={20} className="text-yellow-600 flex-shrink-0 mt-0.5" />
                <div>
                    <p className="font-medium text-yellow-800">
                        {isRTL ? 'تنبيه مهم' : 'Important Notice'}
                    </p>
                    <p className="text-sm text-yellow-700 mt-1">
                        {isRTL
                            ? 'الاستعادة لإصدار سابق ستؤثر على متجرك المباشر فوراً. تأكد من فهمك للتغييرات قبل المتابعة.'
                            : 'Rolling back to a previous version will immediately affect your live store. Make sure you understand the changes before proceeding.'
                        }
                    </p>
                </div>
            </div>

            {/* Empty State */}
            {versions.length === 0 ? (
                <AuthoringEmptyState title="No StoreConfig History" description="No StoreConfig publication snapshot is available yet. CMS Page versions and Theme catalog revisions are intentionally not shown here." />
            ) : (
                /* Version List */
                <div className="space-y-4">
                    {versions.map((version, index) => (
                        <div
                            key={version.version}
                            className={`bg-white rounded-xl border p-5 transition-all ${version.isCurrent
                                ? 'border-green-300 bg-green-50/50'
                                : 'border-gray-200 hover:border-gray-300'
                                }`}
                        >
                            <div className="flex items-start justify-between">
                                <div className="flex items-start gap-4">
                                    {/* Version Number */}
                                    <div className={`w-12 h-12 rounded-xl flex items-center justify-center font-bold ${version.isCurrent
                                        ? 'bg-green-100 text-green-700'
                                        : 'bg-gray-100 text-gray-600'
                                        }`}>
                                        v{version.version}
                                    </div>

                                    {/* Details */}
                                    <div>
                                        <div className="flex items-center gap-3">
                                            <h3 className="font-semibold text-gray-900">
                                                {isRTL ? 'معرّف الإصدار' : 'Version ID'} v{version.version}
                                            </h3>
                                            {version.isCurrent && (
                                                <span className="px-2 py-0.5 bg-green-100 text-green-700 text-xs font-medium rounded-full">
                                                    {isRTL ? 'الحالي' : 'Current'}
                                                </span>
                                            )}
                                        </div>

                                        <div className="flex flex-wrap items-center gap-4 mt-2 text-sm text-gray-500">
                                            <span className="flex items-center gap-1">
                                                <Clock size={14} />
                                                {formatDate(version.publishedAt)}
                                                <span className="text-gray-400">
                                                    ({formatRelativeTime(version.publishedAt)})
                                                </span>
                                            </span>
                                            <span className="flex items-center gap-1">
                                                <User size={14} />
                                                {version.publishedBy}
                                            </span>
                                            <span className="flex items-center gap-1">
                                                <FileText size={14} />
                                                {version.changeSummary || version.changeType}
                                            </span>
                                        </div>
                                    </div>
                                </div>

                                {/* Actions */}
                                {!version.isCurrent && (
                                    <button
                                        onClick={() => openRollbackModal(version)}
                                        className="flex items-center gap-2 px-4 py-2 text-orange-600 bg-orange-50 border border-orange-200 rounded-lg hover:bg-orange-100 transition-colors"
                                    >
                                        <RotateCcw size={16} />
                                        {isRTL ? 'استعادة' : 'Rollback'}
                                    </button>
                                )}
                            </div>
                        </div>
                    ))}
                </div>
            )}

            <AuthoringDialog
                open={rollbackModal.isOpen}
                title={isRTL ? 'تأكيد الاستعادة' : 'Confirm Rollback'}
                description={`${isRTL ? 'معرّف الإصدار' : 'Version ID'} v${rollbackModal.version?.version || ''}`}
                onClose={rollbackModal.isRollingBack ? undefined : closeRollbackModal}
                panelClassName="!max-w-md"
            >
                        <div className="mt-6 space-y-4">
                            {/* Warning */}
                            <div className="p-4 bg-red-50 border border-red-200 rounded-lg">
                                <div className="flex gap-3">
                                    <AlertTriangle size={20} className="text-red-600 flex-shrink-0 mt-0.5" />
                                    <div>
                                        <p className="font-medium text-red-800">
                                            {isRTL ? 'تحذير: إجراء لا يمكن التراجع عنه' : 'Warning: Irreversible Action'}
                                        </p>
                                        <p className="text-sm text-red-700 mt-1">
                                            {isRTL
                                                ? 'سيتم استبدال جميع التغييرات الحالية بالإصدار المحدد. سيؤثر هذا على متجرك المباشر فوراً.'
                                                : 'All current changes will be replaced with the selected version. This will immediately affect your live store.'
                                            }
                                        </p>
                                    </div>
                                </div>
                            </div>

                            {/* Reason Input */}
                            <div>
                                <label className="block text-sm font-medium text-gray-700 mb-2">
                                    {isRTL ? 'سبب الاستعادة (اختياري)' : 'Reason for rollback (optional)'}
                                </label>
                                <textarea
                                    value={rollbackModal.reason}
                                    onChange={(e) => setRollbackModal(prev => ({ ...prev, reason: e.target.value }))}
                                    placeholder={isRTL ? 'مثال: استعادة التصميم السابق' : 'e.g., Reverting to previous design'}
                                    rows={2}
                                    disabled={rollbackModal.isRollingBack}
                                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-orange-500 disabled:bg-gray-50"
                                />
                            </div>

                            {/* Confirmation Checkbox */}
                            <label className="flex items-start gap-3 p-3 bg-gray-50 rounded-lg cursor-pointer">
                                <input
                                    type="checkbox"
                                    checked={rollbackModal.confirmed}
                                    onChange={(e) => setRollbackModal(prev => ({ ...prev, confirmed: e.target.checked }))}
                                    disabled={rollbackModal.isRollingBack}
                                    className="w-5 h-5 text-orange-600 rounded mt-0.5 focus:ring-orange-500"
                                />
                                <span className="text-sm text-gray-700">
                                    {isRTL
                                        ? 'أفهم أن هذا الإجراء سيؤثر على متجري المباشر فوراً وأريد المتابعة.'
                                        : 'I understand this action will immediately affect my live store and I want to proceed.'
                                    }
                                </span>
                            </label>
                        </div>

                        <div className="mt-6 flex justify-end gap-3 border-t border-gray-200 pt-6">
                            <button
                                onClick={closeRollbackModal}
                                disabled={rollbackModal.isRollingBack}
                                className="px-4 py-2 text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50 transition-colors"
                            >
                                {isRTL ? 'إلغاء' : 'Cancel'}
                            </button>
                            <button
                                onClick={handleRollback}
                                disabled={!rollbackModal.confirmed || rollbackModal.isRollingBack}
                                className="flex items-center gap-2 px-4 py-2 bg-orange-600 text-white rounded-lg hover:bg-orange-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                            >
                                {rollbackModal.isRollingBack ? (
                                    <Loader2 size={16} className="animate-spin" />
                                ) : (
                                    <RotateCcw size={16} />
                                )}
                                {rollbackModal.isRollingBack
                                    ? (isRTL ? 'جاري الاستعادة...' : 'Rolling back...')
                                    : (isRTL ? 'استعادة الآن' : 'Rollback Now')
                                }
                            </button>
                        </div>
            </AuthoringDialog>
        </div>
    )
}

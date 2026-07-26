import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'

/**
 * A reusable modal for handling deletion with validation
 * Shows appropriate messages when deletion is blocked and offers alternatives
 */
export default function ValidationDeleteModal({
    isOpen,
    onClose,
    entityType, // 'product', 'customer', 'supplier', 'purchase', 'sale'
    entityId,
    entityName,
    onDeleteSuccess,
    onDeactivateSuccess
}) {
    const { t, i18n } = useTranslation()
    const [loading, setLoading] = useState(false)
    const [checking, setChecking] = useState(true)
    const [canDelete, setCanDelete] = useState(true)
    const [validationMessage, setValidationMessage] = useState('')
    const [errorCode, setErrorCode] = useState('')

    // Map entity types to their API endpoints
    const endpoints = {
        product: '/products',
        customer: '/customers',
        supplier: '/suppliers',
        purchase: '/purchases',
        sale: '/sales'
    }

    // Map entity types to their deactivation endpoints (if available)
    const deactivateEndpoints = {
        product: '/products/{id}/deactivate',
        customer: null, // Customers don't have separate deactivate endpoint
        supplier: null  // Suppliers don't have separate deactivate endpoint
    }

    useEffect(() => {
        if (isOpen && entityId) {
            checkCanDelete()
        }
    }, [isOpen, entityId])

    const checkCanDelete = async () => {
        setChecking(true)
        try {
            const response = await api.get(`${endpoints[entityType]}/${entityId}/can-delete`)
            setCanDelete(response.data.canDelete)
            if (!response.data.canDelete) {
                // Use the appropriate language message
                const message = i18n.language === 'ar'
                    ? response.data.messageAr
                    : response.data.messageEn
                setValidationMessage(message || t('validation.cannotDelete'))
                setErrorCode(response.data.errorCode)
            }
        } catch (error) {
            console.error('Error checking delete permission:', error)
            // If check fails, assume deletion might be blocked
            setCanDelete(false)
            setValidationMessage(t('common.failedToLoad'))
        } finally {
            setChecking(false)
        }
    }

    const handleDelete = async () => {
        setLoading(true)
        try {
            await api.delete(`${endpoints[entityType]}/${entityId}`)
            onDeleteSuccess?.()
            onClose()
        } catch (error) {
            console.error('Error deleting:', error)
            // Extract bilingual message from backend
            const data = error.response?.data
            if (data) {
                const message = i18n.language === 'ar'
                    ? data.messageAr
                    : data.messageEn
                setValidationMessage(message || t('common.deleteFailed'))
                setCanDelete(false)
            }
        } finally {
            setLoading(false)
        }
    }

    const handleDeactivate = async () => {
        setLoading(true)
        try {
            const endpoint = deactivateEndpoints[entityType]?.replace('{id}', entityId)
            if (endpoint) {
                await api.put(endpoint)
            } else {
                // For entities without dedicated deactivate endpoint, use update
                await api.put(`${endpoints[entityType]}/${entityId}`, { isActive: false })
            }
            onDeactivateSuccess?.()
            onClose()
        } catch (error) {
            console.error('Error deactivating:', error)
        } finally {
            setLoading(false)
        }
    }

    if (!isOpen) return null

    const showDeactivateOption = ['product'].includes(entityType) && !canDelete

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
            <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4 p-6">
                {checking ? (
                    <div className="text-center py-8">
                        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600 mx-auto mb-4"></div>
                        <p className="text-gray-600">{t('common.loading')}</p>
                    </div>
                ) : canDelete ? (
                    <>
                        {/* Can delete - show confirmation */}
                        <div className="text-center mb-6">
                            <div className="mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-red-100 mb-4">
                                <svg className="h-6 w-6 text-red-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                                </svg>
                            </div>
                            <h3 className="text-lg font-medium text-gray-900 mb-2">{t('common.delete')}</h3>
                            <p className="text-gray-600">
                                {t('common.deleteConfirm', { item: entityName })}
                            </p>
                        </div>
                        <div className="flex justify-end gap-3">
                            <button
                                onClick={onClose}
                                disabled={loading}
                                className="btn-secondary"
                            >
                                {t('common.cancel')}
                            </button>
                            <button
                                onClick={handleDelete}
                                disabled={loading}
                                className="bg-red-600 hover:bg-red-700 text-white px-4 py-2 rounded-md font-medium transition-colors disabled:opacity-50"
                            >
                                {loading ? t('common.processing') : t('common.delete')}
                            </button>
                        </div>
                    </>
                ) : (
                    <>
                        {/* Cannot delete - show warning and alternatives */}
                        <div className="text-center mb-6">
                            <div className="mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-yellow-100 mb-4">
                                <svg className="h-6 w-6 text-yellow-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                                </svg>
                            </div>
                            <h3 className="text-lg font-medium text-gray-900 mb-2">
                                {t('common.cannotDelete')}
                            </h3>
                            <p className="text-gray-600 text-sm mb-4">
                                {validationMessage}
                            </p>
                            {showDeactivateOption && (
                                <div className="bg-blue-50 border border-blue-200 rounded-lg p-4 text-left">
                                    <p className="text-sm text-blue-800 mb-2">
                                        <strong>{t('common.info')}:</strong> {t('validation.useInactiveHint')}
                                    </p>
                                </div>
                            )}
                            {['purchase', 'sale'].includes(entityType) && errorCode === 'INVOICE_POSTED' && (
                                <div className="bg-blue-50 border border-blue-200 rounded-lg p-4 text-left mt-4">
                                    <p className="text-sm text-blue-800">
                                        <strong>{t('common.info')}:</strong> {t('validation.useReturnHint')}
                                    </p>
                                </div>
                            )}
                        </div>
                        <div className="flex justify-end gap-3">
                            <button
                                onClick={onClose}
                                className="btn-secondary"
                            >
                                {t('common.close')}
                            </button>
                            {showDeactivateOption && (
                                <button
                                    onClick={handleDeactivate}
                                    disabled={loading}
                                    className="bg-yellow-600 hover:bg-yellow-700 text-white px-4 py-2 rounded-md font-medium transition-colors disabled:opacity-50"
                                >
                                    {loading ? t('common.processing') : t('common.markInactive')}
                                </button>
                            )}
                        </div>
                    </>
                )}
            </div>
        </div>
    )
}

import { useState, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'

const ENDPOINTS = {
    product: '/products',
    customer: '/customers',
    supplier: '/suppliers',
    purchase: '/purchases',
    sale: '/sales'
}

const DEACTIVATE_ENDPOINTS = {
    product: '/products/{id}/deactivate',
    customer: null,
    supplier: null
}

const UNSAFE_MESSAGE_PATTERN = /exception|stack\s*trace|\bat\s+\S+\(|sql|select\s+.+\s+from|system\.|authorization|bearer|tenant/i

function safeDomainMessage(value, fallback) {
    return typeof value === 'string'
        && value.trim().length > 0
        && value.length <= 240
        && !UNSAFE_MESSAGE_PATTERN.test(value)
        ? value.trim()
        : fallback
}

function createLifecycleIdempotencyKey(entityType, entityId, operation) {
    const randomPart = globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random().toString(36).slice(2)}`
    return `${entityType}-${operation}:${entityId}:${randomPart}`
}

function buildMutationOptions(entityType, entityId, operation, expectedVersion) {
    const headers = {
        'Idempotency-Key': createLifecycleIdempotencyKey(entityType, entityId, operation)
    }

    if (expectedVersion !== null && expectedVersion !== undefined && `${expectedVersion}`.trim() !== '') {
        headers['If-Match'] = `"${expectedVersion}"`
    }

    return { headers }
}

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
    expectedVersion,
    onDeleteSuccess,
    onDeactivateSuccess
}) {
    const { t, i18n } = useTranslation()
    const language = i18n.language
    const [loading, setLoading] = useState(false)
    const [checking, setChecking] = useState(true)
    const [canDelete, setCanDelete] = useState(true)
    const [validationMessage, setValidationMessage] = useState('')
    const [errorCode, setErrorCode] = useState('')
    const [requiresRefresh, setRequiresRefresh] = useState(false)
    const mutationInFlightRef = useRef(false)

    useEffect(() => {
        if (!isOpen || !entityId) {
            return undefined
        }

        let isCurrent = true
        setChecking(true)
        setCanDelete(true)
        setValidationMessage('')
        setErrorCode('')
        setRequiresRefresh(false)
        mutationInFlightRef.current = false

        void (async () => {
            try {
                const response = await api.get(`${ENDPOINTS[entityType]}/${entityId}/can-delete`)
                if (!isCurrent) return

                const isAllowed = response.data.canDelete === true
                if (isAllowed && entityType === 'product' && (expectedVersion === null || expectedVersion === undefined || `${expectedVersion}`.trim() === '')) {
                    setCanDelete(false)
                    setRequiresRefresh(true)
                    setValidationMessage(t('products.deleteVersionMissing') || 'The product version is unavailable. Refresh Products before deleting.')
                    return
                }

                setCanDelete(isAllowed)
                if (!isAllowed) {
                    const message = language === 'ar'
                        ? response.data.messageAr
                        : response.data.messageEn
                    setValidationMessage(safeDomainMessage(message, t('validation.cannotDelete')))
                    setErrorCode(response.data.errorCode || '')
                }
            } catch (error) {
                if (!isCurrent) return

                setCanDelete(false)
                setRequiresRefresh(true)
                if (error?.response?.status === 404) {
                    setValidationMessage(t('products.noLongerAvailable') || 'This product is no longer available. Refresh the Products list.')
                } else if (error?.response?.status === 403) {
                    setValidationMessage(t('products.deletePermissionDenied') || 'You do not have permission to delete this product.')
                } else {
                    setValidationMessage(t('products.deleteCheckFailed') || 'Deletion eligibility could not be checked. Refresh Products and try again.')
                }
            } finally {
                if (isCurrent) {
                    setChecking(false)
                }
            }
        })()

        return () => {
            isCurrent = false
        }
    }, [isOpen, entityId, entityType, expectedVersion, language])

    useEffect(() => {
        if (!isOpen || loading) return undefined

        const handleEscape = (event) => {
            if (event.key === 'Escape') {
                onClose?.()
            }
        }

        window.addEventListener('keydown', handleEscape)
        return () => window.removeEventListener('keydown', handleEscape)
    }, [isOpen, loading, onClose])

    const applyMutationError = (error, operation) => {
        const status = error?.response?.status
        const data = error?.response?.data
        const localizedMessage = language === 'ar' ? data?.messageAr : data?.messageEn

        if (status === 400 && data?.errorCode && !String(data.errorCode).includes('EXPECTED_VERSION')) {
            setCanDelete(false)
            setErrorCode(data.errorCode)
            setValidationMessage(safeDomainMessage(localizedMessage, t('validation.cannotDelete')))
            return
        }

        setCanDelete(false)
        setRequiresRefresh(true)
        if (status === 409 || data?.errorCode === 'PRODUCT_EXPECTED_VERSION_STALE') {
            setValidationMessage(t('products.deleteConcurrencyConflict') || 'This product changed before the action completed. Refresh Products before trying again.')
        } else if (status === 428 || String(data?.errorCode || '').includes('EXPECTED_VERSION')) {
            setValidationMessage(t('products.deleteVersionMissing') || 'The product version is unavailable. Refresh Products before deleting.')
        } else if (status === 404 || data?.errorCode === 'PRODUCT_NOT_FOUND') {
            setValidationMessage(t('products.noLongerAvailable') || 'This product is no longer available. Refresh the Products list.')
        } else if (status === 403) {
            setValidationMessage(t('products.deletePermissionDenied') || 'You do not have permission to perform this action.')
        } else if (!error?.response || status >= 500) {
            setValidationMessage(
                operation === 'delete'
                    ? (t('products.deleteOutcomeUncertain') || 'The deletion outcome could not be confirmed. Refresh Products before taking another action.')
                    : (t('products.deactivateOutcomeUncertain') || 'The deactivation outcome could not be confirmed. Refresh Products before taking another action.')
            )
        } else {
            setValidationMessage(t('common.deleteFailed'))
        }
    }

    const handleDelete = async () => {
        if (mutationInFlightRef.current || !canDelete) return

        mutationInFlightRef.current = true
        setLoading(true)
        try {
            await api.delete(
                `${ENDPOINTS[entityType]}/${entityId}`,
                buildMutationOptions(entityType, entityId, 'delete', expectedVersion)
            )
            onDeleteSuccess?.()
            onClose?.()
        } catch (error) {
            applyMutationError(error, 'delete')
        } finally {
            mutationInFlightRef.current = false
            setLoading(false)
        }
    }

    const handleDeactivate = async () => {
        if (mutationInFlightRef.current || requiresRefresh) return

        mutationInFlightRef.current = true
        setLoading(true)
        try {
            const endpoint = DEACTIVATE_ENDPOINTS[entityType]?.replace('{id}', entityId)
            if (endpoint) {
                await api.put(
                    endpoint,
                    undefined,
                    buildMutationOptions(entityType, entityId, 'deactivate', expectedVersion)
                )
            } else {
                // For entities without dedicated deactivate endpoint, use update
                await api.put(`${ENDPOINTS[entityType]}/${entityId}`, { isActive: false })
            }
            onDeactivateSuccess?.()
            onClose?.()
        } catch (error) {
            applyMutationError(error, 'deactivate')
        } finally {
            mutationInFlightRef.current = false
            setLoading(false)
        }
    }

    if (!isOpen) return null

    const showDeactivateOption = entityType === 'product' && !canDelete && !requiresRefresh

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50" dir={i18n.dir()}>
            <div
                className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4 p-6"
                role="dialog"
                aria-modal="true"
                aria-labelledby="validation-delete-title"
                aria-describedby="validation-delete-description"
            >
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
                            <h3 id="validation-delete-title" className="text-lg font-medium text-gray-900 mb-2">{t('common.delete')}</h3>
                            <p id="validation-delete-description" className="text-gray-600">
                                {t('common.deleteConfirm', { item: entityName })}
                            </p>
                        </div>
                        <div className="flex justify-end gap-3">
                            <button
                                onClick={onClose}
                                disabled={loading}
                                autoFocus
                                type="button"
                                className="btn-secondary"
                            >
                                {t('common.cancel')}
                            </button>
                            <button
                                onClick={handleDelete}
                                disabled={loading}
                                type="button"
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
                            <h3 id="validation-delete-title" className="text-lg font-medium text-gray-900 mb-2">
                                {t('common.cannotDelete')}
                            </h3>
                            <p id="validation-delete-description" className="text-gray-600 text-sm mb-4" role="alert">
                                {validationMessage}
                            </p>
                            {showDeactivateOption && (
                                <div className="bg-blue-50 border border-blue-200 rounded-lg p-4 text-start">
                                    <p className="text-sm text-blue-800 mb-2">
                                        <strong>{t('common.info')}:</strong> {t('validation.useInactiveHint')}
                                    </p>
                                </div>
                            )}
                            {['purchase', 'sale'].includes(entityType) && errorCode === 'INVOICE_POSTED' && (
                                <div className="bg-blue-50 border border-blue-200 rounded-lg p-4 text-start mt-4">
                                    <p className="text-sm text-blue-800">
                                        <strong>{t('common.info')}:</strong> {t('validation.useReturnHint')}
                                    </p>
                                </div>
                            )}
                        </div>
                        <div className="flex justify-end gap-3">
                            <button
                                onClick={onClose}
                                autoFocus
                                type="button"
                                className="btn-secondary"
                            >
                                {t('common.close')}
                            </button>
                            {showDeactivateOption && (
                                <button
                                    onClick={handleDeactivate}
                                    disabled={loading}
                                    type="button"
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

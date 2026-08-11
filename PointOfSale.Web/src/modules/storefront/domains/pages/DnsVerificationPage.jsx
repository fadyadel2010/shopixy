/**
 * D05 — DNS Verification
 *
 * Constitutional source: DOMAIN-3.1 Canonical Blueprint §2.3 (C05), §2.4 (route: /storefront/domains/:id/verification)
 *
 * Displays the DNS challenge for domain verification and tracks verification status.
 *
 * DOMAIN-3.1-B: Backed by real READ backend data via useDomainVerification hook.
 */

import { useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Shield, Copy, CheckCircle2, Clock3, RefreshCw } from 'lucide-react'

import {
    AuthoringButton,
    AuthoringEmptyState,
    AuthoringFeedback,
    AuthoringLoadingState,
    AuthoringStatusBadge,
    useAuthoringGuard,
} from '../../authoringPlatform'

import { useDomainVerification, useDomainMutations, LOAD_STATES } from '../hooks'

export function DnsVerificationPage() {
    const { t } = useTranslation()
    const { id } = useParams()

    const { data, state, error, isLoading, isError, isUnauthorized, refetch } =
        useDomainVerification(id)
    const {
        isMutating,
        mutationStatus,
        error: mutationError,
        verifyDomainNow,
        retryDomainVerification,
        refreshVerificationToken,
    } = useDomainMutations({
        mutationOptions: {
            onSuccess: () => refetch(),
            onError: () => refetch(),
        },
    })

    const { requestNavigation } = useAuthoringGuard({ isDirty: false, isBusy: isLoading })

    const handleBack = () => {
        requestNavigation(`/storefront/domains/${id}`)
    }

    const handleCopy = async (text) => {
        try {
            await navigator.clipboard.writeText(text)
        } catch {
            // Clipboard API not available — fail silently
        }
    }

    const handleVerifyNow = async () => {
        if (isMutating) return
        try {
            await verifyDomainNow(id, { expectedVersion: data?.version })
        } catch {
            // Safe error state is rendered below.
        }
    }

    const handleRetry = async () => {
        if (isMutating) return
        try {
            await retryDomainVerification(id, { expectedVersion: data?.version })
        } catch {
            // Safe error state is rendered below.
        }
    }

    const handleRefreshToken = async () => {
        if (isMutating) return
        try {
            await refreshVerificationToken(id, {
                verificationMethod: data?.method,
                expectedVersion: data?.version,
            })
        } catch {
            // Safe error state is rendered below.
        }
    }

    if (isLoading) {
        return <AuthoringLoadingState />
    }

    if (isUnauthorized) {
        return (
            <div className="space-y-4">
                <AuthoringButton
                    action={{
                        label: t('common.back', 'Back'),
                        onClick: handleBack,
                    }}
                    defaultLabel={t('common.back', 'Back')}
                />
                <AuthoringFeedback
                    kind="error"
                    title={t('storefront.domains.unauthorized', 'Authentication required')}
                    message={t('storefront.domains.unauthorizedDesc', 'Please sign in to manage domains.')}
                />
            </div>
        )
    }

    if (isError) {
        return (
            <div className="space-y-4">
                <AuthoringButton
                    action={{
                        label: t('common.back', 'Back'),
                        onClick: handleBack,
                    }}
                    defaultLabel={t('common.back', 'Back')}
                />
                <AuthoringFeedback
                    kind="error"
                    title={t('storefront.domains.loadError', 'Unable to load verification')}
                    message={error}
                    action={{
                        label: t('common.retry', 'Retry'),
                        onClick: () => refetch(),
                    }}
                />
            </div>
        )
    }

    if (state === LOAD_STATES.NOT_FOUND) {
        return (
            <div className="space-y-4">
                <AuthoringButton
                    action={{
                        label: t('common.back', 'Back'),
                        onClick: handleBack,
                    }}
                    defaultLabel={t('common.back', 'Back')}
                />
                <AuthoringEmptyState
                    title={t('storefront.domains.domainNotFound', 'Domain not found')}
                    description={t(
                        'storefront.domains.domainNotFoundDesc',
                        'The requested domain could not be found.',
                    )}
                />
            </div>
        )
    }

    if (!data) {
        return <AuthoringLoadingState />
    }

    const hostname = data.hostName || data.normalizedHostName || id || 'example.com'
    const verificationStatus = data.status === 'Success' ? 'success' : data.status === 'Failed' ? 'failed' : 'pending'
    const recordType =
        data.dnsRecordType ||
        (data.method === 'DnsCname' ? 'CNAME' : data.method === 'HttpFile' ? 'HTTP' : 'TXT')
    const recordName = data.method === 'HttpFile' ? data.httpVerificationUrl : data.dnsRecordName
    const recordValue = data.dnsRecordValue || data.verificationToken || ''
    const recordHelp =
        data.method === 'HttpFile'
            ? t(
                  'storefront.domains.httpHelp',
                  'Serve this value from the verification URL. Verification will be checked automatically.',
              )
            : t(
                  'storefront.domains.dnsHelp',
                  'Add this record to your DNS provider. Verification will be checked automatically.',
              )

    return (
        <div className="space-y-4">
            <AuthoringButton
                action={{
                    label: t('common.back', 'Back'),
                    onClick: handleBack,
                }}
                defaultLabel={t('common.back', 'Back')}
            />

            <div className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800">
                <div className="mb-4 flex items-center gap-3">
                    <Shield size={24} className="text-indigo-600 dark:text-indigo-400" />
                    <h1 className="text-xl font-semibold text-gray-900 dark:text-gray-100">
                        {t('storefront.domains.dnsVerification', 'DNS Verification')}
                    </h1>
                </div>
                <p className="text-sm text-gray-500 dark:text-gray-400">
                    {t('storefront.domains.hostname', 'Hostname')}:{' '}
                    <span className="font-mono text-gray-900 dark:text-gray-100 break-all">
                        {hostname}
                    </span>
                </p>
            </div>

            {/* Status Card */}
            <div className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800">
                <div className="mb-4 flex items-center justify-between">
                    <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
                        {t('storefront.domains.verificationStatus', 'Verification Status')}
                    </h2>
                    <AuthoringStatusBadge
                        state={
                            verificationStatus === 'success'
                                ? 'published'
                                : verificationStatus === 'failed'
                                  ? 'error'
                                  : 'draft'
                        }
                    >
                        {verificationStatus === 'success'
                            ? t('storefront.domains.verified', 'Verified')
                            : verificationStatus === 'failed'
                              ? t('storefront.domains.failed', 'Failed')
                              : t('storefront.domains.pendingVerification', 'Pending Verification')}
                    </AuthoringStatusBadge>
                </div>
                <div className="flex items-start gap-3">
                    {verificationStatus === 'success' ? (
                        <CheckCircle2 size={20} className="text-emerald-600 dark:text-emerald-400" />
                    ) : (
                        <Clock3 size={20} className="text-amber-600 dark:text-amber-400" />
                    )}
                    <p className="text-sm text-gray-600 dark:text-gray-300">
                        {verificationStatus === 'success'
                            ? t(
                                  'storefront.domains.verifiedDesc',
                                  'Your domain ownership has been verified.',
                              )
                            : t(
                                  'storefront.domains.pendingDesc',
                                  'Verification is in progress. Add the DNS record below and we will check automatically.',
                              )}
                    </p>
                </div>
            </div>

            {/* DNS Instructions */}
            <div className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800">
                <h2 className="mb-3 text-lg font-semibold text-gray-900 dark:text-gray-100">
                    {t('storefront.domains.verificationInstructions', 'Verification Instructions')}
                </h2>
                <div className="rounded-lg bg-gray-50 p-4 font-mono text-sm dark:bg-gray-700/50">
                    <div className="mb-2">
                        <span className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">
                            {t('storefront.domains.recordType', 'Type')}
                        </span>
                        <span className="ml-2 text-gray-900 dark:text-gray-100">{recordType}</span>
                    </div>
                    <div className="mb-2">
                        <span className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">
                            {data.method === 'HttpFile'
                                ? t('storefront.domains.verificationUrl', 'URL')
                                : t('storefront.domains.recordName', 'Name')}
                        </span>
                        <span className="ml-2 break-all text-gray-900 dark:text-gray-100">
                            {recordName || '_shopixy-verification'}
                        </span>
                    </div>
                    <div className="mb-2">
                        <span className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">
                            {t('storefront.domains.recordValue', 'Value')}
                        </span>
                        <div className="mt-1 flex items-center gap-2">
                            <code className="block break-all rounded bg-white px-3 py-2 text-xs text-gray-900 border border-gray-200 dark:bg-gray-700 dark:border-gray-600 dark:text-gray-100">
                                {recordValue || t('storefront.domains.noToken', 'No verification token')}
                            </code>
                            <button
                                type="button"
                                onClick={() => handleCopy(recordValue)}
                                className="rounded-lg border border-gray-300 p-1 text-gray-500 hover:bg-gray-100 dark:border-gray-600 dark:text-gray-400"
                                title={t('common.copy', 'Copy')}
                                aria-label={t('common.copy', 'Copy')}
                            >
                                <Copy size={16} />
                            </button>
                        </div>
                    </div>
                </div>
                <p className="mt-3 text-xs text-gray-500 dark:text-gray-400">
                    {recordHelp}
                </p>
            </div>

            {mutationError && (
                <AuthoringFeedback
                    kind={mutationStatus === 'infrastructure-error' ? 'error' : 'warning'}
                    title={
                        mutationStatus === 'infrastructure-error'
                            ? t('storefront.domains.infrastructureError', 'Verification service unavailable')
                            : t('storefront.domains.verificationFailed', 'Verification not complete')
                    }
                    message={mutationError}
                />
            )}

            {/* Verification command buttons */}
            <div className="flex flex-wrap justify-end gap-2">
                {verificationStatus !== 'success' && (
                    <button
                        type="button"
                        onClick={handleRefreshToken}
                        disabled={isMutating}
                        className="flex items-center gap-2 rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50 dark:border-gray-600 dark:text-gray-300 dark:hover:bg-gray-700/50"
                    >
                        <RefreshCw size={16} />
                        {t('storefront.domains.refreshToken', 'Refresh Token')}
                    </button>
                )}
                <button
                    type="button"
                    onClick={handleVerifyNow}
                    disabled={isMutating || verificationStatus === 'success'}
                    className="flex items-center gap-2 rounded-lg border border-indigo-500 bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-50"
                >
                    <Shield size={16} />
                    {t('storefront.domains.verifyNow', 'Verify Now')}
                </button>
                <button
                    type="button"
                    onClick={handleRetry}
                    disabled={isMutating || verificationStatus === 'success'}
                    className="flex items-center gap-2 rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50 dark:border-gray-600 dark:text-gray-300 dark:hover:bg-gray-700/50"
                >
                    <RefreshCw size={16} />
                    {t('storefront.domains.retryVerification', 'Retry Verification')}
                </button>
            </div>
        </div>
    )
}

export default DnsVerificationPage

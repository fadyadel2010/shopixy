/**
 * D06 - SSL Management
 *
 * DOMAIN-5.9: Production SSL/TLS management UX backed by DOMAIN-5.8 APIs.
 */

import { useMemo, useRef, useState } from 'react'
import { useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
    AlertTriangle,
    Eye,
    RefreshCw,
    RotateCcw,
    Shield,
    ShieldCheck,
    ShieldOff,
    Trash2,
    Zap,
} from 'lucide-react'

import { useToast } from '../../../../components/Toast'
import {
    AuthoringButton,
    AuthoringDialog,
    AuthoringEmptyState,
    AuthoringFeedback,
    AuthoringLoadingState,
    AuthoringStatusBadge,
    useAuthoringGuard,
} from '../../authoringPlatform'

import {
    LOAD_STATES,
    useDomainDetail,
    useDomainMutations,
    useDomainSsl,
    useDomainSslCertificate,
    useDomainSslOperations,
} from '../hooks'

const CERTIFICATE_STATUS_NAMES = [
    'Pending',
    'Active',
    'Expired',
    'Revoked',
    'Failed',
    'Requested',
    'Authorizing',
    'Issued',
    'Activating',
    'Renewing',
]

const OPERATION_STATUS_NAMES = ['Pending', 'Running', 'Succeeded', 'Failed', 'Cancelled', 'Superseded']
const OPERATION_TYPE_NAMES = ['Provisioning', 'Renewal', 'Retry', 'Revocation', 'Activation', 'MetadataSync']
const OPERATION_PHASE_NAMES = [
    'None',
    'Admission',
    'ProviderRequest',
    'Authorization',
    'MetadataSync',
    'ActivationProbe',
    'RenewalWindow',
    'Revocation',
    'RetryBackoff',
    'EdgeDeactivation',
]
const PROVIDER_NAMES = ['LetsEncrypt', 'CustomUpload', 'SystemDefault']

const REVOCATION_REASONS = [
    'ADMIN_REQUESTED',
    'SECURITY_COMPROMISE',
    'CERTIFICATE_COMPROMISED',
    'SUPERSEDED',
    'POLICY_VIOLATION',
]

function enumName(value, names) {
    if (typeof value === 'number') return names[value] || String(value)
    if (value === null || value === undefined || value === '') return null
    return String(value)
}

function createIdempotencyKey(action) {
    const suffix = globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random().toString(36).slice(2)}`
    return `ssl-${action}-${suffix}`
}

function isRetryWindowOpen(retryAfterUtc) {
    if (!retryAfterUtc) return true
    const retryAfter = new Date(retryAfterUtc).getTime()
    return Number.isNaN(retryAfter) || retryAfter <= Date.now()
}

function getBadgeState(status, degraded = false) {
    if (degraded || status === 'Failed' || status === 'Expired') return 'error'
    if (status === 'Active') return 'published'
    if (status === 'Revoked') return 'disabled'
    if (status === 'Requested' || status === 'Authorizing' || status === 'Activating' || status === 'Renewing') {
        return 'saving'
    }
    return 'draft'
}

function isOperationRunning(status) {
    return status === 'Pending' || status === 'Running'
}

export function SslManagementPage() {
    const { t, i18n } = useTranslation()
    const { showInfo, showWarning } = useToast()
    const { id } = useParams()
    const [lastAccepted, setLastAccepted] = useState(null)
    const [commandName, setCommandName] = useState(null)
    const [activateOpen, setActivateOpen] = useState(false)
    const [revokeOpen, setRevokeOpen] = useState(false)
    const [revokeReason, setRevokeReason] = useState(REVOCATION_REASONS[0])
    const [selectedOperation, setSelectedOperation] = useState(null)
    const commandInFlightRef = useRef(false)
    const uncertainCommandKeysRef = useRef(new Map())

    const detailQuery = useDomainDetail(id)
    const sslQuery = useDomainSsl(id)
    const certificateQuery = useDomainSslCertificate(id)
    const operationsQuery = useDomainSslOperations(id)

    const {
        isMutating,
        mutationStatus,
        error: mutationError,
        errorCode,
        provisionSslCertificate,
        activateSslCertificate,
        renewSslCertificate,
        revokeSslCertificate,
        retrySslOperation,
        refreshSslCertificate,
        clearError,
    } = useDomainMutations({
        mutationOptions: {
            onSuccess: () => {
                void certificateQuery.refresh()
                void operationsQuery.refresh()
                void sslQuery.refresh()
                void detailQuery.refresh()
            },
            onError: () => {
                void certificateQuery.refresh()
                void operationsQuery.refresh()
            },
        },
    })

    const isLoading =
        detailQuery.isLoading ||
        sslQuery.isLoading ||
        certificateQuery.isLoading ||
        operationsQuery.isLoading
    const isUnauthorized =
        detailQuery.isUnauthorized ||
        sslQuery.isUnauthorized ||
        certificateQuery.isUnauthorized ||
        operationsQuery.isUnauthorized
    const isNotFound =
        detailQuery.state === LOAD_STATES.NOT_FOUND ||
        sslQuery.state === LOAD_STATES.NOT_FOUND ||
        certificateQuery.state === LOAD_STATES.NOT_FOUND ||
        operationsQuery.state === LOAD_STATES.NOT_FOUND
    const loadError =
        detailQuery.isError ? detailQuery.error :
            sslQuery.isError ? sslQuery.error :
                certificateQuery.isError ? certificateQuery.error :
                    operationsQuery.isError ? operationsQuery.error : null

    const { requestNavigation } = useAuthoringGuard({ isDirty: false, isBusy: isLoading || isMutating })

    const certificate = certificateQuery.data
    const ssl = sslQuery.data
    const domain = detailQuery.data
    const operationsPage = operationsQuery.data
    const operations = Array.isArray(operationsPage?.items) ? operationsPage.items : []

    const hostname = domain?.hostName || domain?.normalizedHostName || ssl?.hostName || ssl?.normalizedHostName || id
    const certificateId = certificate?.certificateId || ssl?.certificateId
    const certificateStatus = enumName(certificate?.status ?? ssl?.status, CERTIFICATE_STATUS_NAMES) || 'Pending'
    const lastOperationStatus = enumName(certificate?.lastOperationStatus, OPERATION_STATUS_NAMES)
    const activeOperation = operations.find((operation) => isOperationRunning(enumName(operation.status, OPERATION_STATUS_NAMES)))
    const hasCertificate = Boolean(certificateId)
    const commandBusy = Boolean(commandName) && isMutating
    const certificateDegraded = Boolean(certificate?.degraded)

    const daysUntilExpiry = useMemo(() => {
        const notAfter = certificate?.notAfterUtc || ssl?.notAfterUtc
        if (!notAfter) return null
        const expiresAt = new Date(notAfter).getTime()
        if (Number.isNaN(expiresAt)) return null
        return Math.ceil((expiresAt - Date.now()) / 86400000)
    }, [certificate?.notAfterUtc, ssl?.notAfterUtc])

    const terminalCertificate = ['Failed', 'Revoked', 'Expired', 'Retired'].includes(certificateStatus)
    const canProvision = domain?.isVerified === true &&
        (!hasCertificate || terminalCertificate) &&
        !activeOperation
    const canActivate = hasCertificate && certificateStatus === 'Issued' && !activeOperation
    const canRenew = hasCertificate && certificate?.renewalEligible === true && !activeOperation
    const canRefresh = hasCertificate && !activeOperation
    const canRevoke = hasCertificate && !['Revoked', 'Expired'].includes(certificateStatus) && !activeOperation

    const handleBack = () => {
        requestNavigation(`/storefront/domains/${id}`)
    }

    const refreshAll = async () => {
        await Promise.all([
            detailQuery.refetch(),
            sslQuery.refetch(),
            certificateQuery.refetch(),
            operationsQuery.refetch(),
        ])
    }

    const runCommand = async (action, execute, target = id) => {
        if (isMutating || commandInFlightRef.current) return
        commandInFlightRef.current = true
        clearError()
        setCommandName(action)
        const keyScope = `${action}:${target || ''}`
        const idempotencyKey = uncertainCommandKeysRef.current.get(keyScope) || createIdempotencyKey(action)
        uncertainCommandKeysRef.current.set(keyScope, idempotencyKey)
        try {
            const response = await execute(idempotencyKey)
            uncertainCommandKeysRef.current.delete(keyScope)
            setLastAccepted(response)
            const status = enumName(response.operationStatus, OPERATION_STATUS_NAMES)
            if (status === 'Failed') {
                showWarning(t('storefront.domains.ssl.acceptedFailedTitle', 'Operation accepted but failed'))
            } else {
                showInfo(status === 'Succeeded'
                    ? t('storefront.domains.ssl.completedTitle', 'Operation completed')
                    : t('storefront.domains.ssl.acceptedTitle', 'Operation accepted'))
            }
            await refreshAll()
        } catch (error) {
            // A response proves the server rejected the request, so a later user
            // attempt is new. Without a response, retain the key because the
            // server may have accepted the original request before transport loss.
            if (error?.response) uncertainCommandKeysRef.current.delete(keyScope)
            showWarning(t('storefront.domains.ssl.toast.commandFailed', 'Certificate command was not accepted.'))
        } finally {
            commandInFlightRef.current = false
            setCommandName(null)
        }
    }

    const handleProvision = () =>
        runCommand('provision', (idempotencyKey) => provisionSslCertificate(id, { idempotencyKey }))

    const handleActivate = () =>
        runCommand(
            'activate',
            (idempotencyKey) => activateSslCertificate(id, { certificateId, idempotencyKey }),
            certificateId,
        ).finally(() => setActivateOpen(false))

    const handleRenew = () =>
        runCommand(
            'renew',
            (idempotencyKey) => renewSslCertificate(id, { certificateId, idempotencyKey }),
            certificateId,
        )

    const handleRefresh = () =>
        runCommand(
            'refresh',
            (idempotencyKey) => refreshSslCertificate(id, { certificateId, idempotencyKey }),
            certificateId,
        )

    const handleRetry = (operation) =>
        runCommand(
            'retry',
            (idempotencyKey) => retrySslOperation(id, { operationId: operation.operationId, idempotencyKey }),
            operation.operationId,
        )

    const handleRevoke = async () => {
        await runCommand(
            'revoke',
            (idempotencyKey) => revokeSslCertificate(id, { certificateId, reason: revokeReason, idempotencyKey }),
            `${certificateId}:${revokeReason}`,
        )
        setRevokeOpen(false)
    }

    if (isLoading) {
        return <AuthoringLoadingState label={t('storefront.domains.ssl.loading', 'Loading SSL management data...')} />
    }

    if (isUnauthorized) {
        return (
            <SslPageFrame t={t} onBack={handleBack}>
                <AuthoringFeedback
                    kind="error"
                    title={t('storefront.domains.unauthorized', 'Authentication required')}
                    message={t('storefront.domains.unauthorizedDesc', 'Please sign in to manage domains.')}
                />
            </SslPageFrame>
        )
    }

    if (isNotFound) {
        return (
            <SslPageFrame t={t} onBack={handleBack}>
                <AuthoringEmptyState
                    title={t('storefront.domains.domainNotFound', 'Domain not found')}
                    description={t('storefront.domains.domainNotFoundDesc', 'The requested domain could not be found.')}
                />
            </SslPageFrame>
        )
    }

    if (loadError) {
        return (
            <SslPageFrame t={t} onBack={handleBack}>
                <AuthoringFeedback
                    kind="error"
                    title={t('storefront.domains.ssl.loadError', 'Unable to load SSL management')}
                    message={loadError}
                    action={{
                        label: t('common.retry', 'Retry'),
                        onClick: refreshAll,
                    }}
                />
            </SslPageFrame>
        )
    }

    const commandFailureMessage = getFailureMessage(t, errorCode)
    const latestFailureMessage = getFailureMessage(t, certificate?.failureCode)

    return (
        <div className="space-y-4">
            <div className="rounded-xl border border-gray-200 bg-white px-4 py-3 shadow-sm dark:border-gray-700 dark:bg-gray-800">
                <div className="flex flex-wrap items-center gap-3">
                    <AuthoringButton
                        action={{
                            label: t('common.back', 'Back'),
                            onClick: handleBack,
                            ariaLabel: t('storefront.domains.ssl.backToDomain', 'Back to domain detail'),
                        }}
                        defaultLabel={t('common.back', 'Back')}
                    />
                    <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-2">
                            <Shield size={22} className="text-indigo-600 dark:text-indigo-400" aria-hidden="true" />
                            <h1 className="text-xl font-semibold text-gray-900 dark:text-gray-100">
                                {t('storefront.domains.ssl.title', 'SSL Management')}
                            </h1>
                            <AuthoringStatusBadge state={getBadgeState(certificateStatus, certificateDegraded)}>
                                {getCertificateStatusLabel(t, certificateStatus, certificateDegraded)}
                            </AuthoringStatusBadge>
                            {domain?.isPrimary ? (
                                <AuthoringStatusBadge state="live">
                                    {t('storefront.domains.ssl.primaryDomain', 'Primary Domain')}
                                </AuthoringStatusBadge>
                            ) : null}
                        </div>
                        <p className="mt-0.5 break-all text-sm text-gray-500 dark:text-gray-400">
                            {hostname}
                        </p>
                    </div>
                    <div className="flex flex-wrap items-center gap-2" role="toolbar" aria-label={t('storefront.domains.ssl.actionsLabel', 'SSL certificate actions')}>
                        {canProvision ? (
                            <AuthoringButton
                                action={{
                                    label: t('storefront.domains.ssl.actions.provision', 'Provision certificate'),
                                    onClick: handleProvision,
                                    busy: commandName === 'provision' && commandBusy,
                                    busyLabel: t('storefront.domains.ssl.actions.provisioning', 'Provisioning...'),
                                    disabled: isMutating,
                                    icon: ShieldCheck,
                                }}
                                defaultLabel={t('storefront.domains.ssl.actions.provision', 'Provision certificate')}
                                tone="primary"
                            />
                        ) : null}
                        {canActivate ? (
                            <AuthoringButton
                                action={{
                                    label: t('storefront.domains.ssl.actions.activate', 'Activate certificate'),
                                    onClick: () => setActivateOpen(true),
                                    busy: commandName === 'activate' && commandBusy,
                                    busyLabel: t('storefront.domains.ssl.actions.activating', 'Activating...'),
                                    disabled: isMutating,
                                    icon: Zap,
                                }}
                                defaultLabel={t('storefront.domains.ssl.actions.activate', 'Activate certificate')}
                                tone="primary"
                            />
                        ) : null}
                        {canRenew ? (
                            <AuthoringButton
                                action={{
                                    label: t('storefront.domains.ssl.actions.renew', 'Renew certificate'),
                                    onClick: handleRenew,
                                    busy: commandName === 'renew' && commandBusy,
                                    busyLabel: t('storefront.domains.ssl.actions.renewing', 'Renewing...'),
                                    disabled: isMutating,
                                    icon: RotateCcw,
                                    title: t('storefront.domains.ssl.renewHint', 'Renewal reconciles a replacement generation.'),
                                }}
                                defaultLabel={t('storefront.domains.ssl.actions.renew', 'Renew certificate')}
                            />
                        ) : null}
                        {canRefresh ? (
                            <AuthoringButton
                                action={{
                                    label: t('storefront.domains.ssl.actions.refresh', 'Refresh state'),
                                    onClick: handleRefresh,
                                    busy: commandName === 'refresh' && commandBusy,
                                    busyLabel: t('storefront.domains.ssl.actions.refreshing', 'Refreshing...'),
                                    disabled: isMutating,
                                    icon: RefreshCw,
                                    title: t('storefront.domains.ssl.refreshHint', 'Reconcile certificate state with edge and SNI evidence.'),
                                }}
                                defaultLabel={t('storefront.domains.ssl.actions.refresh', 'Refresh state')}
                            />
                        ) : null}
                        {canRevoke ? (
                            <AuthoringButton
                                action={{
                                    label: t('storefront.domains.ssl.actions.revoke', 'Revoke'),
                                    onClick: () => {
                                        clearError()
                                        setRevokeOpen(true)
                                    },
                                    disabled: isMutating,
                                    icon: Trash2,
                                    ariaLabel: t('storefront.domains.ssl.actions.revokeAria', 'Revoke certificate'),
                                }}
                                defaultLabel={t('storefront.domains.ssl.actions.revoke', 'Revoke')}
                                tone="danger"
                            />
                        ) : null}
                    </div>
                </div>
            </div>

            {activeOperation ? (
                <AuthoringFeedback
                    kind="info"
                    title={t('storefront.domains.ssl.operationRunningTitle', 'Certificate operation in progress')}
                    message={t(
                        'storefront.domains.ssl.operationRunningMessage',
                        'Another operation is currently {{status}} in phase {{phase}}. Refresh when it settles before starting a new command.',
                        {
                            status: getOperationStatusLabel(t, enumName(activeOperation.status, OPERATION_STATUS_NAMES)),
                            phase: getOperationPhaseLabel(t, enumName(activeOperation.phase, OPERATION_PHASE_NAMES)),
                        },
                    )}
                />
            ) : null}

            {lastAccepted ? (
                <AuthoringFeedback
                    kind={enumName(lastAccepted.operationStatus, OPERATION_STATUS_NAMES) === 'Failed' ? 'warning' : 'success'}
                    title={getAcceptedTitle(t, lastAccepted)}
                    message={t(
                        'storefront.domains.ssl.acceptedMessage',
                        'Operation {{operationId}} is {{status}} in phase {{phase}}.',
                        {
                            operationId: lastAccepted.operationId,
                            status: getOperationStatusLabel(t, enumName(lastAccepted.operationStatus, OPERATION_STATUS_NAMES)),
                            phase: getOperationPhaseLabel(t, enumName(lastAccepted.operationPhase, OPERATION_PHASE_NAMES)),
                        },
                    )}
                />
            ) : null}

            {mutationError ? (
                <AuthoringFeedback
                    kind={mutationStatus === 'infrastructure-error' ? 'error' : 'warning'}
                    title={getCommandErrorTitle(t, errorCode)}
                    message={commandFailureMessage || mutationError}
                    action={{
                        label: t('storefront.domains.ssl.actions.refresh', 'Refresh state'),
                        onClick: refreshAll,
                    }}
                />
            ) : null}

            {certificate?.failureCode ? (
                <AuthoringFeedback
                    kind={certificate?.retryable ? 'warning' : 'error'}
                    title={certificate?.retryable
                        ? t('storefront.domains.ssl.retryableFailureTitle', 'Operation failed and can be retried')
                        : t('storefront.domains.ssl.failureTitle', 'Certificate attention required')}
                    message={latestFailureMessage}
                />
            ) : null}

            {!hasCertificate ? (
                <AuthoringEmptyState
                    title={t('storefront.domains.ssl.emptyTitle', 'No certificate provisioned')}
                    description={t(
                        'storefront.domains.ssl.emptyDescription',
                        'This domain has no certificate generation yet. Provisioning starts an asynchronous request and the server remains authoritative for completion.',
                    )}
                    action={canProvision ? {
                        label: t('storefront.domains.ssl.actions.provision', 'Provision certificate'),
                        onClick: handleProvision,
                        disabled: isMutating,
                    } : undefined}
                />
            ) : null}

            <section className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800" aria-labelledby="certificate-summary-title">
                <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
                    <div>
                        <h2 id="certificate-summary-title" className="text-lg font-semibold text-gray-900 dark:text-gray-100">
                            {t('storefront.domains.ssl.summaryTitle', 'Certificate summary')}
                        </h2>
                        <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
                            {t('storefront.domains.ssl.summarySubtitle', 'Safe metadata from the certificate API.')}
                        </p>
                    </div>
                    <AuthoringStatusBadge state={getBadgeState(certificateStatus, certificateDegraded)}>
                        {getCertificateStatusLabel(t, certificateStatus, certificateDegraded)}
                    </AuthoringStatusBadge>
                </div>
                <MetadataGrid
                    items={[
                        [t('storefront.domains.ssl.fields.hostname', 'Domain hostname'), hostname],
                        [t('storefront.domains.ssl.fields.generation', 'Certificate generation'), certificate?.generation],
                        [t('storefront.domains.ssl.fields.status', 'Status'), getCertificateStatusLabel(t, certificateStatus, certificateDegraded)],
                        [t('storefront.domains.ssl.fields.provider', 'Provider'), getProviderLabel(t, certificate?.provider ?? ssl?.provider)],
                        [t('storefront.domains.ssl.fields.issuer', 'Issuer'), certificate?.issuer],
                        [t('storefront.domains.ssl.fields.serial', 'Serial'), certificate?.serialNumber],
                        [t('storefront.domains.ssl.fields.fingerprint', 'Fingerprint'), certificate?.fingerprint || certificate?.thumbprint || ssl?.thumbprint],
                        [t('storefront.domains.ssl.fields.notBefore', 'Not before'), formatDate(certificate?.notBeforeUtc, i18n.language)],
                        [t('storefront.domains.ssl.fields.notAfter', 'Not after'), formatDate(certificate?.notAfterUtc || ssl?.notAfterUtc, i18n.language)],
                        [t('storefront.domains.ssl.fields.renewalState', 'Renewal state'), getRenewalState(t, certificate, daysUntilExpiry)],
                        [t('storefront.domains.ssl.fields.lastOperation', 'Last successful operation'), lastOperationStatus === 'Succeeded' ? certificate?.lastOperationId : null],
                        [t('storefront.domains.ssl.fields.lastFailure', 'Last failure code'), certificate?.failureCode],
                        [t('storefront.domains.ssl.fields.lastUpdated', 'Last updated'), formatDate(certificate?.lastOperationUpdatedAtUtc, i18n.language)],
                    ]}
                />
            </section>

            <section className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800" aria-labelledby="certificate-lifecycle-title">
                <h2 id="certificate-lifecycle-title" className="text-lg font-semibold text-gray-900 dark:text-gray-100">
                    {t('storefront.domains.ssl.lifecycleTitle', 'Lifecycle')}
                </h2>
                <ol className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-7">
                    {['Provision', 'Issued', 'Activate', 'SNI Proof', 'Active', 'Renew', 'Retire'].map((step) => (
                        <li
                            key={step}
                            className="rounded-lg border border-gray-200 bg-gray-50 p-3 text-sm dark:border-gray-700 dark:bg-gray-700/50"
                        >
                            <span className="font-medium text-gray-900 dark:text-gray-100">
                                {t(`storefront.domains.ssl.lifecycle.${step}`, step)}
                            </span>
                        </li>
                    ))}
                </ol>
                <p className="mt-3 text-sm text-gray-500 dark:text-gray-400">
                    {t(
                        'storefront.domains.ssl.lifecycleNote',
                        'The backend owns lifecycle transitions. This screen only presents current state and sends commands.',
                    )}
                </p>
            </section>

            <section className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800" aria-labelledby="operations-title">
                <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
                    <div>
                        <h2 id="operations-title" className="text-lg font-semibold text-gray-900 dark:text-gray-100">
                            {t('storefront.domains.ssl.operationsTitle', 'Certificate operations')}
                        </h2>
                        <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
                            {t('storefront.domains.ssl.operationsSubtitle', 'Recent server-recorded certificate work.')}
                        </p>
                    </div>
                    <AuthoringButton
                        action={{
                            label: t('common.refresh', 'Refresh'),
                            onClick: () => operationsQuery.refetch(),
                            busy: operationsQuery.isRefreshing,
                            icon: RefreshCw,
                        }}
                        defaultLabel={t('common.refresh', 'Refresh')}
                    />
                </div>

                {operations.length === 0 ? (
                    <AuthoringEmptyState
                        title={t('storefront.domains.ssl.noOperationsTitle', 'No certificate operations yet')}
                        description={t('storefront.domains.ssl.noOperationsDescription', 'Server-recorded operations will appear here after certificate work starts.')}
                    />
                ) : (
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200 text-sm dark:divide-gray-700">
                            <caption className="sr-only">{t('storefront.domains.ssl.operationsCaption', 'Certificate operation history')}</caption>
                            <thead className="bg-gray-50 dark:bg-gray-700/50">
                                <tr>
                                    <TableHeader>{t('storefront.domains.ssl.table.type', 'Type')}</TableHeader>
                                    <TableHeader>{t('storefront.domains.ssl.table.status', 'Status')}</TableHeader>
                                    <TableHeader>{t('storefront.domains.ssl.table.phase', 'Phase')}</TableHeader>
                                    <TableHeader>{t('storefront.domains.ssl.table.updated', 'Updated')}</TableHeader>
                                    <TableHeader>{t('storefront.domains.ssl.table.retryable', 'Retryable')}</TableHeader>
                                    <TableHeader>{t('storefront.domains.ssl.table.failure', 'Failure')}</TableHeader>
                                    <TableHeader>{t('storefront.domains.ssl.table.actions', 'Actions')}</TableHeader>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100 bg-white dark:divide-gray-700 dark:bg-gray-800">
                                {operations.map((operation) => {
                                    const opStatus = enumName(operation.status, OPERATION_STATUS_NAMES)
                                    const opPhase = enumName(operation.phase, OPERATION_PHASE_NAMES)
                                    const retryReady = operation.retryable === true &&
                                        opStatus === 'Failed' &&
                                        isRetryWindowOpen(operation.retryAfterUtc)
                                    return (
                                        <tr key={operation.operationId}>
                                            <TableCell>{getOperationTypeLabel(t, enumName(operation.operationType, OPERATION_TYPE_NAMES))}</TableCell>
                                            <TableCell>
                                                <AuthoringStatusBadge state={getOperationBadgeState(opStatus)}>
                                                    {getOperationStatusLabel(t, opStatus)}
                                                </AuthoringStatusBadge>
                                            </TableCell>
                                            <TableCell>{getOperationPhaseLabel(t, opPhase)}</TableCell>
                                            <TableCell>{formatDate(operation.updatedAtUtc || operation.createdAtUtc, i18n.language)}</TableCell>
                                            <TableCell>
                                                {operation.retryable
                                                    ? t('storefront.domains.ssl.values.yes', 'Yes')
                                                    : t('storefront.domains.ssl.values.no', 'No')}
                                                {operation.retryAfterUtc && !isRetryWindowOpen(operation.retryAfterUtc)
                                                    ? ` ${t('storefront.domains.ssl.retryAfterInline', 'after {{time}}', { time: formatDate(operation.retryAfterUtc, i18n.language) })}`
                                                    : ''}
                                            </TableCell>
                                            <TableCell>{operation.failureCode || t('storefront.domains.ssl.values.none', 'None')}</TableCell>
                                            <TableCell>
                                                <div className="flex flex-wrap gap-2">
                                                    <AuthoringButton
                                                        action={{
                                                            label: t('storefront.domains.ssl.actions.details', 'Details'),
                                                            onClick: () => setSelectedOperation(operation),
                                                            icon: Eye,
                                                            ariaLabel: t('storefront.domains.ssl.actions.detailsAria', 'View certificate operation details'),
                                                        }}
                                                        defaultLabel={t('storefront.domains.ssl.actions.details', 'Details')}
                                                    />
                                                    {retryReady ? (
                                                        <AuthoringButton
                                                            action={{
                                                                label: t('storefront.domains.ssl.actions.retry', 'Retry'),
                                                                onClick: () => handleRetry(operation),
                                                                busy: commandName === 'retry' && commandBusy,
                                                                disabled: isMutating,
                                                                icon: RefreshCw,
                                                            }}
                                                            defaultLabel={t('storefront.domains.ssl.actions.retry', 'Retry')}
                                                            tone="primary"
                                                        />
                                                    ) : null}
                                                </div>
                                            </TableCell>
                                        </tr>
                                    )
                                })}
                            </tbody>
                        </table>
                    </div>
                )}

                {operationsPage?.totalPages > 1 ? (
                    <div className="mt-4 flex flex-wrap items-center justify-between gap-3 text-sm text-gray-600 dark:text-gray-300">
                        <span>
                            {t('storefront.domains.ssl.pagination', 'Page {{page}} of {{totalPages}}', {
                                page: operationsPage.page,
                                totalPages: operationsPage.totalPages,
                            })}
                        </span>
                        <div className="flex gap-2">
                            <AuthoringButton
                                action={{
                                    label: t('common.previous', 'Previous'),
                                    onClick: operationsQuery.previousPage,
                                    disabled: !operationsPage.hasPreviousPage,
                                }}
                                defaultLabel={t('common.previous', 'Previous')}
                            />
                            <AuthoringButton
                                action={{
                                    label: t('common.next', 'Next'),
                                    onClick: operationsQuery.nextPage,
                                    disabled: !operationsPage.hasNextPage,
                                }}
                                defaultLabel={t('common.next', 'Next')}
                            />
                        </div>
                    </div>
                ) : null}
            </section>

            <RevokeDialog
                open={revokeOpen}
                busy={commandName === 'revoke' && commandBusy}
                t={t}
                hostname={hostname}
                generation={certificate?.generation}
                status={getCertificateStatusLabel(t, certificateStatus, certificateDegraded)}
                reason={revokeReason}
                onReasonChange={setRevokeReason}
                onConfirm={handleRevoke}
                onClose={() => setRevokeOpen(false)}
            />

            <ActivateDialog
                open={activateOpen}
                busy={commandName === 'activate' && commandBusy}
                t={t}
                onConfirm={handleActivate}
                onClose={() => setActivateOpen(false)}
            />

            <OperationDetailsDialog
                operation={selectedOperation}
                t={t}
                language={i18n.language}
                onClose={() => setSelectedOperation(null)}
            />
        </div>
    )
}

function ActivateDialog({ open, busy, t, onConfirm, onClose }) {
    return (
        <AuthoringDialog
            open={open}
            title={t('storefront.domains.ssl.activateDialog.title', 'Activate certificate')}
            description={t(
                'storefront.domains.ssl.activateDialog.description',
                'Activation reconciles Caddy edge configuration and requires SNI proof before the certificate can become Active.',
            )}
            onClose={busy ? undefined : onClose}
        >
            <AuthoringFeedback
                kind="info"
                title={t('storefront.domains.ssl.activateDialog.proofTitle', 'Proof required')}
                message={t(
                    'storefront.domains.ssl.activateDialog.proofMessage',
                    'The certificate will remain pending unless the edge serves the expected certificate evidence.',
                )}
            />
            <div className="mt-6 flex flex-wrap justify-end gap-2">
                <AuthoringButton
                    action={{
                        label: t('common.cancel', 'Cancel'),
                        onClick: onClose,
                        disabled: busy,
                    }}
                    defaultLabel={t('common.cancel', 'Cancel')}
                />
                <AuthoringButton
                    action={{
                        label: t('storefront.domains.ssl.actions.activate', 'Activate certificate'),
                        onClick: onConfirm,
                        busy,
                        busyLabel: t('storefront.domains.ssl.actions.activating', 'Activating...'),
                        disabled: busy,
                        icon: Zap,
                    }}
                    defaultLabel={t('storefront.domains.ssl.actions.activate', 'Activate certificate')}
                    tone="primary"
                />
            </div>
        </AuthoringDialog>
    )
}

function SslPageFrame({ t, onBack, children }) {
    return (
        <div className="space-y-4">
            <AuthoringButton
                action={{
                    label: t('common.back', 'Back'),
                    onClick: onBack,
                }}
                defaultLabel={t('common.back', 'Back')}
            />
            {children}
        </div>
    )
}

function MetadataGrid({ items }) {
    return (
        <dl className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {items.map(([label, value]) => (
                <div key={label} className="rounded-lg border border-gray-200 bg-gray-50 p-3 dark:border-gray-700 dark:bg-gray-700/50">
                    <dt className="text-xs font-medium uppercase text-gray-500 dark:text-gray-400">{label}</dt>
                    <dd className="mt-1 break-all text-sm text-gray-900 dark:text-gray-100">{formatValue(value)}</dd>
                </div>
            ))}
        </dl>
    )
}

function TableHeader({ children }) {
    return <th scope="col" className="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-500 dark:text-gray-300">{children}</th>
}

function TableCell({ children }) {
    return <td className="px-3 py-3 align-top text-gray-700 dark:text-gray-200">{children}</td>
}

function RevokeDialog({ open, busy, t, hostname, generation, status, reason, onReasonChange, onConfirm, onClose }) {
    return (
        <AuthoringDialog
            open={open}
            title={t('storefront.domains.ssl.revokeDialog.title', 'Revoke certificate')}
            description={t(
                'storefront.domains.ssl.revokeDialog.description',
                'Revocation is destructive for the selected generation. Choose a supported reason before requesting it.',
            )}
            onClose={busy ? undefined : onClose}
        >
            <div className="mt-4 space-y-3 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-900">
                <div className="flex items-start gap-2">
                    <AlertTriangle size={18} className="mt-0.5 shrink-0" aria-hidden="true" />
                    <p>{t('storefront.domains.ssl.revokeDialog.warning', 'This action asks the server to revoke the current certificate generation.')}</p>
                </div>
                <p><strong>{t('storefront.domains.ssl.fields.hostname', 'Domain hostname')}:</strong> {hostname}</p>
                <p><strong>{t('storefront.domains.ssl.fields.generation', 'Certificate generation')}:</strong> {formatValue(generation)}</p>
                <p><strong>{t('storefront.domains.ssl.fields.status', 'Status')}:</strong> {status}</p>
            </div>
            <label className="mt-4 block text-sm font-medium text-gray-700">
                {t('storefront.domains.ssl.revokeDialog.reasonLabel', 'Reason')}
                <select
                    value={reason}
                    onChange={(event) => onReasonChange(event.target.value)}
                    disabled={busy}
                    className="mt-2 w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100 disabled:opacity-50"
                >
                    {REVOCATION_REASONS.map((code) => (
                        <option key={code} value={code}>
                            {t(`storefront.domains.ssl.revocationReasons.${code}`, code)}
                        </option>
                    ))}
                </select>
            </label>
            <div className="mt-6 flex flex-wrap justify-end gap-2">
                <AuthoringButton
                    action={{
                        label: t('common.cancel', 'Cancel'),
                        onClick: onClose,
                        disabled: busy,
                    }}
                    defaultLabel={t('common.cancel', 'Cancel')}
                />
                <AuthoringButton
                    action={{
                        label: t('storefront.domains.ssl.actions.confirmRevoke', 'Request revocation'),
                        onClick: onConfirm,
                        busy,
                        busyLabel: t('storefront.domains.ssl.actions.revoking', 'Requesting...'),
                        disabled: busy,
                        icon: ShieldOff,
                    }}
                    defaultLabel={t('storefront.domains.ssl.actions.confirmRevoke', 'Request revocation')}
                    tone="danger"
                />
            </div>
        </AuthoringDialog>
    )
}

function OperationDetailsDialog({ operation, t, language, onClose }) {
    if (!operation) return null
    const status = enumName(operation.status, OPERATION_STATUS_NAMES)
    return (
        <AuthoringDialog
            open={Boolean(operation)}
            title={t('storefront.domains.ssl.operationDetails.title', 'Operation details')}
            description={t('storefront.domains.ssl.operationDetails.description', 'Safe operation metadata recorded by the server.')}
            onClose={onClose}
        >
            <MetadataGrid
                items={[
                    [t('storefront.domains.ssl.operationDetails.operationId', 'Operation ID'), operation.operationId],
                    [t('storefront.domains.ssl.table.type', 'Type'), getOperationTypeLabel(t, enumName(operation.operationType, OPERATION_TYPE_NAMES))],
                    [t('storefront.domains.ssl.table.status', 'Status'), getOperationStatusLabel(t, status)],
                    [t('storefront.domains.ssl.table.phase', 'Phase'), getOperationPhaseLabel(t, enumName(operation.phase, OPERATION_PHASE_NAMES))],
                    [t('storefront.domains.ssl.fields.generation', 'Certificate generation'), operation.generation],
                    [t('storefront.domains.ssl.operationDetails.started', 'Started'), formatDate(operation.createdAtUtc, language)],
                    [t('storefront.domains.ssl.operationDetails.updated', 'Updated'), formatDate(operation.updatedAtUtc, language)],
                    [t('storefront.domains.ssl.operationDetails.completed', 'Completed'), formatDate(operation.completedAtUtc, language)],
                    [t('storefront.domains.ssl.table.failure', 'Failure'), operation.failureCode],
                    [t('storefront.domains.ssl.table.retryable', 'Retryable'), operation.retryable ? t('storefront.domains.ssl.values.yes', 'Yes') : t('storefront.domains.ssl.values.no', 'No')],
                    [t('storefront.domains.ssl.operationDetails.retryAfter', 'Retry after'), formatDate(operation.retryAfterUtc, language)],
                    [t('storefront.domains.ssl.operationDetails.attempts', 'Attempts'), operation.attemptCount],
                ]}
            />
            {operation.failureCode ? (
                <div className="mt-4">
                    <AuthoringFeedback
                        kind={operation.retryable ? 'warning' : 'error'}
                        title={t('storefront.domains.ssl.operationDetails.failureTitle', 'Failure summary')}
                        message={getFailureMessage(t, operation.failureCode)}
                    />
                </div>
            ) : null}
            <div className="mt-6 flex justify-end">
                <AuthoringButton
                    action={{
                        label: t('common.close', 'Close'),
                        onClick: onClose,
                    }}
                    defaultLabel={t('common.close', 'Close')}
                />
            </div>
        </AuthoringDialog>
    )
}

function formatValue(value) {
    if (value === null || value === undefined || value === '') return '-'
    return String(value)
}

function formatDate(value, language) {
    if (!value) return null
    const date = new Date(value)
    if (Number.isNaN(date.getTime())) return null
    return new Intl.DateTimeFormat(language || undefined, {
        dateStyle: 'medium',
        timeStyle: 'short',
    }).format(date)
}

function getCertificateStatusLabel(t, status, degraded) {
    if (degraded && status === 'Active') return t('storefront.domains.ssl.status.Degraded', 'Degraded')
    return t(`storefront.domains.ssl.status.${status}`, status || 'Unavailable')
}

function getOperationStatusLabel(t, status) {
    return t(`storefront.domains.ssl.operationStatus.${status}`, status || 'Unknown')
}

function getOperationTypeLabel(t, type) {
    return t(`storefront.domains.ssl.operationType.${type}`, type || 'Unknown')
}

function getOperationPhaseLabel(t, phase) {
    return t(`storefront.domains.ssl.operationPhase.${phase}`, phase || 'Unknown')
}

function getProviderLabel(t, provider) {
    const name = enumName(provider, PROVIDER_NAMES)
    if (!name) return t('storefront.domains.notConfigured', 'Not configured')
    return t(`storefront.domains.ssl.provider.${name}`, name)
}

function getRenewalState(t, certificate, daysUntilExpiry) {
    if (!certificate?.certificateId) return t('storefront.domains.ssl.renewal.notAvailable', 'Not available')
    if (certificate.renewalEligible) return t('storefront.domains.ssl.renewal.eligible', 'Renewal eligible')
    if (certificate.nextRenewalUtc) {
        return t('storefront.domains.ssl.renewal.scheduled', 'Scheduled for {{date}}', {
            date: formatDate(certificate.nextRenewalUtc),
        })
    }
    if (typeof daysUntilExpiry === 'number') {
        return t('storefront.domains.ssl.renewal.expiresIn', 'Expires in {{days}} days', { days: daysUntilExpiry })
    }
    return t('storefront.domains.ssl.renewal.managed', 'Managed by server policy')
}

function getOperationBadgeState(status) {
    if (status === 'Succeeded') return 'published'
    if (status === 'Failed' || status === 'Cancelled') return 'error'
    if (status === 'Running' || status === 'Pending') return 'saving'
    return 'draft'
}

function getAcceptedTitle(t, response) {
    const status = enumName(response.operationStatus, OPERATION_STATUS_NAMES)
    if (status === 'Succeeded') {
        return t('storefront.domains.ssl.completedTitle', 'Operation completed')
    }
    if (status === 'Failed') {
        return t('storefront.domains.ssl.acceptedFailedTitle', 'Operation accepted but failed')
    }
    return t('storefront.domains.ssl.acceptedTitle', 'Operation accepted')
}

function getCommandErrorTitle(t, errorCode) {
    if (errorCode === 'RATE_LIMITED') return t('storefront.domains.ssl.errors.rateLimitedTitle', 'Rate limited')
    if (errorCode === 'DOMAIN_CONFLICT' || errorCode === 'CONFLICT') return t('storefront.domains.ssl.errors.conflictTitle', 'Operation conflict')
    if (errorCode === 'UNAUTHORIZED') return t('storefront.domains.ssl.errors.forbiddenTitle', 'Permission required')
    return t('storefront.domains.ssl.errors.commandTitle', 'Command failed')
}

function getFailureMessage(t, code) {
    if (!code) return null
    if (code === 'RATE_LIMITED') {
        return t(
            'storefront.domains.ssl.failureCodes.RATE_LIMITED',
            'This operation cannot be retried yet. Refresh after the retry window.',
        )
    }
    return t(
        `storefront.domains.ssl.failureCodes.${code}`,
        t('storefront.domains.ssl.failureCodes.UNKNOWN', 'The server reported a certificate failure. Refresh the state or contact support if it persists.'),
    )
}

export default SslManagementPage

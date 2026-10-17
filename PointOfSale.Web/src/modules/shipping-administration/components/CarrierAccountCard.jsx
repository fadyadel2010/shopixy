import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import CarrierConnectionForm from './CarrierConnectionForm'
import {
    activateCarrierAccount,
    disableCarrierAccount,
    evaluateCarrierHealth,
    getCarrierDisplayName,
    retireCarrierAccount,
    runCarrierConnectionTest,
    selectCarrierEnvironment,
    suspendCarrierAccount,
} from '../shippingAdministrationApi'

const statusClassByState = {
    Healthy: 'bg-green-100 text-green-800 border-green-200',
    Connected: 'bg-green-100 text-green-800 border-green-200',
    Ready: 'bg-green-100 text-green-800 border-green-200',
    Warning: 'bg-amber-100 text-amber-800 border-amber-200',
    'Credentials saved': 'bg-blue-100 text-blue-800 border-blue-200',
    'Action required': 'bg-amber-100 text-amber-800 border-amber-200',
    'Connection failed': 'bg-red-100 text-red-800 border-red-200',
    'Provider unavailable': 'bg-amber-100 text-amber-800 border-amber-200',
    'Not configured': 'bg-gray-100 text-gray-700 border-gray-200',
    Blocked: 'bg-red-100 text-red-800 border-red-200',
    Disabled: 'bg-gray-100 text-gray-700 border-gray-200',
    Retired: 'bg-slate-100 text-slate-500 border-slate-200',
}

function StatusBadge({ value, label }) {
    return (
        <span className={`inline-flex rounded-full border px-2 py-1 text-xs font-semibold ${statusClassByState[value] || 'bg-gray-100 text-gray-700 border-gray-200'}`}>
            {label}
        </span>
    )
}

function Detail({ label, value }) {
    return (
        <div>
            <div className="text-xs font-medium uppercase tracking-wide text-gray-500">{label}</div>
            <div className="mt-1 text-sm font-medium text-gray-900">{value || '-'}</div>
        </div>
    )
}

const emptyConnectionForm = {
    apiKey: '',
    apiSecret: '',
    accountId: '',
    webhookSecret: '',
    useSandbox: true,
    extraConfig: {},
}

function toNullableText(value) {
    const text = value.trim()
    return text ? text : null
}

function describeConnectionTest(carrierDefinition, t) {
    if (carrierDefinition?.connectionTestSupported === false) {
        return t('shippingAdministration.account.connectionUnsupported')
    }

    if (carrierDefinition?.connectionTestMode === 'LocalOnly') {
        return t('shippingAdministration.account.connectionLocalOnly')
    }

    return t('shippingAdministration.account.connectionRemote')
}

function hasBlocker(readiness, code) {
    return readiness?.blockers?.some((blocker) => blocker === code) === true
}

function isProviderConnectivityFailure(code) {
    return ['CONNECTION_ERROR', 'NETWORK_ERROR', 'PROVIDER_UNAVAILABLE', 'CONNECTION_TEST_EXCEPTION']
        .includes((code || '').toUpperCase())
}

function getMerchantIntegrationState(account, carrierDefinition, t, outcome) {
    const readiness = account.integrationReadiness
    const test = account.lastConnectionTest

    if (['bosta', 'aramex', 'dhl', 'mylerz'].includes(account.carrierCode)) {
        const configured = carrierDefinition?.credentialFields?.filter(field => field.required).every(field => account.savedCredentialFields?.includes(field.key)) && account.savedCredentialFields?.length > 0
        const current = carrierDefinition?.connectionTestMode === 'Remote' ? outcome || (account.connectionTestIsCurrent ? test : null) : null
        if (!configured) return { badge: 'Not configured', title: t('shippingAdministration.account.notConfigured'), message: t('shippingAdministration.connection.saveFirst'), checks: ['credentialsNotSaved', 'connectionNotVerified'] }
        if (current?.status === 'Failed') return { badge: 'Connection failed', title: t('shippingAdministration.account.verificationFailed'), message: t('shippingAdministration.account.savedRetry'), checks: ['credentialsSaved', 'connectionNotVerified'] }
        if (current?.status === 'Succeeded') return { badge: 'Connected', title: t('shippingAdministration.connection.authenticated'), message: t('shippingAdministration.connection.entitlement'), checks: ['credentialsSaved', 'connectionVerified'] }
        return { badge: 'Credentials saved', title: t('shippingAdministration.account.credentialsSaved'), message: t('shippingAdministration.connection.notTested'), checks: ['credentialsSaved', 'connectionNotVerified'] }
    }

    if (carrierDefinition?.accountRequired === false && !['Unavailable', 'NotCertified'].includes(carrierDefinition.productionCapability)) {
        return {
            badge: 'Ready',
            title: t('shippingAdministration.account.manualReady'),
            message: t('shippingAdministration.account.manualReadyDescription'),
            checks: ['noCredentialsRequired', 'noConnectionRequired', 'readyForCheckout'],
        }
    }

    if (carrierDefinition && ['Unavailable', 'NotCertified'].includes(carrierDefinition.productionCapability)) {
        return {
            badge: 'Action required',
            title: t(carrierDefinition.productionCapability === 'NotCertified'
                ? 'shippingAdministration.account.productionNotCertified'
                : 'shippingAdministration.account.providerUnavailable'),
            message: t('shippingAdministration.account.providerUnavailableDescription'),
            checks: ['credentialsNotRequiredYet', 'verificationUnavailable'],
        }
    }

    if (!readiness || readiness.credentialAvailability !== 'Available') {
        return {
            badge: 'Not configured',
            title: t('shippingAdministration.account.notConfigured'),
            message: t('shippingAdministration.account.saveBeforeVerify'),
            checks: ['credentialsNotSaved', 'connectionNotVerified'],
        }
    }

    if (test?.status === 'Failed') {
        const providerFailure = isProviderConnectivityFailure(test.failureCode)
        return {
            badge: providerFailure ? 'Provider unavailable' : 'Connection failed',
            title: t(providerFailure ? 'shippingAdministration.account.providerUnreachable' : 'shippingAdministration.account.verificationFailed'),
            message: providerFailure
                ? t('shippingAdministration.account.savedNotInvalid')
                : t('shippingAdministration.account.savedRetry'),
            checks: ['credentialsSaved', providerFailure ? 'providerUnreachable' : 'connectionNotVerified'],
        }
    }

    if (readiness.canExecute) {
        return {
            badge: 'Connected',
            title: t('shippingAdministration.account.connected'),
            message: t('shippingAdministration.account.connectedDescription'),
            checks: ['credentialsSaved', 'connectionVerified', 'readyForCheckout'],
        }
    }

    if (hasBlocker(readiness, 'CARRIER_CONNECTION_NOT_VERIFIED')) {
        return {
            badge: 'Credentials saved',
            title: t('shippingAdministration.account.credentialsSaved'),
            message: t('shippingAdministration.account.runValidation'),
            checks: ['credentialsSaved', 'connectionNotVerified'],
        }
    }

    if (hasBlocker(readiness, 'CARRIER_CONFIGURATION_DISABLED')) {
        return {
            badge: 'Disabled',
            title: t('shippingAdministration.account.configurationDisabled'),
            message: t('shippingAdministration.account.enableBeforeCheckout'),
            checks: ['credentialsSaved', 'connectionPending', 'configurationDisabled'],
        }
    }

    return {
        badge: 'Action required',
        title: t('shippingAdministration.account.actionRequired'),
        message: t('shippingAdministration.account.reviewDiagnostics'),
        checks: ['credentialsSaved', test?.status === 'Succeeded'
            ? (carrierDefinition?.connectionTestMode === 'LocalOnly' ? 'localCredentialsValidated' : 'connectionVerified')
            : 'connectionPending'],
    }
}

export default function CarrierAccountCard({ account, supportedCarriers, onMutated }) {
    const { t, i18n } = useTranslation()
    const carrierDefinition = supportedCarriers.find((carrier) => carrier.carrierCode === account.carrierCode)
    const displayName = getCarrierDisplayName(supportedCarriers, account.carrierCode)
    const accountRequired = carrierDefinition?.accountRequired !== false
    const enabledServices = account.serviceLevels.filter((level) => level.isEnabled)
    const connectionStatus = ['bosta', 'aramex', 'dhl', 'mylerz'].includes(account.carrierCode) && !account.connectionTestIsCurrent
        ? 'Not Tested' : account.lastConnectionTest?.status || 'Not Tested'
    const credentialFields = carrierDefinition?.credentialFields || []
    const connectionTestSupported = carrierDefinition?.connectionTestSupported === true
    const activationSupported = carrierDefinition && ['ProductionReady', 'Partial'].includes(carrierDefinition.productionCapability)
    const readiness = account.integrationReadiness
    const connectionOnly = ['bosta', 'aramex', 'dhl', 'mylerz'].includes(account.carrierCode)
    const [connectionOutcome, setConnectionOutcome] = useState(null)
    useEffect(() => { setConnectionOutcome(null) }, [account.lastConnectionTest?.id, account.connectionTestIsCurrent, account.activeCredentialReferenceId])
    const merchantState = getMerchantIntegrationState(account, carrierDefinition, t, connectionOutcome)
    const [environment, setEnvironment] = useState(account.environment || '')
    const [connectionForm, setConnectionForm] = useState(emptyConnectionForm)
    const [workingAction, setWorkingAction] = useState('')
    const [message, setMessage] = useState('')
    const activeAccountRef = useRef(null)
    const pendingActionRef = useRef(null)
    useEffect(() => {
        const target = { id: account.id, carrier: account.carrierCode }
        activeAccountRef.current = target
        pendingActionRef.current = null
        setConnectionForm(emptyConnectionForm)
        setWorkingAction('')
        setMessage('')
        setConnectionOutcome(null)
        return () => { activeAccountRef.current = null; pendingActionRef.current = null }
    }, [account.id, account.carrierCode])
    useEffect(() => { setEnvironment(account.environment || '') }, [account.id, account.environment])

    const runAction = async (actionName, action, onSuccess) => {
        const target = activeAccountRef.current
        if (!target || pendingActionRef.current) return
        const pending = { target }
        pendingActionRef.current = pending
        setWorkingAction(actionName)
        setMessage('')
        try {
            await action()
            if (activeAccountRef.current !== target) return
            onSuccess?.()
            await onMutated()
            if (activeAccountRef.current !== target) return
            setMessage(t('shippingAdministration.account.actionCompleted', { action: actionName }))
        } catch (error) {
            if (activeAccountRef.current !== target) return
            await onMutated().catch(() => {})
            if (activeAccountRef.current !== target) return
            setMessage(error.message || t('shippingAdministration.account.actionFailed', { action: actionName }))
        } finally {
            if (pendingActionRef.current === pending) {
                pendingActionRef.current = null
                setWorkingAction('')
            }
        }
    }

    const submitEnvironment = (event) => {
        event.preventDefault()
        if (!environment) return
        runAction(t('shippingAdministration.account.actions.environmentSelection'), () => selectCarrierEnvironment(account.id, environment))
    }

    const submitConnectionTest = (event, saveOnly = false) => {
        event.preventDefault()
        if ((!saveOnly && !connectionTestSupported) || !areRequiredCredentialFieldsPresent()) return
        runAction(t(saveOnly ? 'common.save' : 'shippingAdministration.account.actions.connectionTest'), async () => {
            const extraConfig = Object.fromEntries(
                Object.entries(connectionForm.extraConfig)
                    .map(([key, value]) => [key, value.trim()])
                    .filter(([, value]) => !!value)
            )
            const result = await runCarrierConnectionTest(account.id, {
                apiKey: connectionForm.apiKey.trim(),
                apiSecret: toNullableText(connectionForm.apiSecret),
                accountId: toNullableText(connectionForm.accountId),
                webhookSecret: toNullableText(connectionForm.webhookSecret),
                apiUrl: null,
                useSandbox: carrierDefinition?.environmentSupported === false ? false : connectionForm.useSandbox,
                extraConfig: Object.keys(extraConfig).length ? extraConfig : null,
                saveOnly,
            })
            if (result?.succeeded !== true) {
                throw new Error([t('shippingAdministration.account.verificationFailed'), result?.failureCode, ...(result?.blockers || [])].filter(Boolean).join(' — '))
            }
        }, () => setConnectionForm(emptyConnectionForm))
    }

    const runLifecycleAction = (label, action) => {
        runAction(label, () => action(account.id))
    }

    const getCredentialFieldValue = (field) => {
        if (field.target === 'ApiKey') return connectionForm.apiKey
        if (field.target === 'ApiSecret') return connectionForm.apiSecret
        if (field.target === 'AccountId') return connectionForm.accountId
        if (field.target === 'WebhookSecret') return connectionForm.webhookSecret
        if (field.target?.startsWith('ExtraConfig.')) {
            const key = field.target.slice('ExtraConfig.'.length)
            return connectionForm.extraConfig[key] || ''
        }

        return ''
    }

    const setCredentialFieldValue = (field, value) => {
        if (field.target === 'ApiKey') {
            setConnectionForm({ ...connectionForm, apiKey: value })
            return
        }
        if (field.target === 'ApiSecret') {
            setConnectionForm({ ...connectionForm, apiSecret: value })
            return
        }
        if (field.target === 'AccountId') {
            setConnectionForm({ ...connectionForm, accountId: value })
            return
        }
        if (field.target === 'WebhookSecret') {
            setConnectionForm({ ...connectionForm, webhookSecret: value })
            return
        }
        if (field.target?.startsWith('ExtraConfig.')) {
            const key = field.target.slice('ExtraConfig.'.length)
            setConnectionForm({
                ...connectionForm,
                extraConfig: {
                    ...connectionForm.extraConfig,
                    [key]: value,
                },
            })
        }
    }

    const areRequiredCredentialFieldsPresent = () => (
        credentialFields.length === 0 ||
        credentialFields.every((field) => !field.required || getCredentialFieldValue(field).trim())
    )

    return (
        <article className="rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
            <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
                <div>
                    <h3 className="text-lg font-semibold text-gray-900">{displayName}</h3>
                    <p className="text-sm text-gray-500">{t('shippingAdministration.account.carrier')}: <bdi>{account.carrierCode}</bdi></p>
                </div>
                <StatusBadge value={merchantState.badge} label={t(`shippingAdministration.states.${merchantState.badge}`)} />
            </div>

            <div className="mt-5 rounded-lg border border-gray-100 bg-gray-50 p-4">
                <div className="text-sm font-semibold text-gray-900">{merchantState.title}</div>
                <p className="mt-1 text-sm text-gray-600">{merchantState.message}</p>
                <div className="mt-3 flex flex-wrap gap-2">
                    {merchantState.checks.map((check) => (
                        <span key={check} className="rounded-full bg-white px-3 py-1 text-xs font-medium text-gray-700 ring-1 ring-gray-200">
                            {t(`shippingAdministration.account.checks.${check}`)}
                        </span>
                    ))}
                </div>
            </div>

            {accountRequired && <section className="mt-4 rounded-lg border border-amber-100 bg-amber-50 p-3 text-sm"
                aria-label={i18n.language === 'ar' ? 'التفعيل وأهلية الحجز' : 'Activation and booking eligibility'}>
                <h4 className="font-semibold text-gray-900">{i18n.language === 'ar' ? 'التفعيل وأهلية الحجز' : 'Activation and booking eligibility'}</h4>
                <p className="mt-1 text-gray-700">{i18n.language === 'ar'
                    ? 'نجاح المصادقة لا يفعّل الحساب ولا يثبت جاهزية طلب للشحن. تظل بيانات الطرد والوجهة والمصدر خاضعة للتحقق.'
                    : 'Authentication does not activate this account or certify an order for booking. Parcel, destination and pickup evidence are checked separately.'}</p>
                {account.carrierCode === 'bosta' && (connectionOutcome?.status === 'Succeeded' || !connectionOutcome && account.connectionTestIsCurrent && account.lastConnectionTest?.status === 'Succeeded') && <p className="mt-2 font-medium">{t('shippingAdministration.native.connectedIncomplete')}</p>}
                <dl className="mt-2 grid grid-cols-2 gap-2">
                    <div><dt>{t('shippingAdministration.native.integrationSwitch')}</dt><dd>{readiness?.enabled === true ? t('common.yes') : t('common.no')}</dd></div>
                    <div><dt>{t('shippingAdministration.account.state', { defaultValue: 'Account state' })}</dt><dd>{t(`shippingAdministration.states.${account.state}`, { defaultValue: account.state || '-' })}</dd></div>
                    <div><dt>{t('shippingAdministration.account.runtimeExecutable')}</dt><dd>{readiness?.canExecute === true ? t('common.yes') : t('common.no')}</dd></div>
                    {account.carrierCode === 'bosta' && <div><dt>{t('shippingAdministration.native.providerActivation')}</dt><dd>{t('shippingAdministration.native.providerUnknown')}</dd></div>}
                </dl>
                {readiness?.blockers?.length > 0 && <ul className="mt-2 list-inside list-disc break-words text-xs text-amber-900">
                    {readiness.blockers.map(blocker => <li key={blocker}>{account.carrierCode === 'bosta' ? t(`shippingAdministration.native.errors.${blocker}`, { defaultValue: blocker }) : blocker}</li>)}
                </ul>}
            </section>}

            {account.lastConnectionTest?.failureReason && (!connectionOnly || account.connectionTestIsCurrent) && (
                <div className="mt-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                    {account.lastConnectionTest.failureReason}
                </div>
            )}

            <details className="mt-4 rounded-lg border border-gray-100 bg-white p-3 text-sm text-gray-600">
                <summary className="cursor-pointer font-medium text-gray-800">{t('shippingAdministration.account.advancedDiagnostics')}</summary>
                <div className="mt-3 grid grid-cols-1 gap-4 md:grid-cols-3">
                    <Detail label={t('shippingAdministration.account.environment')} value={account.carrierCode === 'mylerz'
                        ? t('shippingAdministration.connection.integrationEndpoint')
                        : t(`shippingAdministration.states.${readiness?.environment || account.environment || 'NotSelected'}`)} />
                    <Detail label={t('shippingAdministration.account.connectionEvidence')} value={t(`shippingAdministration.states.${connectionStatus.replaceAll(' ', '')}`)} />
                    <Detail label={t('shippingAdministration.account.runtimeCredentials')} value={t(`shippingAdministration.states.${readiness?.credentialAvailability || 'Unknown'}`)} />
                    <Detail label={t('shippingAdministration.account.runtimeExecutable')} value={readiness?.canExecute ? t('common.yes') : t('common.no')} />
                    <Detail label={t('shippingAdministration.account.capability')} value={t(`shippingAdministration.states.${carrierDefinition?.productionCapability || readiness?.productionCapability || 'Unknown'}`)} />
                    <Detail label={t('shippingAdministration.account.facadeHealth')} value={t(`shippingAdministration.states.${account.healthState}`)} />
                </div>
                {readiness?.blockers?.length > 0 && (
                    <div className="mt-3 text-xs text-gray-500">{readiness.blockers.join(', ')}</div>
                )}
                {account.operations?.length > 0 && <section className="mt-4" aria-label={t('shippingAdministration.operations.title')}>
                    <h4 className="font-medium text-gray-800">{t('shippingAdministration.operations.title')}</h4>
                    <p className="mt-1 text-xs">{t('shippingAdministration.operations.boundary')}</p>
                    <ul className="mt-2 space-y-2">
                        {account.operations.map(operation => <li key={operation.operation} className="rounded border border-gray-200 p-2">
                            <span className="font-medium">{t(`shippingAdministration.operations.names.${operation.operation}`)}</span>: {t(`shippingAdministration.operations.states.${operation.implementation}`)}
                            {operation.implementation === 'Implemented' && <span> — {t(operation.accountReady ? 'shippingAdministration.operations.accountReady' : 'shippingAdministration.operations.accountBlocked')}</span>}
                            {operation.blockers.length > 0 && <div className="mt-1 break-words text-xs text-gray-500">{operation.blockers.join(', ')}</div>}
                        </li>)}
                    </ul>
                </section>}
                {account.healthReason && (
                    <div className="mt-2 text-xs text-gray-500">{account.healthReason}</div>
                )}
            </details>

            {!accountRequired && !['Unavailable', 'NotCertified'].includes(carrierDefinition?.productionCapability) && (
                <div className="mt-5 rounded-lg border border-slate-200 bg-slate-50 p-4 text-sm text-slate-700">
                    {t('shippingAdministration.account.manualExplanation')}
                </div>
            )}

            <div className="mt-5">
                <h4 className="text-sm font-semibold text-gray-900">{t('shippingAdministration.account.enabledServices')}</h4>
                <div className="mt-2 flex flex-wrap gap-2">
                    {enabledServices.length === 0 ? (
                        <span className="text-sm text-gray-500">{t('shippingAdministration.account.noEnabledServices')}</span>
                    ) : enabledServices.map((level) => (
                        <span key={level.code} className="rounded-full bg-indigo-50 px-3 py-1 text-xs font-medium text-indigo-700">
                            {level.name}
                        </span>
                    ))}
                </div>
            </div>

            {accountRequired && connectionOnly && <CarrierConnectionForm key={`${account.id}-${account.carrierCode}`} account={account} definition={carrierDefinition} onMutated={onMutated} onBusy={setWorkingAction} onOutcome={setConnectionOutcome} />}

            {accountRequired && !connectionOnly && <div className="mt-5 rounded-lg border border-gray-100 bg-gray-50 p-4">
                <h4 className="text-sm font-semibold text-gray-900">{t('shippingAdministration.account.management')}</h4>

                <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-2">
                    <form onSubmit={submitEnvironment} className="space-y-2">
                        <label htmlFor={`carrier-environment-${account.id}`} className="text-xs font-medium uppercase tracking-wide text-gray-500">{t('shippingAdministration.account.environment')}</label>
                        <div className="flex gap-2">
                            <select id={`carrier-environment-${account.id}`} disabled={!!workingAction} value={environment} onChange={(event) => setEnvironment(event.target.value)} className="input">
                                <option value="">{t('shippingAdministration.account.selectEnvironment')}</option>
                                <option value="Sandbox">{t('shippingAdministration.states.Sandbox')}</option>
                                <option value="Production">{t('shippingAdministration.states.Production')}</option>
                            </select>
                            <button type="submit" disabled={!environment || !!workingAction} className="btn-secondary whitespace-nowrap">{t('common.save')}</button>
                        </div>
                    </form>

                    <form onSubmit={(event) => submitConnectionTest(event)} className="space-y-2">
                        <div className="text-xs font-medium uppercase tracking-wide text-gray-500">{t('shippingAdministration.account.saveAndValidate')}</div>
                        <p className="text-xs text-gray-500">{describeConnectionTest(carrierDefinition, t)}</p>
                        {!connectionTestSupported && (
                            <div className="rounded border border-slate-200 bg-slate-50 p-3 text-sm text-slate-700">
                                {carrierDefinition?.productionCapability || 'Unavailable'}
                            </div>
                        )}
                        <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
                            {credentialFields.map((field) => {
                                const inputId = `carrier-${account.id}-${field.key}`
                                const label = t(`shippingAdministration.credentials.${field.key}`, { defaultValue: field.label })
                                return (
                                    <label key={field.key} htmlFor={inputId} className="space-y-1 text-xs font-medium text-gray-600">
                                        <span>{label}{field.required ? ' *' : ''}</span>
                                        <input
                                            id={inputId}
                                            className="input"
                                            autoComplete="new-password"
                                            value={getCredentialFieldValue(field)}
                                            onChange={(event) => setCredentialFieldValue(field, event.target.value)}
                                            type={field.isSecret ? 'password' : 'text'}
                                            disabled={!!workingAction}
                                            required={field.required}
                                        />
                                    </label>
                                )
                            })}
                        </div>
                        {carrierDefinition?.environmentSupported !== false && <label className="flex items-center gap-2 text-sm text-gray-700">
                            <input type="checkbox" disabled={!!workingAction} checked={connectionForm.useSandbox} onChange={(event) => setConnectionForm({ ...connectionForm, useSandbox: event.target.checked })} />
                            {t('shippingAdministration.account.useSandbox')}
                        </label>}
                        <button type="button" onClick={(event) => submitConnectionTest(event, true)} disabled={!areRequiredCredentialFieldsPresent() || !!workingAction} className="btn-secondary">
                            {t('shippingAdministration.account.saveCredentialsOnly')}
                        </button>
                        <button type="submit" disabled={!connectionTestSupported || !areRequiredCredentialFieldsPresent() || !!workingAction} className="btn-secondary">
                            {carrierDefinition?.connectionTestMode === 'LocalOnly'
                                ? t('shippingAdministration.account.saveValidateLocally')
                                : t('shippingAdministration.account.saveAndValidate')}
                        </button>
                    </form>
                </div>
            </div>}

            {accountRequired && <div className="mt-5 border-t border-gray-100 pt-4">
                <h4 className="text-sm font-semibold text-gray-900">{t('shippingAdministration.account.availableActions')}</h4>
                <div className="mt-2 flex flex-wrap gap-2 text-xs text-gray-600">
                    <button type="button" disabled={!!workingAction} onClick={() => runLifecycleAction(t('shippingAdministration.account.actions.healthEvaluation'), evaluateCarrierHealth)} className="rounded border border-gray-200 px-2 py-1">{t('shippingAdministration.account.actions.evaluateHealth')}</button>
                    <button type="button" disabled={!!workingAction || !activationSupported} onClick={() => runLifecycleAction(t('shippingAdministration.account.actions.activation'), activateCarrierAccount)} className="rounded border border-gray-200 px-2 py-1">{t('shippingAdministration.account.actions.activate')}</button>
                    <button type="button" disabled={!!workingAction} onClick={() => runLifecycleAction(t('shippingAdministration.account.actions.suspension'), suspendCarrierAccount)} className="rounded border border-gray-200 px-2 py-1">{t('shippingAdministration.account.actions.suspend')}</button>
                    <button type="button" disabled={!!workingAction} onClick={() => runLifecycleAction(t('shippingAdministration.account.actions.disable'), disableCarrierAccount)} className="rounded border border-gray-200 px-2 py-1">{t('shippingAdministration.account.actions.disable')}</button>
                    <button type="button" disabled={!!workingAction} onClick={() => runLifecycleAction(t('shippingAdministration.account.actions.retire'), retireCarrierAccount)} className="rounded border border-gray-200 px-2 py-1">{t('shippingAdministration.account.actions.retire')}</button>
                </div>
                {workingAction && <p className="mt-2 text-sm text-gray-500" role="status">{t('shippingAdministration.account.actionInProgress', { action: workingAction })}</p>}
                {message && <p className="mt-2 text-sm text-gray-600" role="status">{message}</p>}
            </div>}
        </article>
    )
}

import { useState } from 'react'
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

function StatusBadge({ value }) {
    return (
        <span className={`inline-flex rounded-full border px-2 py-1 text-xs font-semibold ${statusClassByState[value] || 'bg-gray-100 text-gray-700 border-gray-200'}`}>
            {value || 'Unknown'}
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
    useSandbox: true,
    extraConfig: {},
}

function toNullableText(value) {
    const text = value.trim()
    return text ? text : null
}

function describeConnectionTest(carrierDefinition) {
    if (carrierDefinition?.connectionTestSupported === false) {
        return 'Connection testing is not available for this provider.'
    }

    if (carrierDefinition?.connectionTestMode === 'LocalOnly') {
        return 'Local credential validation only. No remote carrier API connectivity is verified.'
    }

    return 'Remote provider validation. Network or endpoint failures are reported separately from credential failures.'
}

function hasBlocker(readiness, code) {
    return readiness?.blockers?.some((blocker) => blocker === code) === true
}

function isProviderConnectivityFailure(code) {
    return ['CONNECTION_ERROR', 'NETWORK_ERROR', 'PROVIDER_UNAVAILABLE', 'CONNECTION_TEST_EXCEPTION']
        .includes((code || '').toUpperCase())
}

function getMerchantIntegrationState(account, carrierDefinition) {
    const readiness = account.integrationReadiness
    const test = account.lastConnectionTest

    if (carrierDefinition?.accountRequired === false) {
        return {
            badge: 'Ready',
            title: 'Manual fulfillment ready',
            message: 'No carrier credentials or connection test are required.',
            checks: ['No credentials required', 'No provider connection required', 'Ready for checkout'],
        }
    }

    if (carrierDefinition && ['Unavailable', 'NotCertified'].includes(carrierDefinition.productionCapability)) {
        return {
            badge: 'Action required',
            title: carrierDefinition.productionCapability === 'NotCertified' ? 'Production not certified' : 'Provider unavailable',
            message: 'This provider is not currently available for checkout activation.',
            checks: ['Credentials not required yet', 'Connection verification unavailable'],
        }
    }

    if (!readiness || readiness.credentialAvailability !== 'Available') {
        return {
            badge: 'Not configured',
            title: 'Not configured',
            message: 'Save carrier credentials before verifying the connection.',
            checks: ['Credentials not saved', 'Connection not verified'],
        }
    }

    if (test?.status === 'Failed') {
        const providerFailure = isProviderConnectivityFailure(test.failureCode)
        return {
            badge: providerFailure ? 'Provider unavailable' : 'Connection failed',
            title: providerFailure ? 'Provider could not be reached' : 'Connection verification failed',
            message: providerFailure
                ? 'Credentials were saved and were not marked invalid.'
                : 'Credentials were saved. Check them and retry the connection.',
            checks: ['Credentials saved', providerFailure ? 'Provider unreachable' : 'Connection not verified'],
        }
    }

    if (readiness.canExecute) {
        return {
            badge: 'Connected',
            title: 'Connected',
            message: 'Credentials saved, connection verified, and ready for checkout.',
            checks: ['Credentials saved', 'Connection verified', 'Ready for checkout'],
        }
    }

    if (hasBlocker(readiness, 'CARRIER_CONNECTION_NOT_VERIFIED')) {
        return {
            badge: 'Credentials saved',
            title: 'Credentials saved',
            message: 'Run validation to verify the provider connection.',
            checks: ['Credentials saved', 'Connection not verified'],
        }
    }

    if (hasBlocker(readiness, 'CARRIER_CONFIGURATION_DISABLED')) {
        return {
            badge: 'Disabled',
            title: 'Configuration disabled',
            message: 'Enable this carrier before it can be used at checkout.',
            checks: ['Credentials saved', 'Connection pending', 'Configuration disabled'],
        }
    }

    return {
        badge: 'Action required',
        title: 'Action required',
        message: 'Review diagnostics or retry validation.',
        checks: ['Credentials saved', test?.status === 'Succeeded' ? 'Connection verified' : 'Connection pending'],
    }
}

export default function CarrierAccountCard({ account, supportedCarriers, onMutated }) {
    const carrierDefinition = supportedCarriers.find((carrier) => carrier.carrierCode === account.carrierCode)
    const displayName = getCarrierDisplayName(supportedCarriers, account.carrierCode)
    const accountRequired = carrierDefinition?.accountRequired !== false
    const enabledServices = account.serviceLevels.filter((level) => level.isEnabled)
    const connectionStatus = account.lastConnectionTest?.status || 'Not Tested'
    const credentialFields = carrierDefinition?.credentialFields || []
    const connectionTestSupported = carrierDefinition?.connectionTestSupported !== false
    const readiness = account.integrationReadiness
    const merchantState = getMerchantIntegrationState(account, carrierDefinition)
    const [environment, setEnvironment] = useState(account.environment || '')
    const [connectionForm, setConnectionForm] = useState(emptyConnectionForm)
    const [workingAction, setWorkingAction] = useState('')
    const [message, setMessage] = useState('')

    const runAction = async (actionName, action) => {
        setWorkingAction(actionName)
        setMessage('')
        try {
            await action()
            await onMutated()
            setMessage(`${actionName} completed.`)
        } catch (error) {
            await onMutated().catch(() => {})
            setMessage(error.message || `${actionName} failed.`)
        } finally {
            setWorkingAction('')
        }
    }

    const submitEnvironment = (event) => {
        event.preventDefault()
        if (!environment) return
        runAction('Environment selection', () => selectCarrierEnvironment(account.id, environment))
    }

    const submitConnectionTest = (event) => {
        event.preventDefault()
        if (!connectionTestSupported || !areRequiredCredentialFieldsPresent()) return
        runAction('Connection test', async () => {
            const extraConfig = Object.fromEntries(
                Object.entries(connectionForm.extraConfig)
                    .map(([key, value]) => [key, value.trim()])
                    .filter(([, value]) => !!value)
            )
            await runCarrierConnectionTest(account.id, {
                apiKey: connectionForm.apiKey.trim(),
                apiSecret: toNullableText(connectionForm.apiSecret),
                accountId: toNullableText(connectionForm.accountId),
                apiUrl: null,
                useSandbox: connectionForm.useSandbox,
                extraConfig: Object.keys(extraConfig).length ? extraConfig : null,
            })
            setConnectionForm(emptyConnectionForm)
        })
    }

    const runLifecycleAction = (label, action) => {
        runAction(label, () => action(account.id))
    }

    const getCredentialFieldValue = (field) => {
        if (field.target === 'ApiKey') return connectionForm.apiKey
        if (field.target === 'ApiSecret') return connectionForm.apiSecret
        if (field.target === 'AccountId') return connectionForm.accountId
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
                    <p className="text-sm text-gray-500">Carrier: {account.carrierCode}</p>
                </div>
                <StatusBadge value={merchantState.badge} />
            </div>

            <div className="mt-5 rounded-lg border border-gray-100 bg-gray-50 p-4">
                <div className="text-sm font-semibold text-gray-900">{merchantState.title}</div>
                <p className="mt-1 text-sm text-gray-600">{merchantState.message}</p>
                <div className="mt-3 flex flex-wrap gap-2">
                    {merchantState.checks.map((check) => (
                        <span key={check} className="rounded-full bg-white px-3 py-1 text-xs font-medium text-gray-700 ring-1 ring-gray-200">
                            {check}
                        </span>
                    ))}
                </div>
            </div>

            {account.lastConnectionTest?.failureReason && (
                <div className="mt-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                    {account.lastConnectionTest.failureReason}
                </div>
            )}

            <details className="mt-4 rounded-lg border border-gray-100 bg-white p-3 text-sm text-gray-600">
                <summary className="cursor-pointer font-medium text-gray-800">Advanced diagnostics</summary>
                <div className="mt-3 grid grid-cols-1 gap-4 md:grid-cols-3">
                    <Detail label="Environment" value={readiness?.environment || account.environment || 'Not Selected'} />
                    <Detail label="Connection evidence" value={connectionStatus} />
                    <Detail label="Runtime credentials" value={readiness?.credentialAvailability || 'Unknown'} />
                    <Detail label="Runtime executable" value={readiness?.canExecute ? 'Yes' : 'No'} />
                    <Detail label="Capability" value={carrierDefinition?.productionCapability || readiness?.productionCapability || 'Unknown'} />
                    <Detail label="Facade health" value={account.healthState} />
                </div>
                {readiness?.blockers?.length > 0 && (
                    <div className="mt-3 text-xs text-gray-500">{readiness.blockers.join(', ')}</div>
                )}
                {account.healthReason && (
                    <div className="mt-2 text-xs text-gray-500">{account.healthReason}</div>
                )}
            </details>

            {!accountRequired && (
                <div className="mt-5 rounded-lg border border-slate-200 bg-slate-50 p-4 text-sm text-slate-700">
                    Manual Fulfillment is managed as a Shipping Method and does not require credentials,
                    connection tests, warehouse assignments, or external readiness actions.
                </div>
            )}

            <div className="mt-5">
                <h4 className="text-sm font-semibold text-gray-900">Enabled Services</h4>
                <div className="mt-2 flex flex-wrap gap-2">
                    {enabledServices.length === 0 ? (
                        <span className="text-sm text-gray-500">No enabled service levels reported.</span>
                    ) : enabledServices.map((level) => (
                        <span key={level.code} className="rounded-full bg-indigo-50 px-3 py-1 text-xs font-medium text-indigo-700">
                            {level.name}
                        </span>
                    ))}
                </div>
            </div>

            {accountRequired && <div className="mt-5 rounded-lg border border-gray-100 bg-gray-50 p-4">
                <h4 className="text-sm font-semibold text-gray-900">Account Management</h4>

                <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-2">
                    <form onSubmit={submitEnvironment} className="space-y-2">
                        <label className="text-xs font-medium uppercase tracking-wide text-gray-500">Environment</label>
                        <div className="flex gap-2">
                            <select value={environment} onChange={(event) => setEnvironment(event.target.value)} className="input">
                                <option value="">Select environment</option>
                                <option value="Sandbox">Sandbox</option>
                                <option value="Production">Production</option>
                            </select>
                            <button type="submit" disabled={!environment || !!workingAction} className="btn-secondary whitespace-nowrap">Save</button>
                        </div>
                    </form>

                    <form onSubmit={submitConnectionTest} className="space-y-2">
                        <label className="text-xs font-medium uppercase tracking-wide text-gray-500">Save & Validate</label>
                        <p className="text-xs text-gray-500">{describeConnectionTest(carrierDefinition)}</p>
                        {!connectionTestSupported && (
                            <div className="rounded border border-slate-200 bg-slate-50 p-3 text-sm text-slate-700">
                                {carrierDefinition?.productionCapability || 'Unavailable'}
                            </div>
                        )}
                        <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
                            {credentialFields.map((field) => (
                                <input
                                    key={field.key}
                                    className="input"
                                    value={getCredentialFieldValue(field)}
                                    onChange={(event) => setCredentialFieldValue(field, event.target.value)}
                                    placeholder={field.required ? `${field.label} *` : field.label}
                                    type={field.isSecret ? 'password' : 'text'}
                                    disabled={!connectionTestSupported}
                                />
                            ))}
                        </div>
                        {carrierDefinition?.environmentSupported !== false && <label className="flex items-center gap-2 text-sm text-gray-700">
                            <input type="checkbox" checked={connectionForm.useSandbox} onChange={(event) => setConnectionForm({ ...connectionForm, useSandbox: event.target.checked })} />
                            Use sandbox
                        </label>}
                        <button type="submit" disabled={!connectionTestSupported || !areRequiredCredentialFieldsPresent() || !!workingAction} className="btn-secondary">
                            {carrierDefinition?.connectionTestMode === 'LocalOnly' ? 'Save & validate locally' : 'Save & validate'}
                        </button>
                    </form>
                </div>
            </div>}

            {accountRequired && <div className="mt-5 border-t border-gray-100 pt-4">
                <h4 className="text-sm font-semibold text-gray-900">Available Actions</h4>
                <div className="mt-2 flex flex-wrap gap-2 text-xs text-gray-600">
                    <button type="button" disabled={!!workingAction} onClick={() => runLifecycleAction('Health evaluation', evaluateCarrierHealth)} className="rounded border border-gray-200 px-2 py-1">Evaluate health</button>
                    <button type="button" disabled={!!workingAction} onClick={() => runLifecycleAction('Activation', activateCarrierAccount)} className="rounded border border-gray-200 px-2 py-1">Activate</button>
                    <button type="button" disabled={!!workingAction} onClick={() => runLifecycleAction('Suspension', suspendCarrierAccount)} className="rounded border border-gray-200 px-2 py-1">Suspend</button>
                    <button type="button" disabled={!!workingAction} onClick={() => runLifecycleAction('Disable', disableCarrierAccount)} className="rounded border border-gray-200 px-2 py-1">Disable</button>
                    <button type="button" disabled={!!workingAction} onClick={() => runLifecycleAction('Retire', retireCarrierAccount)} className="rounded border border-gray-200 px-2 py-1">Retire</button>
                </div>
                {workingAction && <p className="mt-2 text-sm text-gray-500">{workingAction} in progress...</p>}
                {message && <p className="mt-2 text-sm text-gray-600">{message}</p>}
            </div>}
        </article>
    )
}

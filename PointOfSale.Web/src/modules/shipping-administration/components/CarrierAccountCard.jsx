import { useState } from 'react'
import {
    activateCarrierAccount,
    assignCarrierWarehouse,
    disableCarrierAccount,
    evaluateCarrierHealth,
    getCarrierDisplayName,
    retireCarrierAccount,
    runCarrierConnectionTest,
    selectCarrierEnvironment,
    setCarrierPolicy,
    setCarrierServiceLevel,
    suspendCarrierAccount,
    updateCredentialReference,
} from '../shippingAdministrationApi'

const statusClassByState = {
    Healthy: 'bg-green-100 text-green-800 border-green-200',
    Warning: 'bg-amber-100 text-amber-800 border-amber-200',
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

const emptyCredentialForm = {
    secretReference: '',
    maskedFingerprint: '',
    expiresAtUtc: '',
}

const emptyWarehouseForm = {
    warehouseId: '',
    originReadinessPublished: false,
    readinessEvidenceReference: '',
}

const emptyConnectionForm = {
    apiKey: '',
    apiSecret: '',
    accountId: '',
    apiUrl: '',
    useSandbox: true,
}

function toNullableText(value) {
    const text = value.trim()
    return text ? text : null
}

export default function CarrierAccountCard({ account, supportedCarriers, onMutated }) {
    const displayName = getCarrierDisplayName(supportedCarriers, account.carrierCode)
    const activeCredential = account.credentialReferences.find((credential) => credential.id === account.activeCredentialReferenceId)
        || account.credentialReferences[0]
    const activeWarehouse = account.warehouseAssignments.find((assignment) => assignment.isEnabled)
    const enabledServices = account.serviceLevels.filter((level) => level.isEnabled)
    const connectionStatus = account.lastConnectionTest?.status || 'Not Tested'
    const [environment, setEnvironment] = useState(account.environment || '')
    const [credentialForm, setCredentialForm] = useState(emptyCredentialForm)
    const [warehouseForm, setWarehouseForm] = useState(emptyWarehouseForm)
    const [connectionForm, setConnectionForm] = useState(emptyConnectionForm)
    const [policyScope, setPolicyScope] = useState('Domestic')
    const [policyMode, setPolicyMode] = useState('Enabled')
    const [customsReadinessPublished, setCustomsReadinessPublished] = useState(false)
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

    const submitCredential = (event) => {
        event.preventDefault()
        if (!credentialForm.secretReference.trim()) return
        runAction('Credential update', async () => {
            await updateCredentialReference(account.id, {
                secretReference: credentialForm.secretReference.trim(),
                maskedFingerprint: toNullableText(credentialForm.maskedFingerprint),
                expiresAtUtc: toNullableText(credentialForm.expiresAtUtc),
            })
            setCredentialForm(emptyCredentialForm)
        })
    }

    const submitWarehouse = (event) => {
        event.preventDefault()
        if (!warehouseForm.warehouseId.trim()) return
        runAction('Warehouse assignment', async () => {
            await assignCarrierWarehouse(account.id, warehouseForm.warehouseId.trim(), {
                originReadinessPublished: warehouseForm.originReadinessPublished,
                readinessEvidenceReference: toNullableText(warehouseForm.readinessEvidenceReference),
            })
            setWarehouseForm(emptyWarehouseForm)
        })
    }

    const submitConnectionTest = (event) => {
        event.preventDefault()
        if (!connectionForm.apiKey.trim()) return
        runAction('Connection test', async () => {
            await runCarrierConnectionTest(account.id, {
                apiKey: connectionForm.apiKey.trim(),
                apiSecret: toNullableText(connectionForm.apiSecret),
                accountId: toNullableText(connectionForm.accountId),
                apiUrl: toNullableText(connectionForm.apiUrl),
                useSandbox: connectionForm.useSandbox,
                extraConfig: null,
            })
            setConnectionForm(emptyConnectionForm)
        })
    }

    const submitPolicy = (event) => {
        event.preventDefault()
        runAction('Policy update', () => setCarrierPolicy(account.id, policyScope, {
            mode: policyMode,
            customsReadinessPublished,
            readinessEvidenceReference: null,
        }))
    }

    const toggleServiceLevel = (level) => {
        runAction('Service level update', () => setCarrierServiceLevel(account.id, level.code, !level.isEnabled))
    }

    const runLifecycleAction = (label, action) => {
        runAction(label, () => action(account.id))
    }

    return (
        <article className="rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
            <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
                <div>
                    <h3 className="text-lg font-semibold text-gray-900">{displayName}</h3>
                    <p className="text-sm text-gray-500">Carrier: {account.carrierCode}</p>
                </div>
                <StatusBadge value={account.healthState} />
            </div>

            <div className="mt-5 grid grid-cols-1 gap-4 md:grid-cols-3">
                <Detail label="Environment" value={account.environment || 'Not Selected'} />
                <Detail label="Activation State" value={account.state} />
                <Detail label="Operational Health" value={account.operationalState} />
                <Detail label="Credential Status" value={activeCredential?.state || 'Missing'} />
                <Detail label="Connection Test Status" value={connectionStatus} />
                <Detail label="Assigned Warehouse" value={activeWarehouse?.warehouseId || 'Warehouse Missing'} />
            </div>

            {account.healthReason && (
                <div className="mt-4 rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
                    {account.healthReason}
                </div>
            )}

            {account.lastConnectionTest?.failureReason && (
                <div className="mt-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                    {account.lastConnectionTest.failureReason}
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

            <div className="mt-5 rounded-lg border border-gray-100 bg-gray-50 p-4">
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

                    <form onSubmit={submitCredential} className="space-y-2">
                        <label className="text-xs font-medium uppercase tracking-wide text-gray-500">Credential Reference</label>
                        <input className="input" value={credentialForm.secretReference} onChange={(event) => setCredentialForm({ ...credentialForm, secretReference: event.target.value })} placeholder="Secret reference" />
                        <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
                            <input className="input" value={credentialForm.maskedFingerprint} onChange={(event) => setCredentialForm({ ...credentialForm, maskedFingerprint: event.target.value })} placeholder="Masked fingerprint" />
                            <input className="input" value={credentialForm.expiresAtUtc} onChange={(event) => setCredentialForm({ ...credentialForm, expiresAtUtc: event.target.value })} placeholder="Expires at UTC" />
                        </div>
                        <button type="submit" disabled={!credentialForm.secretReference.trim() || !!workingAction} className="btn-secondary">Update credential reference</button>
                    </form>

                    <form onSubmit={submitWarehouse} className="space-y-2">
                        <label className="text-xs font-medium uppercase tracking-wide text-gray-500">Warehouse Assignment</label>
                        <input className="input" value={warehouseForm.warehouseId} onChange={(event) => setWarehouseForm({ ...warehouseForm, warehouseId: event.target.value })} placeholder="Warehouse ID" />
                        <label className="flex items-center gap-2 text-sm text-gray-700">
                            <input type="checkbox" checked={warehouseForm.originReadinessPublished} onChange={(event) => setWarehouseForm({ ...warehouseForm, originReadinessPublished: event.target.checked })} />
                            Origin readiness published
                        </label>
                        <input className="input" value={warehouseForm.readinessEvidenceReference} onChange={(event) => setWarehouseForm({ ...warehouseForm, readinessEvidenceReference: event.target.value })} placeholder="Readiness evidence reference" />
                        <button type="submit" disabled={!warehouseForm.warehouseId.trim() || !!workingAction} className="btn-secondary">Assign warehouse</button>
                    </form>

                    <form onSubmit={submitConnectionTest} className="space-y-2">
                        <label className="text-xs font-medium uppercase tracking-wide text-gray-500">Connection Test</label>
                        <input className="input" value={connectionForm.apiKey} onChange={(event) => setConnectionForm({ ...connectionForm, apiKey: event.target.value })} placeholder="API key or token" />
                        <div className="grid grid-cols-1 gap-2 md:grid-cols-3">
                            <input className="input" value={connectionForm.apiSecret} onChange={(event) => setConnectionForm({ ...connectionForm, apiSecret: event.target.value })} placeholder="API secret" type="password" />
                            <input className="input" value={connectionForm.accountId} onChange={(event) => setConnectionForm({ ...connectionForm, accountId: event.target.value })} placeholder="Account ID" />
                            <input className="input" value={connectionForm.apiUrl} onChange={(event) => setConnectionForm({ ...connectionForm, apiUrl: event.target.value })} placeholder="API URL" />
                        </div>
                        <label className="flex items-center gap-2 text-sm text-gray-700">
                            <input type="checkbox" checked={connectionForm.useSandbox} onChange={(event) => setConnectionForm({ ...connectionForm, useSandbox: event.target.checked })} />
                            Use sandbox
                        </label>
                        <button type="submit" disabled={!connectionForm.apiKey.trim() || !!workingAction} className="btn-secondary">Run connection test</button>
                    </form>
                </div>

                <form onSubmit={submitPolicy} className="mt-4 flex flex-col gap-2 md:flex-row md:items-end">
                    <div>
                        <label className="text-xs font-medium uppercase tracking-wide text-gray-500">Policy Scope</label>
                        <select value={policyScope} onChange={(event) => setPolicyScope(event.target.value)} className="input">
                            <option value="Domestic">Domestic</option>
                            <option value="International">International</option>
                        </select>
                    </div>
                    <div>
                        <label className="text-xs font-medium uppercase tracking-wide text-gray-500">Policy Mode</label>
                        <select value={policyMode} onChange={(event) => setPolicyMode(event.target.value)} className="input">
                            <option value="Enabled">Enabled</option>
                            <option value="Disabled">Disabled</option>
                        </select>
                    </div>
                    <label className="flex items-center gap-2 text-sm text-gray-700 md:pb-2">
                        <input type="checkbox" checked={customsReadinessPublished} onChange={(event) => setCustomsReadinessPublished(event.target.checked)} />
                        Customs readiness published
                    </label>
                    <button type="submit" disabled={!!workingAction} className="btn-secondary md:w-auto">Save policy</button>
                </form>

                <div className="mt-4">
                    <h5 className="text-xs font-medium uppercase tracking-wide text-gray-500">Service Levels</h5>
                    <div className="mt-2 flex flex-wrap gap-2">
                        {account.serviceLevels.length === 0 ? (
                            <span className="text-sm text-gray-500">No service levels reported.</span>
                        ) : account.serviceLevels.map((level) => (
                            <button key={level.code} type="button" disabled={!!workingAction} onClick={() => toggleServiceLevel(level)} className="rounded border border-gray-200 bg-white px-3 py-1 text-xs font-medium text-gray-700">
                                {level.name}: {level.isEnabled ? 'Enabled' : 'Disabled'}
                            </button>
                        ))}
                    </div>
                </div>
            </div>

            <div className="mt-5 border-t border-gray-100 pt-4">
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
            </div>
        </article>
    )
}

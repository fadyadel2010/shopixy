import { useMemo, useState } from 'react'
import CarrierAccountCard from './components/CarrierAccountCard'
import ShippingMethodsTab from './components/ShippingMethodsTab'
import { useShippingAdministrationApi } from './hooks/useShippingAdministrationApi'
import { createCarrierAccount, createShippingAdministrationSummary, getCarrierDisplayName } from './shippingAdministrationApi'

const TABS = [
    { key: 'overview', label: 'Overview' },
    { key: 'carrier-accounts', label: 'Carrier Accounts' },
    { key: 'shipping-methods', label: 'Shipping Methods' },
    { key: 'health', label: 'Health' },
    { key: 'audit', label: 'Audit' },
]

const ERROR_STATES = [
    'Credential Missing',
    'Connection Failed',
    'Carrier Mapping Missing',
    'Activation Blocked',
]

function formatDate(value) {
    if (!value) return '-'
    return new Date(value).toLocaleString()
}

function EmptyState({ title, description }) {
    return (
        <div className="rounded-lg border border-dashed border-gray-300 bg-white p-10 text-center">
            <h3 className="text-base font-semibold text-gray-900">{title}</h3>
            <p className="mt-2 text-sm text-gray-500">{description}</p>
        </div>
    )
}

function SummaryCard({ label, value }) {
    return (
        <div className="rounded-lg border border-gray-200 bg-white p-4">
            <div className="text-sm font-medium text-gray-500">{label}</div>
            <div className="mt-2 text-2xl font-semibold text-gray-900">{value}</div>
        </div>
    )
}

function OverviewTab({ accounts, audit }) {
    const summary = createShippingAdministrationSummary(accounts)

    return (
        <div className="space-y-6">
            <div className="grid grid-cols-1 gap-4 md:grid-cols-5">
                <SummaryCard label="Configured Carriers" value={summary.configuredCarriers} />
                <SummaryCard label="Active Carriers" value={summary.activeCarriers} />
                <SummaryCard label="Blocked Carriers" value={summary.blockedCarriers} />
                <SummaryCard label="Connection Warnings" value={summary.connectionWarnings} />
                <SummaryCard label="Activation Blockers" value={summary.activationBlockers} />
            </div>

            <section className="rounded-lg border border-gray-200 bg-white p-5">
                <h3 className="font-semibold text-gray-900">Recommended Next Action</h3>
                <p className="mt-2 text-sm text-gray-600">{summary.recommendedNextAction}</p>
            </section>

            <section className="rounded-lg border border-gray-200 bg-white p-5">
                <h3 className="font-semibold text-gray-900">Recent Administration Activity</h3>
                {audit.length === 0 ? (
                    <p className="mt-2 text-sm text-gray-500">No administration activity recorded yet.</p>
                ) : (
                    <div className="mt-4 divide-y divide-gray-100">
                        {audit.slice(0, 5).map((entry) => (
                            <div key={entry.id} className="py-3 text-sm">
                                <div className="font-medium text-gray-900">{entry.action}</div>
                                <div className="text-gray-500">{formatDate(entry.occurredAtUtc)} - {entry.reason || 'No backend reason reported.'}</div>
                            </div>
                        ))}
                    </div>
                )}
            </section>
        </div>
    )
}

function CarrierAccountsTab({ accounts, supportedCarriers, onCreated }) {
    const [carrierCode, setCarrierCode] = useState('')
    const [creating, setCreating] = useState(false)
    const [message, setMessage] = useState('')
    const accountCarriers = supportedCarriers.filter((carrier) => carrier.accountRequired !== false)
    const visibleAccounts = accounts.filter((account) => {
        const definition = supportedCarriers.find((carrier) => carrier.carrierCode === account.carrierCode)
        return definition?.accountRequired !== false
    })

    const createAccount = async (event) => {
        event.preventDefault()
        if (!carrierCode) return

        setCreating(true)
        setMessage('')
        try {
            await createCarrierAccount(carrierCode)
            setCarrierCode('')
            await onCreated()
        } catch (error) {
            setMessage(error.message || 'Failed to create carrier account.')
        } finally {
            setCreating(false)
        }
    }

    return (
        <div className="space-y-6">
            <form onSubmit={createAccount} className="rounded-lg border border-gray-200 bg-white p-5">
                <h3 className="font-semibold text-gray-900">Add Carrier Account</h3>
                <div className="mt-4 flex flex-col gap-3 md:flex-row">
                    <select
                        value={carrierCode}
                        onChange={(event) => setCarrierCode(event.target.value)}
                        className="input md:max-w-sm"
                    >
                        <option value="">Select backend-supported carrier</option>
                        {accountCarriers.map((carrier) => (
                            <option key={carrier.carrierCode} value={carrier.carrierCode}>
                                {carrier.displayName}
                            </option>
                        ))}
                    </select>
                    <button type="submit" disabled={!carrierCode || creating} className="btn-primary md:w-auto">
                        {creating ? 'Creating...' : 'Add carrier account'}
                    </button>
                </div>
                {message && <p className="mt-3 text-sm text-red-700">{message}</p>}
            </form>

            {visibleAccounts.length === 0 ? (
                <EmptyState
                    title="No carrier accounts configured"
                    description="External carrier accounts are created from certified provider capabilities. Manual Fulfillment is configured as a Shipping Method."
                />
            ) : (
                <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
                    {visibleAccounts.map((account) => (
                        <CarrierAccountCard key={account.id} account={account} supportedCarriers={supportedCarriers} onMutated={onCreated} />
                    ))}
                </div>
            )}
        </div>
    )
}

function HealthTab({ accounts, supportedCarriers }) {
    const visibleAccounts = accounts.filter((account) => account.carrierCode !== 'manual')

    if (visibleAccounts.length === 0) {
        return <EmptyState title="No carrier readiness" description="External carrier readiness will appear here after a carrier integration is configured." />
    }

    return (
        <div className="space-y-4">
            <div className="rounded-lg border border-gray-200 bg-white p-5">
                <h3 className="font-semibold text-gray-900">Provider Readiness States</h3>
                <div className="mt-3 flex flex-wrap gap-2">
                    {['Ready', 'Blocked', 'NotRequired', 'Missing', 'Unavailable'].map((state) => (
                        <span key={state} className="rounded-full border border-gray-200 px-3 py-1 text-sm text-gray-700">{state}</span>
                    ))}
                </div>
                <h3 className="mt-5 font-semibold text-gray-900">Recognized Provider Outcomes</h3>
                <div className="mt-3 flex flex-wrap gap-2">
                    {ERROR_STATES.map((state) => (
                        <span key={state} className="rounded-full border border-gray-200 px-3 py-1 text-sm text-gray-700">{state}</span>
                    ))}
                </div>
            </div>

            {visibleAccounts.map((account) => (
                <div key={account.id} className="rounded-lg border border-gray-200 bg-white p-5">
                    <div className="flex flex-col gap-2 md:flex-row md:items-center md:justify-between">
                        <h3 className="font-semibold text-gray-900">{getCarrierDisplayName(supportedCarriers, account.carrierCode)}</h3>
                        <span className="text-sm font-medium text-gray-700">
                            {account.integrationReadiness?.canExecute ? 'Ready' : 'Blocked'}
                        </span>
                    </div>
                    <p className="mt-2 text-sm text-gray-600">
                        Credential availability: {account.integrationReadiness?.credentialAvailability || 'Unknown'}
                    </p>
                    <p className="mt-1 text-sm text-gray-500">
                        Production capability: {account.integrationReadiness?.productionCapability || 'Unknown'}
                    </p>
                    {account.integrationReadiness?.blockers?.length > 0 && (
                        <p className="mt-2 text-sm text-amber-700">{account.integrationReadiness.blockers.join(', ')}</p>
                    )}
                    {account.lastConnectionTest?.failureReason && (
                        <p className="mt-2 text-sm text-red-700">{account.lastConnectionTest.failureReason}</p>
                    )}
                </div>
            ))}
        </div>
    )
}

function AuditTab({ audit, supportedCarriers, accounts }) {
    if (audit.length === 0) {
        return <EmptyState title="No administration activity recorded yet" description="Shipping Administration audit is append-only and read-only." />
    }

    const accountById = new Map(accounts.map((account) => [account.id, account]))

    return (
        <div className="overflow-x-auto rounded-lg border border-gray-200 bg-white">
            <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50">
                    <tr>
                        <th className="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-500">Time</th>
                        <th className="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-500">Carrier Account</th>
                        <th className="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-500">Action</th>
                        <th className="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-500">Actor</th>
                        <th className="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-500">State</th>
                        <th className="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-500">Reason</th>
                        <th className="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-500">Correlation</th>
                    </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                    {audit.map((entry) => {
                        const account = accountById.get(entry.carrierAccountId)
                        return (
                            <tr key={entry.id}>
                                <td className="px-4 py-3 text-sm text-gray-700">{formatDate(entry.occurredAtUtc)}</td>
                                <td className="px-4 py-3 text-sm text-gray-900">
                                    {account ? getCarrierDisplayName(supportedCarriers, account.carrierCode) : entry.carrierAccountId}
                                </td>
                                <td className="px-4 py-3 text-sm text-gray-700">{entry.action}</td>
                                <td className="px-4 py-3 text-sm text-gray-700">{entry.actorId}</td>
                                <td className="px-4 py-3 text-sm text-gray-700">{`${entry.previousState || '-'} -> ${entry.nextState || '-'}`}</td>
                                <td className="px-4 py-3 text-sm text-gray-700">{entry.reason || '-'}</td>
                                <td className="px-4 py-3 text-sm text-gray-700">{entry.correlationId || '-'}</td>
                            </tr>
                        )
                    })}
                </tbody>
            </table>
        </div>
    )
}

export default function ShippingAdministrationWorkspace({ initialTab = 'overview' }) {
    const [activeTab, setActiveTab] = useState(initialTab)
    const {
        supportedCarriers,
        carrierAccounts,
        warehouses,
        audit,
        loading,
        error,
        refresh,
    } = useShippingAdministrationApi()
    const summary = useMemo(() => createShippingAdministrationSummary(carrierAccounts), [carrierAccounts])

    return (
        <div className="max-w-7xl mx-auto p-6">
            <div className="mb-6 flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">Shipping Administration</h1>
                    <p className="mt-1 text-sm text-gray-600">
                        Carrier readiness, storefront methods, policies, warehouses, health, and audit.
                    </p>
                </div>
                <button onClick={refresh} disabled={loading} className="btn-secondary md:w-auto">
                    {loading ? 'Refreshing...' : 'Run readiness check'}
                </button>
            </div>

            {error && (
                <div className="mb-6 rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">
                    {error}
                </div>
            )}

            <div className="mb-6 grid grid-cols-2 gap-3 md:grid-cols-4">
                <SummaryCard label="Active carrier accounts" value={summary.activeCarriers} />
                <SummaryCard label="Blocked carrier accounts" value={summary.blockedCarriers} />
                <SummaryCard label="Supported carriers" value={supportedCarriers.length} />
                <SummaryCard label="Audit events" value={audit.length} />
            </div>

            <div className="mb-6 overflow-x-auto border-b border-gray-200">
                <div className="flex min-w-max gap-2">
                    {TABS.map((tab) => (
                        <button
                            key={tab.key}
                            type="button"
                            onClick={() => setActiveTab(tab.key)}
                            className={`border-b-2 px-4 py-3 text-sm font-medium ${activeTab === tab.key
                                ? 'border-indigo-600 text-indigo-700'
                                : 'border-transparent text-gray-500 hover:text-gray-700'
                                }`}
                        >
                            {tab.label}
                        </button>
                    ))}
                </div>
            </div>

            {loading && carrierAccounts.length === 0 ? (
                <div className="flex justify-center py-16">
                    <div className="h-8 w-8 animate-spin rounded-full border-4 border-indigo-600 border-t-transparent"></div>
                </div>
            ) : (
                <>
                    {activeTab === 'overview' && <OverviewTab accounts={carrierAccounts} audit={audit} />}
                    {activeTab === 'carrier-accounts' && (
                        <CarrierAccountsTab accounts={carrierAccounts} supportedCarriers={supportedCarriers} onCreated={refresh} />
                    )}
                    {activeTab === 'shipping-methods' && <ShippingMethodsTab warehouses={warehouses} />}
                    {activeTab === 'health' && <HealthTab accounts={carrierAccounts} supportedCarriers={supportedCarriers} />}
                    {activeTab === 'audit' && <AuditTab audit={audit} supportedCarriers={supportedCarriers} accounts={carrierAccounts} />}
                </>
            )}
        </div>
    )
}

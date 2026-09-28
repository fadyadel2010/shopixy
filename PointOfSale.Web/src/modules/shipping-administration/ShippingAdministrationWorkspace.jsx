import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import CarrierAccountCard from './components/CarrierAccountCard'
import ShippingMethodsTab from './components/ShippingMethodsTab'
import { useShippingAdministrationApi } from './hooks/useShippingAdministrationApi'
import { createCarrierAccount, createShippingAdministrationSummary, getCarrierDisplayName } from './shippingAdministrationApi'

const TABS = [
    { key: 'overview', labelKey: 'overview' },
    { key: 'carrier-accounts', labelKey: 'carrierAccounts' },
    { key: 'shipping-methods', labelKey: 'shippingMethods' },
    { key: 'health', labelKey: 'health' },
    { key: 'audit', labelKey: 'audit' },
]

function formatDate(value, language) {
    if (!value) return '-'
    return new Intl.DateTimeFormat(language === 'ar' ? 'ar-EG' : 'en', {
        dateStyle: 'medium',
        timeStyle: 'short',
    }).format(new Date(value))
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
    const { t, i18n } = useTranslation()
    const summary = createShippingAdministrationSummary(accounts)

    return (
        <div className="space-y-6">
            <div className="grid grid-cols-1 gap-4 md:grid-cols-5">
                <SummaryCard label={t('shippingAdministration.summary.configured')} value={summary.configuredCarriers} />
                <SummaryCard label={t('shippingAdministration.summary.active')} value={summary.activeCarriers} />
                <SummaryCard label={t('shippingAdministration.summary.blocked')} value={summary.blockedCarriers} />
                <SummaryCard label={t('shippingAdministration.summary.connectionWarnings')} value={summary.connectionWarnings} />
                <SummaryCard label={t('shippingAdministration.summary.activationBlockers')} value={summary.activationBlockers} />
            </div>

            <section className="rounded-lg border border-gray-200 bg-white p-5">
                <h3 className="font-semibold text-gray-900">{t('shippingAdministration.overview.recommendedAction')}</h3>
                <p className="mt-2 text-sm text-gray-600">
                    {t(`shippingAdministration.overview.actions.${summary.recommendedNextActionKey}`)}
                </p>
            </section>

            <section className="rounded-lg border border-gray-200 bg-white p-5">
                <h3 className="font-semibold text-gray-900">{t('shippingAdministration.overview.recentActivity')}</h3>
                {audit.length === 0 ? (
                    <p className="mt-2 text-sm text-gray-500">{t('shippingAdministration.audit.emptyTitle')}</p>
                ) : (
                    <div className="mt-4 divide-y divide-gray-100">
                        {audit.slice(0, 5).map((entry) => (
                            <div key={entry.id} className="py-3 text-sm">
                                <div className="font-medium text-gray-900">{entry.action}</div>
                                <div className="text-gray-500">
                                    {formatDate(entry.occurredAtUtc, i18n.language)} - {entry.reason || t('shippingAdministration.audit.noReason')}
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </section>
        </div>
    )
}

function CarrierAccountsTab({ accounts, supportedCarriers, onCreated }) {
    const { t } = useTranslation()
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
            setMessage(error.message || t('shippingAdministration.accounts.createFailed'))
        } finally {
            setCreating(false)
        }
    }

    return (
        <div className="space-y-6">
            <form onSubmit={createAccount} className="rounded-lg border border-gray-200 bg-white p-5">
                <h3 className="font-semibold text-gray-900">{t('shippingAdministration.accounts.add')}</h3>
                <div className="mt-4 flex flex-col gap-3 md:flex-row">
                    <select
                        value={carrierCode}
                        onChange={(event) => setCarrierCode(event.target.value)}
                        className="input md:max-w-sm"
                    >
                        <option value="">{t('shippingAdministration.accounts.selectSupported')}</option>
                        {accountCarriers.map((carrier) => (
                            <option key={carrier.carrierCode} value={carrier.carrierCode}>
                                {carrier.displayName}
                            </option>
                        ))}
                    </select>
                    <button type="submit" disabled={!carrierCode || creating} className="btn-primary md:w-auto">
                        {creating ? t('shippingAdministration.accounts.creating') : t('shippingAdministration.accounts.addAction')}
                    </button>
                </div>
                {message && <p className="mt-3 text-sm text-red-700">{message}</p>}
            </form>

            {visibleAccounts.length === 0 ? (
                <EmptyState
                    title={t('shippingAdministration.accounts.emptyTitle')}
                    description={t('shippingAdministration.accounts.emptyDescription')}
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
    const { t } = useTranslation()
    const visibleAccounts = accounts.filter((account) => account.carrierCode !== 'manual')

    if (visibleAccounts.length === 0) {
        return <EmptyState title={t('shippingAdministration.health.emptyTitle')} description={t('shippingAdministration.health.emptyDescription')} />
    }

    return (
        <div className="space-y-4">
            <div className="rounded-lg border border-gray-200 bg-white p-5">
                <h3 className="font-semibold text-gray-900">{t('shippingAdministration.health.readinessStates')}</h3>
                <div className="mt-3 flex flex-wrap gap-2">
                    {['Ready', 'Blocked', 'NotRequired', 'Missing', 'Unavailable'].map((state) => (
                        <span key={state} className="rounded-full border border-gray-200 px-3 py-1 text-sm text-gray-700">
                            {t(`shippingAdministration.states.${state}`)}
                        </span>
                    ))}
                </div>
                <h3 className="mt-5 font-semibold text-gray-900">{t('shippingAdministration.health.providerOutcomes')}</h3>
                <div className="mt-3 flex flex-wrap gap-2">
                    {['credentialMissing', 'connectionFailed', 'mappingMissing', 'activationBlocked'].map((state) => (
                        <span key={state} className="rounded-full border border-gray-200 px-3 py-1 text-sm text-gray-700">
                            {t(`shippingAdministration.health.outcomes.${state}`)}
                        </span>
                    ))}
                </div>
            </div>

            {visibleAccounts.map((account) => (
                <div key={account.id} className="rounded-lg border border-gray-200 bg-white p-5">
                    <div className="flex flex-col gap-2 md:flex-row md:items-center md:justify-between">
                        <h3 className="font-semibold text-gray-900">{getCarrierDisplayName(supportedCarriers, account.carrierCode)}</h3>
                        <span className="text-sm font-medium text-gray-700">
                            {account.integrationReadiness?.canExecute ? t('shippingAdministration.states.Ready') : t('shippingAdministration.states.Blocked')}
                        </span>
                    </div>
                    <p className="mt-2 text-sm text-gray-600">
                        {t('shippingAdministration.health.credentialAvailability')}: {t(`shippingAdministration.states.${account.integrationReadiness?.credentialAvailability || 'Unknown'}`)}
                    </p>
                    <p className="mt-1 text-sm text-gray-500">
                        {t('shippingAdministration.health.productionCapability')}: {t(`shippingAdministration.states.${account.integrationReadiness?.productionCapability || 'Unknown'}`)}
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
    const { t, i18n } = useTranslation()
    if (audit.length === 0) {
        return <EmptyState title={t('shippingAdministration.audit.emptyTitle')} description={t('shippingAdministration.audit.emptyDescription')} />
    }

    const accountById = new Map(accounts.map((account) => [account.id, account]))

    return (
        <div className="overflow-x-auto rounded-lg border border-gray-200 bg-white">
            <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50">
                    <tr>
                         {['time', 'carrierAccount', 'action', 'actor', 'state', 'reason', 'correlation'].map((column) => (
                             <th key={column} className="px-4 py-3 text-start text-xs font-semibold uppercase text-gray-500">
                                 {t(`shippingAdministration.audit.columns.${column}`)}
                             </th>
                         ))}
                    </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                    {audit.map((entry) => {
                        const account = accountById.get(entry.carrierAccountId)
                        return (
                            <tr key={entry.id}>
                                <td className="px-4 py-3 text-sm text-gray-700">{formatDate(entry.occurredAtUtc, i18n.language)}</td>
                                <td className="px-4 py-3 text-sm text-gray-900">
                                    {account ? getCarrierDisplayName(supportedCarriers, account.carrierCode) : entry.carrierAccountId}
                                </td>
                                <td className="px-4 py-3 text-sm text-gray-700">{entry.action}</td>
                                <td className="px-4 py-3 text-sm text-gray-700">{entry.actorId}</td>
                                <td className="px-4 py-3 text-sm text-gray-700">{`${entry.previousState || '-'} -> ${entry.nextState || '-'}`}</td>
                                <td className="px-4 py-3 text-sm text-gray-700">{entry.reason || '-'}</td>
                                <td className="px-4 py-3 text-sm text-gray-700"><bdi>{entry.correlationId || '-'}</bdi></td>
                            </tr>
                        )
                    })}
                </tbody>
            </table>
        </div>
    )
}

export default function ShippingAdministrationWorkspace({ initialTab = 'overview' }) {
    const { t, i18n } = useTranslation()
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
        <div className="mx-auto max-w-7xl p-4 sm:p-6" dir={i18n.dir()}>
            <div className="mb-6 flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">{t('shippingAdministration.title')}</h1>
                    <p className="mt-1 text-sm text-gray-600">
                        {t('shippingAdministration.subtitle')}
                    </p>
                </div>
                <button onClick={refresh} disabled={loading} className="btn-secondary md:w-auto">
                    {loading ? t('shippingAdministration.refreshing') : t('shippingAdministration.refreshReadiness')}
                </button>
            </div>

            {error && (
                <div className="mb-6 rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">
                    {error}
                </div>
            )}

            <div className="mb-6 grid grid-cols-2 gap-3 md:grid-cols-4">
                <SummaryCard label={t('shippingAdministration.summary.activeAccounts')} value={summary.activeCarriers} />
                <SummaryCard label={t('shippingAdministration.summary.blockedAccounts')} value={summary.blockedCarriers} />
                <SummaryCard label={t('shippingAdministration.summary.supported')} value={supportedCarriers.length} />
                <SummaryCard label={t('shippingAdministration.summary.auditEvents')} value={audit.length} />
            </div>

            <div className="mb-6 overflow-x-auto border-b border-gray-200">
                <div className="flex min-w-max gap-2" role="tablist" aria-label={t('shippingAdministration.tabs.label')}>
                    {TABS.map((tab) => (
                        <button
                            key={tab.key}
                            type="button"
                            role="tab"
                            aria-selected={activeTab === tab.key}
                            onClick={() => setActiveTab(tab.key)}
                            className={`border-b-2 px-4 py-3 text-sm font-medium ${activeTab === tab.key
                                ? 'border-indigo-600 text-indigo-700'
                                : 'border-transparent text-gray-500 hover:text-gray-700'
                                }`}
                        >
                            {t(`shippingAdministration.tabs.${tab.labelKey}`)}
                        </button>
                    ))}
                </div>
            </div>

            {loading && carrierAccounts.length === 0 ? (
                <div className="flex justify-center py-16">
                    <div className="h-8 w-8 animate-spin rounded-full border-4 border-indigo-600 border-t-transparent" role="status" aria-label={t('shippingAdministration.loading')}></div>
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

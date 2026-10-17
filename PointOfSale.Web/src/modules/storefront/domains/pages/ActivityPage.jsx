/**
 * D09 — Activity
 *
 * Constitutional source: DOMAIN-3.1 Canonical Blueprint §2.3 (C09), §2.4 (route: /storefront/domains/activity)
 *
 * Real-time dashboard of domain-related background job status across all tenant domains.
 *
 * DOMAIN-3.1-B: Backed by real READ backend data via useDomainActivity hook.
 */

import { useTranslation } from 'react-i18next'
import { Clock, Globe, Shield, Edit3, CheckCircle2 } from 'lucide-react'

import {
    AuthoringButton,
    AuthoringStatusBadge,
    AuthoringEmptyState,
    AuthoringFeedback,
    AuthoringLoadingState,
    useAuthoringGuard,
} from '../../authoringPlatform'

import { useDomainActivity } from '../hooks'

const EVENT_ICONS = {
    ssl_activated: Shield,
    ssl_renewed: Shield,
    verification_passed: CheckCircle2,
    redirect_created: Globe,
    domain_added: Globe,
    domain_updated: Edit3,
    domain_removed: Globe,
    default: Globe,
}

export function ActivityPage() {
    const { t } = useTranslation()

    const { data, error, isLoading, isError, isUnauthorized, isEmpty, refetch } =
        useDomainActivity()

    const { requestNavigation } = useAuthoringGuard({ isDirty: false, isBusy: isLoading })

    const handleBack = () => {
        requestNavigation('/storefront/domains')
    }

    const getEventIcon = (eventType) => {
        return EVENT_ICONS[eventType] || EVENT_ICONS.default
    }

    if (isLoading) {
        return (
            <div className="space-y-4">
                <AuthoringButton
                    action={{
                        label: t('common.back', 'Back'),
                        onClick: handleBack,
                    }}
                    defaultLabel={t('common.back', 'Back')}
                />
                <AuthoringLoadingState />
            </div>
        )
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
                    title={t('storefront.domains.activityError', 'Unable to load activity')}
                    message={error}
                    action={{
                        label: t('common.retry', 'Retry'),
                        onClick: () => refetch(),
                    }}
                />
            </div>
        )
    }

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
                    <Clock size={24} className="text-indigo-600 dark:text-indigo-400" />
                    <h1 className="text-xl font-semibold text-gray-900 dark:text-gray-100">
                        {t('storefront.domains.domainsActivity', 'Activity')}
                    </h1>
                </div>
                <p className="text-sm text-gray-500 dark:text-gray-400">
                    {t('storefront.domains.activityDesc', 'Recent events across all domains.')}
                </p>
            </div>

            {/* Summary bar */}
            {data && (
                <div className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800">
                    <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                        <div className="text-center">
                            <p className="text-2xl font-bold text-sky-600 dark:text-sky-400">
                                {data.jobsRunning}
                            </p>
                            <p className="text-xs text-gray-500 dark:text-gray-400">
                                {t('storefront.domains.jobsRunning', 'Running')}
                            </p>
                        </div>
                        <div className="text-center">
                            <p className="text-2xl font-bold text-emerald-600 dark:text-emerald-400">
                                {data.jobsSucceeded}
                            </p>
                            <p className="text-xs text-gray-500 dark:text-gray-400">
                                {t('storefront.domains.jobsSucceeded', 'Succeeded')}
                            </p>
                        </div>
                        <div className="text-center">
                            <p className="text-2xl font-bold text-red-600 dark:text-red-400">
                                {data.jobsFailed}
                            </p>
                            <p className="text-xs text-gray-500 dark:text-gray-400">
                                {t('storefront.domains.jobsFailed', 'Failed')}
                            </p>
                        </div>
                    </div>
                </div>
            )}

            {/* Loading / Empty / Content states */}
            {isLoading && (
                <div className="rounded-xl border border-gray-200 bg-white p-8 dark:border-gray-700 dark:bg-gray-800">
                    <AuthoringLoadingState />
                </div>
            )}

            {isEmpty && (
                <div className="rounded-xl border border-gray-200 bg-white p-8 dark:border-gray-700 dark:bg-gray-800">
                    <AuthoringEmptyState
                        title={t('storefront.domains.noActivity', 'No recent activity')}
                        description={t(
                            'storefront.domains.noActivityDesc',
                            'No domain events have been recorded yet.',
                        )}
                    />
                </div>
            )}

            {data && data.recentActivity && data.recentActivity.length > 0 && (
                <div className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800">
                    <h2 className="mb-4 text-lg font-semibold text-gray-900 dark:text-gray-100">
                        {t('storefront.domains.recentActivity', 'Recent Activity')}
                    </h2>
                    <div className="space-y-3">
                        {data.recentActivity.map((event) => {
                            const Icon = getEventIcon(event.eventType)
                            return (
                                <div
                                    key={event.id}
                                    className="flex items-start gap-3 rounded-lg border border-gray-200 p-3 dark:border-gray-700"
                                >
                                    <div className="mt-0.5 flex h-8 w-8 items-center justify-center rounded-lg bg-gray-100 dark:bg-gray-700">
                                        <Icon size={16} className="text-gray-600 dark:text-gray-300" />
                                    </div>
                                    <div className="flex-1">
                                        <div className="flex items-center justify-between">
                                            <p className="font-medium text-gray-900 dark:text-gray-100">
                                                {t(
                                                    `storefront.domains.event${event.eventType}`,
                                                    event.eventType,
                                                )}
                                            </p>
                                            <span className="text-xs text-gray-500 dark:text-gray-400">
                                                {t('storefront.domains.timestampFormat', '{{date, time}}', {
                                                    date: new Date(event.timestampUtc).toLocaleDateString(),
                                                    time: new Date(event.timestampUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
                                                })}
                                            </span>
                                        </div>
                                        <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
                                            {t('storefront.domains.onDomain', 'on {{domain}}', {
                                                domain: event.hostName,
                                            })}
                                        </p>
                                        <div className="mt-1">
                                            <AuthoringStatusBadge
                                                state={
                                                    event.status === 'success'
                                                        ? 'published'
                                                        : event.status === 'failed'
                                                          ? 'error'
                                                          : 'draft'
                                                }
                                            >
                                                {event.status}
                                            </AuthoringStatusBadge>
                                        </div>
                                    </div>
                                </div>
                            )
                        })}
                    </div>
                </div>
            )}
        </div>
    )
}

export default ActivityPage

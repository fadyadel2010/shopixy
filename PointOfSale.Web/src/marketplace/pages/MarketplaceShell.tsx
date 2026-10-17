import { useEffect } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useMarketplaceProfileQuery } from '../hooks/useMarketplaceProfileQuery'
import { DiscoverTab } from './tabs/DiscoverTab'
import { MyServicesTab } from './tabs/MyServicesTab'
import { MyRequestsTab } from './tabs/MyRequestsTab'
import { IncomingTab } from './tabs/IncomingTab'
import { ProfileTab } from './tabs/ProfileTab'
import { useRTL } from '../../hooks/useRTL'

type Tab = 'discover' | 'my-services' | 'my-requests' | 'incoming' | 'profile'

const profileOnlyTabs = new Set<Tab>(['my-services', 'incoming'])

const isTab = (value: string | null): value is Tab => {
    return value === 'discover'
        || value === 'my-services'
        || value === 'my-requests'
        || value === 'incoming'
        || value === 'profile'
}

const normalizeTab = (value: string | null, hasProfile: boolean): Tab => {
    if (!isTab(value)) {
        return 'discover'
    }

    if (!hasProfile && profileOnlyTabs.has(value)) {
        return 'discover'
    }

    return value
}

export function MarketplaceShell() {
    const { t } = useTranslation()
    const { dir } = useRTL()
    const [searchParams, setSearchParams] = useSearchParams()
    const requestedTab = searchParams.get('tab')

    const { data: profile, isFetched: hasResolvedProfile, isError: profileAuthorityFailed } = useMarketplaceProfileQuery()

    const hasProfile = !profileAuthorityFailed
        && profile?.hasMarketplaceProfile === true
        && profile.isActive === true
    const activeTab = normalizeTab(requestedTab, hasProfile)

    useEffect(() => {
        if (requestedTab === null) {
            return
        }

        const canonicalTab = normalizeTab(requestedTab, hasResolvedProfile ? hasProfile : true)

        if (requestedTab !== canonicalTab) {
            setSearchParams(current => {
                const next = new URLSearchParams(current)
                next.set('tab', canonicalTab)
                return next
            }, { replace: true })
        }
    }, [hasProfile, hasResolvedProfile, requestedTab, setSearchParams])

    const setActiveTab = (tab: Tab) => {
        setSearchParams(current => {
            const next = new URLSearchParams(current)
            next.set('tab', tab)
            return next
        })
    }

    return (
        <div dir={dir} className="space-y-6">
            <div className="flex items-center justify-between">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">{t('servicesMarketplace.shell.title')}</h1>
                    <p className="text-sm text-gray-500 mt-1">{t('servicesMarketplace.shell.subtitle')}</p>
                </div>
            </div>

            <div className="card">
                <div className="flex gap-2 border-b border-gray-200 -mx-6 -mt-6 px-6 overflow-x-auto">
                    <button
                        className={`px-4 py-3 font-medium text-sm whitespace-nowrap transition-colors border-b-2 ${
                            activeTab === 'discover'
                                ? 'border-indigo-600 text-indigo-600'
                                : 'border-transparent text-gray-600 hover:text-gray-900 hover:border-gray-300'
                        }`}
                        onClick={() => setActiveTab('discover')}
                    >
                        {t('servicesMarketplace.tabs.discover')}
                    </button>

                    {hasProfile && (
                        <>
                            <button
                                className={`px-4 py-3 font-medium text-sm whitespace-nowrap transition-colors border-b-2 ${
                                    activeTab === 'my-services'
                                        ? 'border-indigo-600 text-indigo-600'
                                        : 'border-transparent text-gray-600 hover:text-gray-900 hover:border-gray-300'
                                }`}
                                onClick={() => setActiveTab('my-services')}
                            >
                                {t('servicesMarketplace.tabs.myServices')}
                            </button>
                            <button
                                className={`px-4 py-3 font-medium text-sm whitespace-nowrap transition-colors border-b-2 ${
                                    activeTab === 'incoming'
                                        ? 'border-indigo-600 text-indigo-600'
                                        : 'border-transparent text-gray-600 hover:text-gray-900 hover:border-gray-300'
                                }`}
                                onClick={() => setActiveTab('incoming')}
                            >
                                {t('servicesMarketplace.tabs.incoming')}
                            </button>
                        </>
                    )}

                    <button
                        className={`px-4 py-3 font-medium text-sm whitespace-nowrap transition-colors border-b-2 ${
                            activeTab === 'my-requests'
                                ? 'border-indigo-600 text-indigo-600'
                                : 'border-transparent text-gray-600 hover:text-gray-900 hover:border-gray-300'
                        }`}
                        onClick={() => setActiveTab('my-requests')}
                    >
                        {t('servicesMarketplace.tabs.myRequests')}
                    </button>

                    <button
                        className={`px-4 py-3 font-medium text-sm whitespace-nowrap transition-colors border-b-2 ${
                            activeTab === 'profile'
                                ? 'border-indigo-600 text-indigo-600'
                                : 'border-transparent text-gray-600 hover:text-gray-900 hover:border-gray-300'
                        }`}
                        onClick={() => setActiveTab('profile')}
                    >
                        {t('servicesMarketplace.tabs.profile')}
                    </button>
                </div>
            </div>

            <div>
                {activeTab === 'discover' && <DiscoverTab />}
                {activeTab === 'my-services' && <MyServicesTab />}
                {activeTab === 'my-requests' && <MyRequestsTab />}
                {activeTab === 'incoming' && <IncomingTab />}
                {activeTab === 'profile' && <ProfileTab />}
            </div>
        </div>
    )
}

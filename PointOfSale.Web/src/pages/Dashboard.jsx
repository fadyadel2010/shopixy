import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PosDashboardContent from '../components/PosDashboardContent'
import StorefrontAnalyticsDashboard from './StorefrontAnalyticsDashboard'
import { useRTL } from '../hooks/useRTL'

const DASHBOARD_TAB_STORAGE_KEY = 'admin.dashboard.activeTab'

const DASHBOARD_TABS = [
    { id: 'pos', labelKey: 'dashboard.tabs.pos' },
    { id: 'storefront', labelKey: 'dashboard.tabs.storefront' },
]

export default function Dashboard() {
    const { t } = useTranslation()
    const { isRTL, dir } = useRTL()
    const [activeTab, setActiveTab] = useState(() => {
        const storedValue = window.localStorage.getItem(DASHBOARD_TAB_STORAGE_KEY)
        return storedValue === 'storefront' ? 'storefront' : 'pos'
    })

    useEffect(() => {
        window.localStorage.setItem(DASHBOARD_TAB_STORAGE_KEY, activeTab)
    }, [activeTab])

    return (
        <div className="space-y-6" dir={dir}>
            <div className="rounded-2xl border border-gray-200 bg-white p-3 shadow-sm">
                <div className={`flex flex-col gap-3 md:flex-row md:items-center md:justify-between ${isRTL ? 'md:flex-row-reverse' : ''}`}>
                    <div className={isRTL ? 'text-right' : 'text-left'}>
                        <p className="text-sm font-semibold uppercase tracking-wide text-gray-400">
                            {t('dashboard.title')}
                        </p>
                        <h1 className="text-xl font-bold text-gray-900">
                            {t('dashboard.dashboardSwitcher')}
                        </h1>
                    </div>

                    <div className={`inline-flex rounded-xl bg-gray-100 p-1 ${isRTL ? 'flex-row-reverse self-end md:self-auto' : ''}`}>
                        {DASHBOARD_TABS.map(tab => (
                            <button
                                key={tab.id}
                                onClick={() => setActiveTab(tab.id)}
                                className={`rounded-lg px-4 py-2 text-sm font-medium transition-colors ${activeTab === tab.id
                                    ? 'bg-white text-gray-900 shadow-sm'
                                    : 'text-gray-500 hover:text-gray-800'
                                    }`}
                            >
                                {t(tab.labelKey)}
                            </button>
                        ))}
                    </div>
                </div>
            </div>

            {activeTab === 'pos'
                ? <PosDashboardContent />
                : <StorefrontAnalyticsDashboard />}
        </div>
    )
}

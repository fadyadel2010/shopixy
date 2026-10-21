import { useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useAuthStore } from '../store/authStore'
import LanguageSwitcher from './LanguageSwitcher'
import Sidebar from './Sidebar'
import EmailVerificationBanner from './EmailVerificationBanner'
import MarketplaceNotificationBell from './MarketplaceNotificationBell'
import HelpButton from '../admin/help/HelpButton'
import HelpDrawer from '../admin/help/HelpDrawer'
import { getHelpArticleKey } from '../admin/help/helpRegistry'
import { useRTL } from '../hooks/useRTL'
import api from '../services/api'
import { invalidatePosRuntimeCache } from '../services/posRuntimeCache'
import { broadcastPosVendorLogout } from '../auth/posVendorSessionAuthority'
import SmartNavigationProvider from '../navigation/SmartNavigationProvider'
import CommandPalette from '../navigation/CommandPalette'
import NavigationBreadcrumbs from '../navigation/NavigationBreadcrumbs'

export default function Layout() {
    return <SmartNavigationProvider><LayoutContent /></SmartNavigationProvider>
}

function LayoutContent() {
    const { t } = useTranslation()
    const { user, logout } = useAuthStore()
    const navigate = useNavigate()
    const location = useLocation()
    const [sidebarOpen, setSidebarOpen] = useState(false)
    const [helpOpen, setHelpOpen] = useState(false)
    const [isLoggingOut, setIsLoggingOut] = useState(false)
    const { dir, isRTL } = useRTL()
    const articleKey = getHelpArticleKey(location.pathname, location.search)

    const handleLogout = async () => {
        if (isLoggingOut) {
            return
        }

        setIsLoggingOut(true)

        try {
            await api.post('/auth/logout')
        } catch (error) {
            if (import.meta.env.DEV) {
                console.warn('POS logout request failed, clearing local session anyway.', error)
            }
        } finally {
            invalidatePosRuntimeCache({ reason: 'logout', broadcast: false })
            broadcastPosVendorLogout()
            logout()
            // Navigate will unmount this component, so no need to reset isLoggingOut
            navigate('/login', { replace: true })
        }
    }

    return (
        <div className="min-h-screen bg-gray-50" dir={dir}>
            {/* Sidebar */}
            <Sidebar isOpen={sidebarOpen} onClose={() => setSidebarOpen(false)} />

            {/* Main Content Area */}
            <div className={isRTL ? 'lg:pr-72 transition-all duration-300' : 'lg:pl-72 transition-all duration-300'}>
                {/* Top Header */}
                <header className="bg-white shadow-sm border-b border-gray-200 sticky top-0 z-30">
                    <div className="px-4 sm:px-6 lg:px-8">
                        <div className="flex justify-between items-center h-16">
                            {/* Mobile Menu Button & Logo */}
                            <div className="flex items-center gap-2 sm:gap-4">
                                <button
                                    onClick={() => setSidebarOpen(true)}
                                    aria-label={t('smartNavigation.openNavigation')}
                                    className="lg:hidden text-gray-600 hover:text-gray-900 hover:bg-gray-100 rounded-lg p-2 transition-colors"
                                >
                                    <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 6h16M4 12h16M4 18h16" />
                                    </svg>
                                </button>

                                <div className="hidden items-center gap-2 sm:flex">
                                    <div className="w-8 h-8 bg-gradient-to-br from-indigo-600 to-indigo-700 rounded-lg flex items-center justify-center shadow-md">
                                        <span className="text-white font-bold text-lg">🏪</span>
                                    </div>
                                    <h1 className="text-lg font-bold text-gray-900 hidden sm:block">{t('app.name')}</h1>
                                </div>
                            </div>

                            {/* User Menu */}
                            <div className="flex items-center gap-1 sm:gap-3">
                                <CommandPalette onNavigate={() => setSidebarOpen(false)} />
                                {/* Marketplace Notifications */}
                                <MarketplaceNotificationBell />

                                {/* Settings */}
                                <button
                                    onClick={() => navigate('/settings')}
                                    className="p-1.5 sm:p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded-lg transition-colors"
                                    title={t('settings.settings')}
                                >
                                    <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                                    </svg>
                                </button>

                                <div className="hidden h-6 w-px bg-gray-200 mx-1 sm:block"></div>
                                <LanguageSwitcher />

                                <div className="hidden md:flex items-center gap-3 px-3 py-2 bg-gray-50 rounded-lg">
                                    <div className="text-start">
                                        <div className="text-sm font-medium text-gray-900">
                                            {user?.firstName} {user?.lastName}
                                        </div>
                                        <div className="text-xs text-gray-500">
                                            {user?.role}
                                        </div>
                                    </div>
                                    <div className="w-10 h-10 bg-gradient-to-br from-indigo-500 to-indigo-600 rounded-full flex items-center justify-center text-white font-semibold shadow-md">
                                        {user?.firstName?.[0]}{user?.lastName?.[0]}
                                    </div>
                                </div>

                                <button
                                    onClick={handleLogout}
                                    disabled={isLoggingOut}
                                    className="flex items-center gap-2 px-2 sm:px-3 py-2 text-sm font-medium text-red-600 hover:bg-red-50 rounded-lg transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
                                    title={t('app.logout')}
                                    aria-busy={isLoggingOut}
                                >
                                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1" />
                                    </svg>
                                    <span className="hidden sm:inline">{isLoggingOut ? t('common.loading') : t('app.logout')}</span>
                                </button>
                            </div>
                        </div>
                    </div>
                </header>

                {/* Phase 8.4: Email Verification Banner */}
                <EmailVerificationBanner />

                {/* Main Content */}
                <main className="px-4 sm:px-6 lg:px-8 py-6">
                    <NavigationBreadcrumbs />
                    <div className="mb-4 flex justify-end">
                        <HelpButton
                            onClick={() => setHelpOpen(true)}
                            hasArticle={!!articleKey}
                        />
                    </div>

                    <Outlet />
                </main>
            </div>

            <HelpDrawer
                isOpen={helpOpen}
                onClose={() => setHelpOpen(false)}
                articleKey={articleKey}
                pathname={`${location.pathname}${location.search}`}
            />
        </div>
    )
}

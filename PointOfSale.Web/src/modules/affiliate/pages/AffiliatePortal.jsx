/**
 * AF-15: Affiliate Portal App Shell
 * 
 * Sidebar + TopBar + Content layout.
 * Dark-first premium SaaS design.
 */
import { useState, useEffect, useCallback, useRef } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { motion, AnimatePresence } from 'framer-motion'
import { AFFILIATE_AUTH_SESSION_EVENT_KEY, affiliateAuthApi } from '../../../services/affiliateAuthApi'
import { Menu, X, Wallet, User, ChevronLeft, LogOut, Settings, BarChart3, DollarSign, ShoppingBag, Link2, Package, LayoutDashboard, Globe, ArrowDownCircle } from 'lucide-react'
import { useLanguage } from '../i18n/LanguageContext'
import LanguageSwitcher from '../components/LanguageSwitcher'
import ThemeSwitcher from '../components/ThemeSwitcher'

// Lazy-load tab components
import DashboardTab from './portal/DashboardTab'
import DiscoverTabEnhanced from './portal/DiscoverTabEnhanced' // FE-1: New intelligence-driven discover
import LinksTab from './portal/LinksTab'
import LandingPagesTab from './portal/LandingPagesTab' // AF-27B: My Landing Pages
import OrdersTab from './portal/OrdersTab'
import EarningsTab from './portal/EarningsTab'
import AnalyticsTab from './portal/AnalyticsTab' // AF-24: Performance analytics
import PayoutsTab from './portal/PayoutsTab' // AF-38: Withdrawals
import ProfileTab from './portal/ProfileTab'

const TAB_DEFS = [
    { id: 'dashboard', labelKey: 'portal.dashboard', icon: LayoutDashboard },
    { id: 'discover', labelKey: 'portal.promote', icon: Package },
    { id: 'links', labelKey: 'portal.myLinks', icon: Link2 },
    { id: 'landing-pages', labelKey: 'portal.landingPages', icon: Globe },
    { id: 'orders', labelKey: 'portal.orders', icon: ShoppingBag },
    { id: 'earnings', labelKey: 'portal.earnings', icon: DollarSign },
    { id: 'payouts', labelKey: 'portal.payouts', icon: ArrowDownCircle },
    { id: 'analytics', labelKey: 'portal.analytics', icon: BarChart3 },
    { id: 'profile', labelKey: 'portal.settings', icon: Settings },
]

const AFFILIATE_SESSION_REVALIDATION_INTERVAL_MS = 60000

const AffiliatePortal = () => {
    const navigate = useNavigate()
    const [searchParams, setSearchParams] = useSearchParams()
    const [activeTab, setActiveTab] = useState(searchParams.get('tab') || 'dashboard')
    const [sidebarOpen, setSidebarOpen] = useState(true)
    const [mobileSidebarOpen, setMobileSidebarOpen] = useState(false)
    const [profile, setProfile] = useState(null)
    const [isBootstrapping, setIsBootstrapping] = useState(true)
    const [authError, setAuthError] = useState(false)
    const mountedRef = useRef(false)
    const restoreInFlightRef = useRef(null)

    const restoreSession = useCallback(() => {
        if (restoreInFlightRef.current) return restoreInFlightRef.current

        const request = affiliateAuthApi.restoreSession()
            .then((sessionProfile) => {
                if (mountedRef.current) {
                    setProfile(sessionProfile)
                    setAuthError(false)
                }
            })
            .catch((error) => {
                if (!mountedRef.current) return

                if (error.response?.status === 401 || error.response?.status === 403) {
                    setProfile(null)
                    setAuthError(false)
                    const returnUrl = `${window.location.pathname}${window.location.search}`
                    navigate(`/affiliate/login?returnUrl=${encodeURIComponent(returnUrl)}`, { replace: true })
                } else {
                    setAuthError(true)
                }
            })
            .finally(() => {
                restoreInFlightRef.current = null
                if (mountedRef.current) {
                    setIsBootstrapping(false)
                }
            })

        restoreInFlightRef.current = request
        return request
    }, [navigate])

    useEffect(() => {
        mountedRef.current = true

        void restoreSession()
        const revalidationInterval = window.setInterval(() => {
            void restoreSession()
        }, AFFILIATE_SESSION_REVALIDATION_INTERVAL_MS)

        const handleStorage = (event) => {
            if (event.key === AFFILIATE_AUTH_SESSION_EVENT_KEY && event.oldValue !== event.newValue) {
                void restoreSession()
            }
        }

        window.addEventListener('storage', handleStorage)
        return () => {
            mountedRef.current = false
            window.clearInterval(revalidationInterval)
            window.removeEventListener('storage', handleStorage)
        }
    }, [restoreSession])

    const switchTab = useCallback((tab) => {
        setActiveTab(tab)
        // AF-32: When switching away from discover, clear filter params
        // When staying on discover, let the tab manage its own params
        if (tab !== 'discover') {
            setSearchParams({ tab })
        } else {
            // Preserve existing category/search params on discover
            const newParams = new URLSearchParams(searchParams)
            newParams.set('tab', tab)
            setSearchParams(newParams)
        }
        setMobileSidebarOpen(false)
    }, [searchParams, setSearchParams])

    const handleLogout = () => {
        affiliateAuthApi.logout()
    }

    const formatCurrency = (val, currency) => {
        if (val == null) return '—'
        const normalizedCurrency = typeof currency === 'string' ? currency.trim().toUpperCase() : ''
        if (!/^[A-Z]{3}$/.test(normalizedCurrency)) {
            return `${Number(val).toFixed(2)} ${normalizedCurrency || 'UNKNOWN'}`
        }
        return new Intl.NumberFormat('en-US', {
            style: 'currency', currency: normalizedCurrency,
            minimumFractionDigits: 2
        }).format(val)
    }

    const renderTab = () => {
        const props = { formatCurrency }
        switch (activeTab) {
            case 'dashboard': return <DashboardTab {...props} />
            case 'discover': return <DiscoverTabEnhanced /> // FE-1: Intelligence-driven discover
            case 'links': return <LinksTab {...props} />
            case 'landing-pages': return <LandingPagesTab {...props} />
            case 'orders': return <OrdersTab {...props} />
            case 'earnings': return <EarningsTab {...props} />
            case 'payouts': return <PayoutsTab {...props} />
            case 'analytics': return <AnalyticsTab />
            case 'profile': return <ProfileTab />
            default: return <DashboardTab {...props} />
        }
    }

    const { t, dir, isRTL } = useLanguage()
    const TABS = TAB_DEFS.map(tab => ({ ...tab, label: t(tab.labelKey) }))

    if (isBootstrapping) {
        return (
            <div
                dir={dir}
                className="min-h-screen bg-gray-50 dark:bg-[#0B0F19] text-gray-800 dark:text-gray-200 flex items-center justify-center"
                style={{ fontFamily: isRTL ? "'Noto Kufi Arabic', 'Inter', sans-serif" : "'Inter', -apple-system, system-ui, sans-serif" }}
            >
                <div className="rounded-2xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-[#0D1117] px-6 py-4 shadow-sm dark:shadow-none">
                    <p className="text-sm text-gray-600 dark:text-gray-400">{t('portal.restoringSession')}</p>
                </div>
            </div>
        )
    }

    if (authError && !profile) {
        return (
            <div dir={dir} className="min-h-screen bg-gray-50 dark:bg-[#0B0F19] flex items-center justify-center px-4">
                <div className="max-w-md rounded-2xl border border-red-500/20 bg-white dark:bg-[#0D1117] p-6 text-center">
                    <p className="text-sm text-gray-700 dark:text-gray-300">{t('portal.sessionRestoreError')}</p>
                    <button type="button" onClick={() => void restoreSession()} className="mt-4 rounded-xl bg-[#6C63FF] px-5 py-3 text-sm font-semibold text-white">
                        {t('portal.retrySession')}
                    </button>
                </div>
            </div>
        )
    }

    if (!profile) {
        return null
    }

    const currentAffiliateActorKey = profile.affiliateAccountId || 'unadmitted-affiliate-actor'

    return (
        <div dir={dir} className="min-h-screen bg-gray-50 dark:bg-[#0B0F19] text-gray-800 dark:text-gray-200 flex" style={{ fontFamily: isRTL ? "'Noto Kufi Arabic', 'Inter', sans-serif" : "'Inter', -apple-system, system-ui, sans-serif" }}>
            {/* Desktop Sidebar */}
            <aside className={`hidden md:flex flex-col border-r border-gray-200 dark:border-white/[0.06] bg-white dark:bg-[#0D1117] transition-all duration-300 ${sidebarOpen ? 'w-60' : 'w-[68px]'}`}>
                {/* Logo */}
                <div className="h-16 flex items-center px-4 border-b border-gray-200 dark:border-white/[0.06]">
                    <div className="w-9 h-9 rounded-xl bg-gradient-to-br from-[#6C63FF] to-[#8B5CF6] flex items-center justify-center flex-shrink-0 shadow-lg shadow-[#6C63FF]/15">
                        <DollarSign className="w-5 h-5 text-white" />
                    </div>
                    {sidebarOpen && (
                        <span className="ml-3 font-bold text-sm bg-gradient-to-r from-[#6C63FF] to-[#A855F7] bg-clip-text text-transparent truncate">
                            {t('portal.title')}
                        </span>
                    )}
                </div>

                {/* Nav */}
                <nav className="flex-1 py-4 px-2 space-y-1">
                    {TABS.map(tab => {
                        const Icon = tab.icon
                        const isActive = activeTab === tab.id
                        return (
                            <button
                                key={tab.id}
                                onClick={() => switchTab(tab.id)}
                                className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm font-medium transition-all duration-200 ${isActive
                                    ? 'bg-[#6C63FF]/10 text-[#6C63FF] border border-[#6C63FF]/20'
                                    : 'text-gray-500 hover:text-gray-700 dark:hover:text-gray-300 hover:bg-gray-50 dark:hover:bg-white/[0.03] border border-transparent'
                                    }`}
                                title={tab.label}
                            >
                                <Icon className={`w-[18px] h-[18px] flex-shrink-0 ${isActive ? 'text-[#6C63FF]' : ''}`} />
                                {sidebarOpen && <span className="truncate">{tab.label}</span>}
                            </button>
                        )
                    })}
                </nav>

                {/* Collapse + Logout */}
                <div className="p-2 border-t border-gray-200 dark:border-white/[0.06] space-y-1">
                    <button
                        onClick={() => setSidebarOpen(!sidebarOpen)}
                        className="w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm text-gray-500 hover:text-gray-700 dark:hover:text-gray-300 hover:bg-gray-50 dark:hover:bg-white/[0.03] transition-all"
                    >
                        <ChevronLeft className={`w-[18px] h-[18px] transition-transform ${!sidebarOpen ? 'rotate-180' : ''}`} />
                        {sidebarOpen && <span>{t('portal.collapse')}</span>}
                    </button>
                    <button
                        onClick={handleLogout}
                        className="w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm text-red-400/70 hover:text-red-400 hover:bg-red-500/5 transition-all"
                    >
                        <LogOut className="w-[18px] h-[18px]" />
                        {sidebarOpen && <span>{t('portal.logout')}</span>}
                    </button>
                </div>
            </aside>

            {/* Mobile Sidebar Overlay */}
            <AnimatePresence>
                {mobileSidebarOpen && (
                    <>
                        <motion.div
                            initial={{ opacity: 0 }}
                            animate={{ opacity: 1 }}
                            exit={{ opacity: 0 }}
                            onClick={() => setMobileSidebarOpen(false)}
                            className="fixed inset-0 bg-gray-900/40 dark:bg-black/60 z-40 md:hidden"
                        />
                        <motion.aside
                            initial={{ x: -260 }}
                            animate={{ x: 0 }}
                            exit={{ x: -260 }}
                            transition={{ type: 'spring', damping: 25, stiffness: 300 }}
                            className="fixed left-0 top-0 bottom-0 w-60 bg-white dark:bg-[#0D1117] border-r border-gray-200 dark:border-white/[0.06] z-50 md:hidden flex flex-col"
                        >
                            <div className="h-16 flex items-center justify-between px-4 border-b border-gray-200 dark:border-white/[0.06]">
                                <span className="font-bold text-sm text-[#6C63FF]">{t('portal.title')}</span>
                                <button onClick={() => setMobileSidebarOpen(false)} className="text-gray-500">
                                    <X className="w-5 h-5" />
                                </button>
                            </div>
                            <nav className="flex-1 py-4 px-2 space-y-1">
                                {TABS.map(tab => {
                                    const Icon = tab.icon
                                    const isActive = activeTab === tab.id
                                    return (
                                        <button
                                            key={tab.id}
                                            onClick={() => switchTab(tab.id)}
                                            className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm font-medium transition-all ${isActive ? 'bg-[#6C63FF]/10 text-[#6C63FF]' : 'text-gray-500 hover:text-gray-700 dark:hover:text-gray-300'
                                                }`}
                                        >
                                            <Icon className="w-[18px] h-[18px]" />
                                            <span>{tab.label}</span>
                                        </button>
                                    )
                                })}
                            </nav>
                            <div className="p-2 border-t border-gray-200 dark:border-white/[0.06]">
                                <button onClick={handleLogout} className="w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm text-red-400/70 hover:text-red-400">
                                    <LogOut className="w-[18px] h-[18px]" />
                                    <span>{t('portal.logout')}</span>
                                </button>
                            </div>
                        </motion.aside>
                    </>
                )}
            </AnimatePresence>

            {/* Main Content */}
            <div className="flex-1 flex flex-col min-w-0">
                {/* Top Bar */}
                <header className="h-16 flex items-center justify-between px-4 md:px-6 border-b border-gray-200 dark:border-white/[0.06] bg-white dark:bg-[#0D1117]/50 backdrop-blur-xl flex-shrink-0">
                    <div className="flex items-center gap-3">
                        <button
                            onClick={() => setMobileSidebarOpen(true)}
                            className="md:hidden text-gray-500 hover:text-gray-700 dark:hover:text-gray-300"
                        >
                            <Menu className="w-5 h-5" />
                        </button>
                        <h2 className="text-base font-semibold text-gray-900 dark:text-white capitalize">
                            {TABS.find(tab => tab.id === activeTab)?.label || t('portal.dashboard')}
                        </h2>
                    </div>

                    <div className="flex items-center gap-4">
                        {/* Quick balance pills */}
                        <div className="hidden lg:flex items-center gap-2">
                            <div className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-emerald-500/10 border border-emerald-500/20">
                                <Wallet className="w-3.5 h-3.5 text-emerald-400" />
                                <span className="text-xs font-medium text-emerald-400">
                                    {profile?.referralCode || '—'}
                                </span>
                            </div>
                        </div>

                        <ThemeSwitcher />
                        <LanguageSwitcher />

                        {/* Profile */}
                        <div className="flex items-center gap-2.5">
                            <div className="w-8 h-8 rounded-lg bg-gradient-to-br from-[#6C63FF]/20 to-[#8B5CF6]/20 border border-[#6C63FF]/20 flex items-center justify-center">
                                <User className="w-4 h-4 text-[#6C63FF]" />
                            </div>
                            <span className="hidden sm:block text-sm text-gray-600 dark:text-gray-400 max-w-[120px] truncate">
                                {profile?.displayName || profile?.email || 'Affiliate'}
                            </span>
                        </div>
                    </div>
                </header>

                {/* Page Content */}
                <main className="flex-1 overflow-y-auto p-4 md:p-6">
                    <AnimatePresence mode="wait">
                        <motion.div
                            key={`${currentAffiliateActorKey}:${activeTab}`}
                            initial={{ opacity: 0, y: 8 }}
                            animate={{ opacity: 1, y: 0 }}
                            exit={{ opacity: 0, y: -8 }}
                            transition={{ duration: 0.25 }}
                        >
                            {renderTab()}
                        </motion.div>
                    </AnimatePresence>
                </main>
            </div>
        </div>
    )
}

export default AffiliatePortal

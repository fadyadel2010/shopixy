import { useMemo, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import {
    ArrowUpRight,
    BadgePercent,
    BadgeDollarSign,
    MessageSquareQuote,
    ChevronRight,
    CreditCard,
    LayoutDashboard,
    LayoutTemplate,
    Landmark,
    LogOut,
    Menu,
    Package,
    RotateCw,
    Shield,
    Users,
    X,
} from 'lucide-react'
import { logoutSuperAdmin } from '../auth/superAdminAuthService'
import { useSuperAdminAuthStore } from '../auth/superAdminAuthStore'
import {
    hasSuperAdminNavigationCapability,
    type SuperAdminIdentity,
    type SuperAdminNavigationCapability,
} from '../auth/superAdminAuth.types'

interface NavigationItem {
    label: string
    description: string
    icon: typeof LayoutDashboard
    to?: string
    disabled?: boolean
    capability?: SuperAdminNavigationCapability
}

const navigationItems: NavigationItem[] = [
    {
        label: 'Dashboard',
        description: 'Platform pulse',
        icon: LayoutDashboard,
        disabled: true,
    },
    {
        label: 'Finance',
        description: 'Payments and revenue',
        icon: BadgeDollarSign,
        to: '/super-admin/finance',
        capability: 'super-admin.finance.view',
    },
    {
        label: 'Payments',
        description: 'Manual settlement review',
        icon: CreditCard,
        to: '/super-admin/payments',
        capability: 'super-admin.payments.view',
    },
    {
        label: 'Payment Providers',
        description: 'CommerceHub provider configuration',
        icon: CreditCard,
        to: '/super-admin/commercehub/payment-providers',
        capability: 'super-admin.payment-providers.view',
    },
    {
        label: 'Orders',
        description: 'Cross-platform order view',
        icon: Package,
        to: '/super-admin/orders',
        capability: 'super-admin.orders.view',
    },
    {
        label: 'Reviews',
        description: 'Trust and moderation queue',
        icon: MessageSquareQuote,
        to: '/super-admin/reviews',
        capability: 'super-admin.reviews.view',
    },
    {
        label: 'Users',
        description: 'Governance and fraud controls',
        icon: Users,
        to: '/super-admin/users',
        capability: 'super-admin.users.govern',
    },
    {
        label: 'Marketplace Governance',
        description: 'Visibility and suppression controls',
        icon: Shield,
        to: '/super-admin/marketplace/governance',
        capability: 'super-admin.marketplace.govern',
    },
    {
        label: 'Marketplace Operations',
        description: 'Reindex and recovery tools',
        icon: RotateCw,
        to: '/super-admin/marketplace/operations',
        capability: 'super-admin.marketplace.operate',
    },
    {
        label: 'Refunds',
        description: 'Recovery and exceptions',
        icon: ArrowUpRight,
        to: '/super-admin/refunds',
        capability: 'super-admin.refunds.manage',
    },
    {
        label: 'Payouts',
        description: 'Settlement operations',
        icon: Landmark,
        to: '/super-admin/payouts',
        capability: 'super-admin.payouts.manage',
    },
    {
        label: 'Commission',
        description: 'Rule engine and previews',
        icon: BadgePercent,
        to: '/super-admin/commission',
        capability: 'super-admin.commission.manage',
    },
    {
        label: 'Promo Codes',
        description: 'Commission discount controls',
        icon: BadgePercent,
        to: '/super-admin/promo-codes',
        capability: 'super-admin.promotions.manage',
    },
    {
        label: 'Homepage',
        description: 'Visual draft builder',
        icon: LayoutTemplate,
        to: '/super-admin/homepage',
        capability: 'super-admin.homepage.manage',
    },
    {
        label: 'Storefront Defaults',
        description: 'Tenant fallback footer',
        icon: LayoutTemplate,
        to: '/super-admin/storefront/footer',
        capability: 'super-admin.storefront-defaults.manage',
    },
    {
        label: 'Marketplace CMS',
        description: 'Global pages and policies',
        icon: LayoutTemplate,
        to: '/super-admin/marketplace/pages',
        capability: 'super-admin.marketplace-cms.manage',
    },
    {
        label: 'Marketplace Footer',
        description: 'Global footer and social links',
        icon: LayoutTemplate,
        to: '/super-admin/marketplace/footer',
        capability: 'super-admin.marketplace-footer.manage',
    },
]

function NavigationLink({ item, onNavigate }: { item: NavigationItem; onNavigate?: () => void }) {
    const Icon = item.icon

    if (item.disabled || !item.to) {
        return (
            <button
                type="button"
                title="This section is queued for the next dashboard phase."
                className="group flex w-full items-center gap-3 rounded-2xl border border-white/8 bg-white/[0.03] px-4 py-3 text-left text-slate-400 transition hover:border-white/12 hover:bg-white/[0.05]"
            >
                <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-white/[0.06] text-slate-300">
                    <Icon className="h-5 w-5" />
                </div>
                <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                        <span className="font-semibold text-slate-100">{item.label}</span>
                        <span className="rounded-full border border-amber-400/30 bg-amber-400/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-[0.18em] text-amber-200">
                            Soon
                        </span>
                    </div>
                    <p className="mt-0.5 text-xs text-slate-400">{item.description}</p>
                </div>
            </button>
        )
    }

    return (
        <NavLink
            end
            onClick={onNavigate}
            to={item.to}
            className={({ isActive }) =>
                [
                    'group flex items-center gap-3 rounded-2xl border px-4 py-3 text-left transition',
                    isActive
                        ? 'border-emerald-400/30 bg-emerald-400/10 text-white shadow-[0_20px_45px_-30px_rgba(16,185,129,0.9)]'
                        : 'border-white/8 bg-white/[0.03] text-slate-300 hover:border-white/12 hover:bg-white/[0.05]',
                ].join(' ')
            }
        >
            {({ isActive }) => (
                <>
                    <div
                        className={[
                            'flex h-10 w-10 items-center justify-center rounded-2xl',
                            isActive ? 'bg-emerald-300/15 text-emerald-100' : 'bg-white/[0.06] text-slate-200',
                        ].join(' ')}
                    >
                        <Icon className="h-5 w-5" />
                    </div>
                    <div className="min-w-0 flex-1">
                        <p className="font-semibold">{item.label}</p>
                        <p className="mt-0.5 text-xs text-slate-400">{item.description}</p>
                    </div>
                    <ChevronRight className="h-4 w-4 text-slate-500" />
                </>
            )}
        </NavLink>
    )
}

function SidebarContent({
    user,
    onNavigate,
}: {
    user: SuperAdminIdentity | null
    onNavigate?: () => void
}) {
    const visibleNavigationItems = navigationItems.filter(
        (item) => !item.capability || hasSuperAdminNavigationCapability(user, item.capability),
    )

    return (
        <div className="flex h-full flex-col">
            <div className="border-b border-white/10 px-5 py-6">
                <div className="flex items-center gap-3">
                    <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-gradient-to-br from-emerald-400 to-cyan-400 text-slate-950 shadow-lg shadow-emerald-400/20">
                        <Shield className="h-6 w-6" />
                    </div>
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.24em] text-emerald-200">
                            CommerceHub
                        </p>
                        <h1 className="text-lg font-semibold text-white">Super Admin</h1>
                    </div>
                </div>

                <div className="mt-5 rounded-3xl border border-white/10 bg-white/[0.04] p-4">
                    <p className="text-xs uppercase tracking-[0.22em] text-slate-400">
                        Control Surface
                    </p>
                    <p className="mt-2 text-sm leading-6 text-slate-200">
                        Platform-wide command center for finance, operations, and exception handling.
                    </p>
                </div>
            </div>

            <nav className="flex-1 space-y-3 overflow-y-auto px-4 py-5">
                {visibleNavigationItems.map((item) => (
                    <NavigationLink key={item.label} item={item} onNavigate={onNavigate} />
                ))}
            </nav>
        </div>
    )
}

export default function SuperAdminLayout() {
    const location = useLocation()
    const navigate = useNavigate()
    const { user, logout } = useSuperAdminAuthStore()
    const [isSidebarOpen, setIsSidebarOpen] = useState(false)

    const pageLabel = useMemo(() => {
        if (location.pathname.includes('/finance')) {
            return 'Finance'
        }

        if (location.pathname.includes('/orders')) {
            return 'Orders'
        }

        if (location.pathname.includes('/reviews')) {
            return 'Reviews'
        }

        if (location.pathname.includes('/users')) {
            return 'Users'
        }

        if (location.pathname.includes('/marketplace/governance')) {
            return 'Marketplace Governance'
        }

        if (location.pathname.includes('/payments')) {
            return 'Payments'
        }

        if (location.pathname.includes('/payment-providers')) {
            return 'Payment Providers'
        }

        if (location.pathname.includes('/payouts')) {
            return 'Payouts'
        }

        if (location.pathname.includes('/refunds')) {
            return 'Refunds'
        }

        if (location.pathname.includes('/commission')) {
            return 'Commission'
        }

        if (location.pathname.includes('/promo-codes')) {
            return 'Promo Codes'
        }

        if (location.pathname.includes('/homepage')) {
            return 'Homepage'
        }

        if (location.pathname.includes('/storefront/footer')) {
            return 'Storefront Defaults'
        }

        if (location.pathname.includes('/marketplace/pages')) {
            return 'Marketplace CMS'
        }

        if (location.pathname.includes('/marketplace/footer')) {
            return 'Marketplace Footer'
        }

        return 'Console'
    }, [location.pathname])

    function handleLogout() {
        logoutSuperAdmin()
            .catch(() => {})
            .finally(() => {
                logout()
                navigate('/super-admin/login', { replace: true })
            })
    }

    return (
        <div className="min-h-screen bg-[radial-gradient(circle_at_top,_rgba(16,185,129,0.1),_transparent_32%),linear-gradient(180deg,#07111f_0%,#0f172a_18%,#f5f7fb_18%,#f8fafc_100%)]">
            <div className="relative flex min-h-screen">
                <aside className="hidden w-80 border-r border-white/10 bg-slate-950/95 backdrop-blur xl:block">
                    <SidebarContent user={user} />
                </aside>

                {isSidebarOpen ? (
                    <div className="fixed inset-0 z-40 xl:hidden">
                        <button
                            type="button"
                            aria-label="Close sidebar"
                            className="absolute inset-0 bg-slate-950/60 backdrop-blur-sm"
                            onClick={() => setIsSidebarOpen(false)}
                        />
                        <aside className="relative h-full w-[86%] max-w-sm border-r border-white/10 bg-slate-950/95 shadow-2xl shadow-slate-950/60">
                            <div className="flex items-center justify-end px-4 pt-4">
                                <button
                                    type="button"
                                    className="flex h-10 w-10 items-center justify-center rounded-2xl border border-white/10 bg-white/[0.04] text-slate-200"
                                    onClick={() => setIsSidebarOpen(false)}
                                >
                                    <X className="h-5 w-5" />
                                </button>
                            </div>
                            <SidebarContent user={user} onNavigate={() => setIsSidebarOpen(false)} />
                        </aside>
                    </div>
                ) : null}

                <div className="flex min-w-0 flex-1 flex-col">
                    <header className="sticky top-0 z-30 border-b border-slate-200/80 bg-white/90 backdrop-blur">
                        <div className="mx-auto flex max-w-[1440px] items-center justify-between gap-4 px-4 py-4 sm:px-6 lg:px-8">
                            <div className="flex items-center gap-3">
                                <button
                                    type="button"
                                    className="flex h-11 w-11 items-center justify-center rounded-2xl border border-slate-200 bg-white text-slate-700 shadow-sm xl:hidden"
                                    onClick={() => setIsSidebarOpen(true)}
                                >
                                    <Menu className="h-5 w-5" />
                                </button>

                                <div>
                                    <p className="text-xs font-semibold uppercase tracking-[0.24em] text-emerald-600">
                                        {pageLabel}
                                    </p>
                                    <h2 className="text-xl font-semibold text-slate-950">
                                        Super Admin Dashboard
                                    </h2>
                                </div>
                            </div>

                            <div className="flex items-center gap-3">
                                <div className="hidden rounded-2xl border border-slate-200 bg-white px-4 py-3 shadow-sm sm:block">
                                    <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-slate-500">
                                        Super Admin
                                    </p>
                                    <p className="mt-1 text-sm font-medium text-slate-900">
                                        {user?.email ?? 'owner@commercehub'}
                                    </p>
                                </div>

                                <button
                                    type="button"
                                    className="inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-4 py-3 text-sm font-semibold text-white shadow-lg shadow-slate-900/15 transition hover:bg-slate-800"
                                    onClick={handleLogout}
                                >
                                    <LogOut className="h-4 w-4" />
                                    Logout
                                </button>
                            </div>
                        </div>
                    </header>

                    <main className="flex-1 px-4 py-6 sm:px-6 lg:px-8">
                        <div className="mx-auto max-w-[1440px]">
                            <Outlet />
                        </div>
                    </main>
                </div>
            </div>
        </div>
    )
}

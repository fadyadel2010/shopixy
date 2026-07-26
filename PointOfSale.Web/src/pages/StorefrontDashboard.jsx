/**
 * Storefront Dashboard
 * Phase 9.8: POS Integration - Unified Admin Experience
 *
 * Overview page for storefront settings and quick actions
 */

import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
    ArrowRight,
    FileText,
    Grid,
    History,
    Home,
    Link2,
    Palette,
    Share2,
    ShoppingBag,
} from 'lucide-react'
import StorefrontStatusCard from '../components/StorefrontStatusCard'
import WishlistAnalyticsCard from '../components/WishlistAnalyticsCard'
import { hasCommerceHubVendorNavigationCapability } from '../auth/posVendorSessionAuthority'
import { useAuthStore } from '../store/authStore'

const PAGE_CARDS = [
    {
        key: 'home',
        route: '/storefront/builder?page=home',
        icon: Home,
        labelEn: 'Home Page',
        labelAr: '\u0627\u0644\u0635\u0641\u062D\u0629 \u0627\u0644\u0631\u0626\u064A\u0633\u064A\u0629',
        descEn: 'Customize your storefront homepage with hero banners, featured products, and more.',
        descAr: '\u062E\u0635\u0635 \u0627\u0644\u0635\u0641\u062D\u0629 \u0627\u0644\u0631\u0626\u064A\u0633\u064A\u0629 \u0644\u0645\u062A\u062C\u0631\u0643 \u0628\u0627\u0644\u0628\u0627\u0646\u0631\u0627\u062A \u0648\u0627\u0644\u0645\u0646\u062A\u062C\u0627\u062A \u0627\u0644\u0645\u0645\u064A\u0632\u0629 \u0648\u0627\u0644\u0645\u0632\u064A\u062F.',
    },
    {
        key: 'product',
        route: '/storefront/builder?page=product',
        icon: ShoppingBag,
        labelEn: 'Product Page',
        labelAr: '\u0635\u0641\u062D\u0629 \u0627\u0644\u0645\u0646\u062A\u062C',
        descEn: 'Design how individual products are displayed to customers.',
        descAr: '\u0635\u0645\u0645 \u0637\u0631\u064A\u0642\u0629 \u0639\u0631\u0636 \u0627\u0644\u0645\u0646\u062A\u062C\u0627\u062A \u0627\u0644\u0641\u0631\u062F\u064A\u0629 \u0644\u0644\u0639\u0645\u0644\u0627\u0621.',
    },
    {
        key: 'category',
        route: '/storefront/builder?page=category',
        icon: Grid,
        labelEn: 'Category Page',
        labelAr: '\u0635\u0641\u062D\u0629 \u0627\u0644\u062A\u0635\u0646\u064A\u0641',
        descEn: 'Customize category listings and product grids.',
        descAr: '\u062E\u0635\u0635 \u0642\u0648\u0627\u0626\u0645 \u0627\u0644\u062A\u0635\u0646\u064A\u0641\u0627\u062A \u0648\u0634\u0628\u0643\u0627\u062A \u0627\u0644\u0645\u0646\u062A\u062C\u0627\u062A.',
    },
]

export default function StorefrontDashboard() {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const navigate = useNavigate()
    const user = useAuthStore((state) => state.user)
    const canViewAnalytics = hasCommerceHubVendorNavigationCapability(user, 'commercehub.analytics.view')
    const canManageCms = hasCommerceHubVendorNavigationCapability(user, 'commercehub.storefront.cms.manage')

    return (
        <div className="mx-auto max-w-6xl">
            <div className="mb-8">
                <h1 className="text-2xl font-bold text-gray-900">
                    {isRTL ? '\u0625\u0639\u062F\u0627\u062F\u0627\u062A \u0627\u0644\u0645\u062A\u062C\u0631 \u0627\u0644\u0625\u0644\u0643\u062A\u0631\u0648\u0646\u064A' : 'Storefront Settings'}
                </h1>
                <p className="mt-1 text-gray-600">
                    {isRTL
                        ? '\u062A\u062E\u0635\u064A\u0635 \u0645\u0638\u0647\u0631 \u0648\u062A\u0635\u0645\u064A\u0645 \u0645\u062A\u062C\u0631\u0643 \u0627\u0644\u0625\u0644\u0643\u062A\u0631\u0648\u0646\u064A'
                        : 'Customize the appearance and design of your online store'}
                </p>
            </div>

            <div className="mb-8 grid grid-cols-1 gap-6 md:grid-cols-2">
                <div className="h-full">
                    <StorefrontStatusCard />
                </div>

                {canViewAnalytics ? (
                    <div className="h-full">
                        <WishlistAnalyticsCard />
                    </div>
                ) : null}
            </div>

            <div className="mb-8">
                <h2 className="mb-4 text-lg font-semibold text-gray-900">
                    {isRTL ? '\u062A\u062E\u0635\u064A\u0635 \u0627\u0644\u0635\u0641\u062D\u0627\u062A' : 'Customize Pages'}
                </h2>
                <div className="grid grid-cols-1 gap-6 md:grid-cols-3">
                    {PAGE_CARDS.map((page) => {
                        const Icon = page.icon
                        return (
                            <button
                                key={page.key}
                                onClick={() => navigate(page.route)}
                                className="group rounded-xl border border-gray-200 bg-white p-6 text-left transition-all hover:border-indigo-300 hover:shadow-md"
                            >
                                <div className="mb-4 flex h-12 w-12 items-center justify-center rounded-xl bg-indigo-100 transition-colors group-hover:bg-indigo-600">
                                    <Icon size={24} className="text-indigo-600 transition-colors group-hover:text-white" />
                                </div>
                                <h3 className="mb-2 flex items-center justify-between text-lg font-semibold text-gray-900">
                                    {isRTL ? page.labelAr : page.labelEn}
                                    <ArrowRight size={20} className={`text-gray-400 transition-colors group-hover:text-indigo-600 ${isRTL ? 'rotate-180' : ''}`} />
                                </h3>
                                <p className="text-sm text-gray-500">
                                    {isRTL ? page.descAr : page.descEn}
                                </p>
                            </button>
                        )
                    })}
                </div>
            </div>

            <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
                <div className="rounded-xl border border-gray-200 bg-white p-6">
                    <div className="mb-4 flex items-center gap-4">
                        <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-purple-100">
                            <Palette size={24} className="text-purple-600" />
                        </div>
                        <div>
                            <h3 className="text-lg font-semibold text-gray-900">
                                {isRTL ? '\u0627\u0644\u0623\u0644\u0648\u0627\u0646 \u0648\u0627\u0644\u062E\u0637\u0648\u0637' : 'Theme & Colors'}
                            </h3>
                            <p className="text-sm text-gray-500">
                                {isRTL ? '\u062A\u062E\u0635\u064A\u0635 \u0623\u0644\u0648\u0627\u0646 \u0648\u062E\u0637\u0648\u0637 \u0627\u0644\u0645\u062A\u062C\u0631' : 'Customize store colors and fonts'}
                            </p>
                        </div>
                    </div>
                    <button
                        onClick={() => navigate('/storefront/themes')}
                        className="w-full rounded-lg border border-purple-200 py-2 text-sm font-medium text-purple-600 transition-colors hover:bg-purple-50"
                    >
                        {isRTL ? '\u062A\u0639\u062F\u064A\u0644 \u0627\u0644\u0645\u0638\u0647\u0631' : 'Edit Theme'}
                    </button>
                </div>

                <div className="rounded-xl border border-gray-200 bg-white p-6">
                    <div className="mb-4 flex items-center gap-4">
                        <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-orange-100">
                            <History size={24} className="text-orange-600" />
                        </div>
                        <div>
                            <h3 className="text-lg font-semibold text-gray-900">
                                {isRTL ? '\u0633\u062C\u0644 \u0627\u0644\u0625\u0635\u062F\u0627\u0631\u0627\u062A' : 'Version History'}
                            </h3>
                            <p className="text-sm text-gray-500">
                                {isRTL ? '\u0639\u0631\u0636 \u0648\u0627\u0633\u062A\u0639\u0627\u062F\u0629 \u0627\u0644\u0625\u0635\u062F\u0627\u0631\u0627\u062A \u0627\u0644\u0633\u0627\u0628\u0642\u0629' : 'View and restore previous versions'}
                            </p>
                        </div>
                    </div>
                    <button
                        onClick={() => navigate('/storefront/versions')}
                        className="w-full rounded-lg border border-orange-200 py-2 text-sm font-medium text-orange-600 transition-colors hover:bg-orange-50"
                    >
                        {isRTL ? '\u0639\u0631\u0636 \u0627\u0644\u0633\u062C\u0644' : 'View History'}
                    </button>
                </div>
            </div>

            {canManageCms ? (
            <div className="mt-8">
                <h2 className="mb-4 text-lg font-semibold text-gray-900">
                    {isRTL ? '\u0625\u062F\u0627\u0631\u0629 \u0627\u0644\u0645\u062D\u062A\u0648\u0649' : 'Content Management'}
                </h2>
                <div className="grid grid-cols-1 gap-6 md:grid-cols-2 xl:grid-cols-3">
                    <div className="rounded-xl border border-gray-200 bg-white p-6">
                        <div className="mb-4 flex items-center gap-4">
                            <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-emerald-100">
                                <FileText size={24} className="text-emerald-600" />
                            </div>
                            <div>
                                <h3 className="text-lg font-semibold text-gray-900">
                                    {isRTL ? '\u0635\u0641\u062D\u0627\u062A \u0627\u0644\u0645\u062D\u062A\u0648\u0649' : 'CMS Pages'}
                                </h3>
                                <p className="text-sm text-gray-500">
                                    {isRTL ? '\u0625\u062F\u0627\u0631\u0629 \u0635\u0641\u062D\u0627\u062A \u0645\u0646 \u0646\u062D\u0646\u060C \u0627\u062A\u0635\u0644 \u0628\u0646\u0627\u060C \u0627\u0644\u062E\u0635\u0648\u0635\u064A\u0629' : 'Manage About, Contact, Privacy pages'}
                                </p>
                            </div>
                        </div>
                        <button
                            onClick={() => navigate('/storefront/cms/pages')}
                            className="w-full rounded-lg border border-emerald-200 py-2 text-sm font-medium text-emerald-600 transition-colors hover:bg-emerald-50"
                        >
                            {isRTL ? '\u0625\u062F\u0627\u0631\u0629 \u0627\u0644\u0635\u0641\u062D\u0627\u062A' : 'Manage Pages'}
                        </button>
                    </div>

                    <div className="rounded-xl border border-gray-200 bg-white p-6">
                        <div className="mb-4 flex items-center gap-4">
                            <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-sky-100">
                                <Link2 size={24} className="text-sky-600" />
                            </div>
                            <div>
                                <h3 className="text-lg font-semibold text-gray-900">
                                    {isRTL ? '\u0642\u0648\u0627\u0626\u0645 \u0627\u0644\u062A\u0646\u0642\u0644' : 'Navigation'}
                                </h3>
                                <p className="text-sm text-gray-500">
                                    {isRTL ? '\u062A\u0639\u062F\u064A\u0644 \u0631\u0648\u0627\u0628\u0637 \u0627\u0644\u0647\u064A\u062F\u0631 \u0648\u0627\u0644\u0641\u0648\u062A\u0631' : 'Edit header and footer links'}
                                </p>
                            </div>
                        </div>
                        <button
                            onClick={() => navigate('/storefront/cms/navigation')}
                            className="w-full rounded-lg border border-sky-200 py-2 text-sm font-medium text-sky-600 transition-colors hover:bg-sky-50"
                        >
                            {isRTL ? '\u062A\u0639\u062F\u064A\u0644 \u0627\u0644\u0642\u0648\u0627\u0626\u0645' : 'Edit Navigation'}
                        </button>
                    </div>

                    <div className="rounded-xl border border-gray-200 bg-white p-6">
                        <div className="mb-4 flex items-center gap-4">
                            <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-violet-100">
                                <Share2 size={24} className="text-violet-600" />
                            </div>
                            <div>
                                <h3 className="text-lg font-semibold text-gray-900">
                                    {isRTL ? '\u0627\u0644\u0641\u0648\u062A\u0631 \u0648\u0627\u0644\u062A\u0648\u0627\u0635\u0644' : 'Footer & Social'}
                                </h3>
                                <p className="text-sm text-gray-500">
                                    {isRTL ? '\u0625\u062F\u0627\u0631\u0629 \u0623\u0642\u0633\u0627\u0645 \u0627\u0644\u0641\u0648\u062A\u0631 \u0648\u0631\u0648\u0627\u0628\u0637 \u0635\u0641\u062D\u0627\u062A \u0627\u0644\u0645\u062D\u062A\u0648\u0649 \u0648\u062D\u0633\u0627\u0628\u0627\u062A \u0627\u0644\u062A\u0648\u0627\u0635\u0644' : 'Manage footer sections, CMS links, and social profiles'}
                                </p>
                            </div>
                        </div>
                        <button
                            onClick={() => navigate('/storefront/cms/footer')}
                            className="w-full rounded-lg border border-violet-200 py-2 text-sm font-medium text-violet-600 transition-colors hover:bg-violet-50"
                        >
                            {isRTL ? '\u062A\u062D\u0631\u064A\u0631 \u0627\u0644\u0641\u0648\u062A\u0631' : 'Edit Footer'}
                        </button>
                    </div>
                </div>
            </div>
            ) : null}

            <div className="mt-8 rounded-xl border border-gray-200 bg-gray-50 p-6">
                <h3 className="mb-2 text-lg font-semibold text-gray-900">
                    {isRTL ? '\u0643\u064A\u0641\u064A\u0629 \u062A\u062E\u0635\u064A\u0635 \u0645\u062A\u062C\u0631\u0643' : 'How to Customize Your Store'}
                </h3>
                <ul className="space-y-2 text-sm text-gray-600">
                    <li className="flex items-start gap-2">
                        <span className="flex h-5 w-5 flex-shrink-0 items-center justify-center rounded-full bg-indigo-100 text-xs font-semibold text-indigo-600">1</span>
                        {isRTL
                            ? '\u0627\u062E\u062A\u0631 \u0627\u0644\u0635\u0641\u062D\u0629 \u0627\u0644\u062A\u064A \u062A\u0631\u064A\u062F \u062A\u0639\u062F\u064A\u0644\u0647\u0627 \u0645\u0646 \u0627\u0644\u0623\u0639\u0644\u0649'
                            : 'Click on the page you want to customize above'}
                    </li>
                    <li className="flex items-start gap-2">
                        <span className="flex h-5 w-5 flex-shrink-0 items-center justify-center rounded-full bg-indigo-100 text-xs font-semibold text-indigo-600">2</span>
                        {isRTL
                            ? '\u0627\u0633\u062D\u0628 \u0627\u0644\u0623\u0642\u0633\u0627\u0645 \u0645\u0646 \u0627\u0644\u0644\u0648\u062D\u0629 \u0627\u0644\u062C\u0627\u0646\u0628\u064A\u0629 \u0625\u0644\u0649 \u0627\u0644\u0635\u0641\u062D\u0629'
                            : 'Drag sections from the palette onto the canvas'}
                    </li>
                    <li className="flex items-start gap-2">
                        <span className="flex h-5 w-5 flex-shrink-0 items-center justify-center rounded-full bg-indigo-100 text-xs font-semibold text-indigo-600">3</span>
                        {isRTL
                            ? '\u0627\u0636\u063A\u0637 \u0639\u0644\u0649 \u0642\u0633\u0645 \u0644\u062A\u0639\u062F\u064A\u0644 \u0625\u0639\u062F\u0627\u062F\u0627\u062A\u0647'
                            : 'Click on a section to edit its settings'}
                    </li>
                    <li className="flex items-start gap-2">
                        <span className="flex h-5 w-5 flex-shrink-0 items-center justify-center rounded-full bg-indigo-100 text-xs font-semibold text-indigo-600">4</span>
                        {isRTL
                            ? '\u0627\u0636\u063A\u0637 "\u0646\u0634\u0631" \u0644\u062A\u0637\u0628\u064A\u0642 \u0627\u0644\u062A\u063A\u064A\u064A\u0631\u0627\u062A \u0639\u0644\u0649 \u0627\u0644\u0645\u062A\u062C\u0631 \u0627\u0644\u0645\u0628\u0627\u0634\u0631'
                            : 'Click "Publish" to apply changes to your live store'}
                    </li>
                </ul>
            </div>
        </div>
    )
}

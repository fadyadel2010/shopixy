/**
 * Application Router
 * 
 * React Router configuration for storefront pages.
 * Phase 10.1: Multi-language URL routing with /ar/ and /en/ prefixes
 */

import { Navigate, createBrowserRouter, Outlet, useLocation } from 'react-router-dom';
import { useEffect } from 'react';
import {
    HomePage,
    ProductListPage,
    ProductDetailsPage,
    CheckoutPendingPage,
    OrderConfirmationPage,
    OrderTrackingPage,
    CmsPage,
    CategoryPage,
    AffiliateLandingPage,
    RecoverCheckoutPage,
    RecoverCartPage,
} from '@/pages';
import {
    LoginPage,
    RegisterPage,
    ForgotPasswordPage,
    ResetPasswordPage,
    VerifyEmailPage,
} from '@/auth/pages';
import { Layout } from '@/components/layout'; // AF-27E
import { useLanguageStore, type SupportedLanguage } from '@/i18n';
// PHASE 6: Commerce AI Chat Widget
import { ChatWidget } from '@/components/chat';
import { ThemePageRoute } from '@/storefront/pages/ThemePageRoute';

// ============================================================================
// Root Layout
// ============================================================================

/**
 * Root layout that wraps all pages.
 * Provides global components that need Router context (e.g., ChatWidget).
 */
const RootLayout: React.FC = () => {
    return (
        <>
            <Outlet />
            <ChatWidget />
        </>
    );
};

// ============================================================================
// Language Wrapper Component
// ============================================================================

/**
 * Wraps routes to detect language from URL and apply it.
 * Supports both /ar/... and /en/... URL prefixes.
 */
interface LanguageWrapperProps {
    lang: SupportedLanguage;
}

const LanguageWrapper: React.FC<LanguageWrapperProps> = ({ lang }) => {
    const setLanguage = useLanguageStore((state) => state.setLanguage);
    const currentLanguage = useLanguageStore((state) => state.language);

    useEffect(() => {
        if (lang !== currentLanguage) {
            setLanguage(lang);
        }
    }, [lang, currentLanguage, setLanguage]);

    return <Outlet />;
};

const LegacyCheckoutSuccessRedirect: React.FC = () => {
    const location = useLocation();
    const pendingPath = location.pathname.replace(/\/checkout\/success$/, '/checkout/pending');

    return <Navigate replace to={{ pathname: pendingPath, search: location.search }} />;
};

// ============================================================================
// Route Definitions
// ============================================================================

/**
 * Core page routes (used in both language-prefixed and non-prefixed versions)
 */
const pageRoutes = [
    {
        index: true,
        element: <HomePage />,
    },
    {
        path: 'products',
        element: <ProductListPage />,
    },
    {
        path: 'products/:slug',
        element: <ProductDetailsPage />,
    },
    // Categories
    {
        path: 'category',
        element: <CategoryPage />,
    },
    {
        path: 'category/:slug',
        element: <ProductListPage />,
    },
    {
        path: 'cart',
        element: <ThemePageRoute pageName="cart" />,
    },
    {
        path: 'checkout',
        element: <ThemePageRoute pageName="checkout" />,
    },
    {
        path: 'checkout/pending',
        element: <CheckoutPendingPage />,
    },
    {
        path: 'checkout/success',
        element: <LegacyCheckoutSuccessRedirect />,
    },
    {
        path: 'orders/:orderId/confirmation',
        element: <OrderConfirmationPage />,
    },
    {
        path: 'orders/:orderId/track',
        element: <OrderTrackingPage />,
    },
    // CMS Pages (Phase 9.11)
    {
        path: 'pages/:slug',
        element: <CmsPage />,
    },
    {
        path: ':slug',
        element: <CmsPage />,
    },
    // Search (Unified Search - Elasticsearch)
    {
        path: 'search',
        element: <ThemePageRoute pageName="search" />,
    },
];

// ============================================================================
// Router Configuration
// ============================================================================

export const router = createBrowserRouter([
    {
        element: <RootLayout />,
        children: [
            // Non-prefixed routes (default language from store/detection)
            {
                path: '/',
                element: <HomePage />,
            },
            {
                path: '/products',
                element: <ProductListPage />,
            },
            {
                path: '/products/:slug',
                element: <ProductDetailsPage />,
            },
            {
                path: '/category',
                element: <CategoryPage />,
            },
            {
                path: '/category/:slug',
                element: <ProductListPage />,
            },
            {
                path: '/cart',
                element: <ThemePageRoute pageName="cart" />,
            },
            {
                path: '/checkout',
                element: <ThemePageRoute pageName="checkout" />,
            },
            {
                path: '/checkout/pending',
                element: <CheckoutPendingPage />,
            },
            {
                path: '/checkout/success',
                element: <LegacyCheckoutSuccessRedirect />,
            },
            {
                path: '/orders/:orderId/confirmation',
                element: <OrderConfirmationPage />,
            },
            {
                path: '/orders/:orderId/track',
                element: <OrderTrackingPage />,
            },
            {
                path: '/pages/:slug',
                element: <CmsPage />,
            },
            {
                path: '/:slug',
                element: <CmsPage />,
            },
            {
                path: '/search',
                element: <ThemePageRoute pageName="search" />,
            },
            // Auth routes (Phase F1)
            {
                path: '/login',
                element: <LoginPage />,
            },
            {
                path: '/register',
                element: <RegisterPage />,
            },
            {
                path: '/forgot-password',
                element: <ForgotPasswordPage />,
            },
            {
                path: '/reset-password',
                element: <ResetPasswordPage />,
            },
            // Email verification route
            {
                path: '/verify-email',
                element: <VerifyEmailPage />,
            },
            // Protected routes (Phase F1.1)
            {
                path: '/account',
                element: <ThemePageRoute pageName="account" />,
            },
            // Wishlist route (Phase 10.3.6)
            {
                path: '/wishlist',
                element: <ThemePageRoute pageName="wishlist" />,
            },
            // Abandoned Checkout Recovery Link
            {
                path: '/recover-checkout/:token',
                element: <RecoverCheckoutPage />,
            },
            {
                path: '/recover-cart/:token',
                element: <RecoverCartPage />,
            },
            // AF-27A: Affiliate Landing Page route
            // MUST be before /:lang to prevent language wrapper from intercepting
            // AF-27E: Wrapped in Layout to show consistent banner
            {
                path: '/p/:referralCode/:slug', // AF-27E
                element: (
                    <Layout>
                        <AffiliateLandingPage />
                    </Layout>
                ),
            },
            // Language-prefixed routes (/ar/... and /en/...)
            {
                path: '/ar',
                element: <LanguageWrapper lang="ar" />,
                children: pageRoutes,
            },
            {
                path: '/en',
                element: <LanguageWrapper lang="en" />,
                children: pageRoutes,
            },
        ],
    },
]);

// ============================================================================
// Helper Functions
// ============================================================================

/**
 * Generates a language-prefixed URL
 * @param path - The base path (e.g., '/products')
 * @param lang - Target language ('en' or 'ar')
 * @returns Language-prefixed URL (e.g., '/ar/products')
 */
export function getLocalizedUrl(path: string, lang: SupportedLanguage): string {
    // Remove leading slash if present
    const cleanPath = path.startsWith('/') ? path.slice(1) : path;
    return `/${lang}/${cleanPath}`;
}

/**
 * Gets the current URL with a different language
 * @param lang - Target language
 * @returns URL with new language prefix
 */
export function switchUrlLanguage(currentPath: string, newLang: SupportedLanguage): string {
    // Check if current path has a language prefix
    const langPrefixMatch = currentPath.match(/^\/(ar|en)(\/.*)?$/);

    if (langPrefixMatch) {
        // Replace existing language prefix
        const restOfPath = langPrefixMatch[2] || '';
        return `/${newLang}${restOfPath}`;
    } else {
        // Add language prefix to non-prefixed path
        return `/${newLang}${currentPath}`;
    }
}


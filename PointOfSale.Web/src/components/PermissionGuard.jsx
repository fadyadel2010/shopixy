/**
 * Permission Guard Component
 * Phase 9.9: Storefront Admin Completion
 * 
 * Enforces role-based access at UI level:
 * - Storefront pages: Owner / Admin only
 * - Graceful error UI on 403 responses
 */

import { useTranslation } from 'react-i18next'
import { ShieldAlert, Lock } from 'lucide-react'
import { useAuthStore } from '../store/authStore'

/**
 * Check if user has required role
 */
export function useHasPermission(requiredRoles = []) {
    const user = useAuthStore((s) => s.user)

    if (!user || !requiredRoles.length) return true

    const userRole = user.role
    return requiredRoles.includes(userRole)
}

/**
 * Check if user can access storefront admin
 */
export function useCanAccessStorefront() {
    return useHasPermission(['Owner', 'Admin'])
}

/**
 * Check if user's email is verified
 */
export function useIsEmailVerified() {
    const user = useAuthStore((s) => s.user)
    return user?.emailVerified ?? false
}

/**
 * Permission Guard Wrapper
 */
export function PermissionGuard({
    requiredRoles = [],
    requireEmailVerified = false,
    children,
    fallback = null,
}) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const user = useAuthStore((s) => s.user)

    const hasPermission = useHasPermission(requiredRoles)
    const isEmailVerified = user?.emailVerified ?? false

    // Check email verification if required
    if (requireEmailVerified && !isEmailVerified) {
        if (fallback) return fallback

        return (
            <div className="max-w-2xl mx-auto py-12">
                <div className="text-center p-8 bg-yellow-50 border border-yellow-200 rounded-2xl">
                    <div className="w-16 h-16 bg-yellow-100 rounded-2xl flex items-center justify-center mx-auto mb-4">
                        <Lock size={32} className="text-yellow-600" />
                    </div>
                    <h2 className="text-xl font-bold text-gray-900 mb-2">
                        {isRTL ? 'التحقق من البريد مطلوب' : 'Email Verification Required'}
                    </h2>
                    <p className="text-gray-600">
                        {isRTL
                            ? 'يرجى التحقق من بريدك الإلكتروني للوصول إلى هذه الميزة.'
                            : 'Please verify your email address to access this feature.'
                        }
                    </p>
                </div>
            </div>
        )
    }

    // Check role permission
    if (!hasPermission) {
        if (fallback) return fallback

        return (
            <div className="max-w-2xl mx-auto py-12">
                <div className="text-center p-8 bg-red-50 border border-red-200 rounded-2xl">
                    <div className="w-16 h-16 bg-red-100 rounded-2xl flex items-center justify-center mx-auto mb-4">
                        <ShieldAlert size={32} className="text-red-600" />
                    </div>
                    <h2 className="text-xl font-bold text-gray-900 mb-2">
                        {isRTL ? 'غير مصرح بالوصول' : 'Access Denied'}
                    </h2>
                    <p className="text-gray-600 mb-4">
                        {isRTL
                            ? 'ليس لديك الصلاحيات الكافية للوصول إلى هذه الصفحة.'
                            : 'You do not have permission to access this page.'
                        }
                    </p>
                    <p className="text-sm text-gray-500">
                        {isRTL
                            ? 'يتطلب الوصول دور: '
                            : 'Required role: '
                        }
                        <span className="font-medium">{requiredRoles.join(' / ')}</span>
                    </p>
                </div>
            </div>
        )
    }

    return children
}

/**
 * Storefront Guard - Shorthand for storefront pages
 */
export function StorefrontGuard({ children }) {
    return (
        <PermissionGuard requiredRoles={['Owner', 'Admin']}>
            {children}
        </PermissionGuard>
    )
}

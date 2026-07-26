import { useEffect, useRef } from 'react'
import api from '../services/api'
import { clearLegacyPosTokenStorage, useAuthStore } from '../store/authStore'
import { queryClient } from '../queryClient'
import {
    POS_VENDOR_SESSION_AUTHORITY_LOST_EVENT,
    POS_VENDOR_SESSION_LOGOUT_STORAGE_KEY,
    requirePosVendorSessionUserAuthority,
} from '../auth/posVendorSessionAuthority'
import { invalidatePosRuntimeCache } from '../services/posRuntimeCache'

const watchedKeys = new Set([POS_VENDOR_SESSION_LOGOUT_STORAGE_KEY])
const SESSION_REVALIDATION_INTERVAL_MS = 60000

export default function AuthBootstrap() {
    const hydrateSession = useAuthStore((state) => state.hydrateSession)
    const logout = useAuthStore((state) => state.logout)
    const setHasHydrated = useAuthStore((state) => state.setHasHydrated)
    const beginRouteAuthorityValidation = useAuthStore((state) => state.beginRouteAuthorityValidation)
    const isBootstrapping = useRef(false)
    const debounceTimer = useRef(null)
    const latestBootstrapRequestId = useRef(0)

    useEffect(() => {
        let disposed = false

        const purgeCommerceHubVendorAuthority = () => {
            queryClient.removeQueries({
                predicate: (query) => {
                    const [root, , , domain] = query.queryKey ?? []

                    return root === 'commercehub-vendor-session' ||
                        root === 'commercehub-finance' ||
                        root === 'commercehub-earnings-dashboard' ||
                        root === 'commercehub-payout-methods' ||
                        root === 'commercehub-payments-dashboard' ||
                        domain === 'commercehub-finance' ||
                        domain === 'commercehub-earnings-dashboard' ||
                        domain === 'commercehub-payout-methods' ||
                        domain === 'commercehub-payments-dashboard'
                },
            })
        }

        const bootstrapSession = async ({ withholdRoutePresentation = false } = {}) => {
            if (isBootstrapping.current) {
                return
            }

            if (withholdRoutePresentation) {
                beginRouteAuthorityValidation()
            }

            isBootstrapping.current = true
            const bootstrapRequestId = ++latestBootstrapRequestId.current
            const sessionRevisionAtStart = JSON.stringify({
                userId: useAuthStore.getState().user?.id ?? null,
                isAuthenticated: useAuthStore.getState().isAuthenticated,
            })
            clearLegacyPosTokenStorage()

            try {
                const response = await api.get('/auth/session')

                if (!disposed && bootstrapRequestId === latestBootstrapRequestId.current) {
                    const sessionRevisionChanged = JSON.stringify({
                        userId: useAuthStore.getState().user?.id ?? null,
                        isAuthenticated: useAuthStore.getState().isAuthenticated,
                    }) !== sessionRevisionAtStart

                    if (sessionRevisionChanged) {
                        return
                    }

                    const nextUser = requirePosVendorSessionUserAuthority(response.data?.user ?? null)
                    hydrateSession(nextUser)
                }
            } catch {
                const sessionRevisionChanged = JSON.stringify({
                    userId: useAuthStore.getState().user?.id ?? null,
                    isAuthenticated: useAuthStore.getState().isAuthenticated,
                }) !== sessionRevisionAtStart

                if (!disposed
                    && bootstrapRequestId === latestBootstrapRequestId.current
                    && !sessionRevisionChanged) {
                    hydrateSession(null)
                }
            } finally {
                if (!disposed && bootstrapRequestId === latestBootstrapRequestId.current) {
                    setHasHydrated(true)
                }
                isBootstrapping.current = false
            }
        }

        void bootstrapSession()

        const clearAuthority = () => {
            latestBootstrapRequestId.current += 1
            clearLegacyPosTokenStorage()
            purgeCommerceHubVendorAuthority()
            logout()
        }

        const revalidateCommerceHubVendorAuthority = () => {
            purgeCommerceHubVendorAuthority()
            void bootstrapSession({ withholdRoutePresentation: true })
        }

        const unsubscribeFromActorTransitions = useAuthStore.subscribe((state, previousState) => {
            const currentScope = `${state.user?.tenantId ?? ''}:${state.user?.id ?? ''}:${state.user?.role ?? ''}:${state.isAuthenticated}`
            const previousScope = `${previousState.user?.tenantId ?? ''}:${previousState.user?.id ?? ''}:${previousState.user?.role ?? ''}:${previousState.isAuthenticated}`

            if (currentScope !== previousScope) {
                queryClient.clear()
                invalidatePosRuntimeCache({ reason: 'actor-scope-changed', broadcast: false })
            }
        })

        const revalidationInterval = window.setInterval(() => {
            void bootstrapSession()
        }, SESSION_REVALIDATION_INTERVAL_MS)

        const handleVisibility = () => {
            if (document.visibilityState === 'visible') {
                void bootstrapSession({ withholdRoutePresentation: true })
            }
        }

        const handleRouteLifecycle = () => {
            void bootstrapSession({ withholdRoutePresentation: true })
        }

        const handlePageShow = (event) => {
            if (event.persisted) {
                void bootstrapSession({ withholdRoutePresentation: true })
            }
        }

        const handleStorage = (event) => {
            if (event.key && watchedKeys.has(event.key)) {
                clearAuthority()
            }
        }

        window.addEventListener('storage', handleStorage)
        window.addEventListener('focus', handleRouteLifecycle)
        window.addEventListener('online', handleRouteLifecycle)
        window.addEventListener('popstate', handleRouteLifecycle)
        window.addEventListener('pageshow', handlePageShow)
        window.addEventListener(POS_VENDOR_SESSION_AUTHORITY_LOST_EVENT, revalidateCommerceHubVendorAuthority)
        document.addEventListener('visibilitychange', handleVisibility)

        return () => {
            disposed = true
            window.clearInterval(revalidationInterval)
            if (debounceTimer.current) {
                clearTimeout(debounceTimer.current)
            }
            window.removeEventListener('storage', handleStorage)
            window.removeEventListener('focus', handleRouteLifecycle)
            window.removeEventListener('online', handleRouteLifecycle)
            window.removeEventListener('popstate', handleRouteLifecycle)
            window.removeEventListener('pageshow', handlePageShow)
            window.removeEventListener(POS_VENDOR_SESSION_AUTHORITY_LOST_EVENT, revalidateCommerceHubVendorAuthority)
            document.removeEventListener('visibilitychange', handleVisibility)
            unsubscribeFromActorTransitions()
        }
    }, [beginRouteAuthorityValidation, hydrateSession, logout, setHasHydrated])

    return null
}

import { useEffect, useRef } from 'react'
import api, { isDefinitiveAuthFailure, requestPosSessionRefresh } from '../services/api'
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
    const isBootstrapping = useRef(false)
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

        const bootstrapSession = async () => {
            if (isBootstrapping.current) {
                return
            }

            isBootstrapping.current = true
            const bootstrapRequestId = ++latestBootstrapRequestId.current
            const hadAuthenticatedSession = useAuthStore.getState().isAuthenticated
            const sessionRevisionAtStart = JSON.stringify({
                userId: useAuthStore.getState().user?.id ?? null,
                isAuthenticated: useAuthStore.getState().isAuthenticated,
            })
            clearLegacyPosTokenStorage()

            const applySession = (user) => {
                if (disposed || bootstrapRequestId !== latestBootstrapRequestId.current) {
                    return
                }

                const sessionRevisionChanged = JSON.stringify({
                    userId: useAuthStore.getState().user?.id ?? null,
                    isAuthenticated: useAuthStore.getState().isAuthenticated,
                }) !== sessionRevisionAtStart

                if (!sessionRevisionChanged) {
                    hydrateSession(user)
                }
            }

            try {
                const response = await api.get('/auth/session')
                try {
                    applySession(requirePosVendorSessionUserAuthority(response.data?.user ?? null))
                } catch {
                    // A successful HTTP response with malformed authority is not a
                    // transient network failure. Fail closed without reloading.
                    applySession(null)
                }
            } catch (error) {
                if (error?.response?.status === 401) {
                    try {
                        const refreshedUser = await requestPosSessionRefresh()
                        applySession(refreshedUser)
                        return
                    } catch (refreshError) {
                        if (!hadAuthenticatedSession || isDefinitiveAuthFailure(refreshError)) {
                            applySession(null)
                        }
                        return
                    }
                }

                if (!hadAuthenticatedSession || isDefinitiveAuthFailure(error)) {
                    applySession(null)
                }
            } finally {
                if (bootstrapRequestId === latestBootstrapRequestId.current) {
                    if (!disposed) {
                        setHasHydrated(true)
                    }
                    isBootstrapping.current = false
                }
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
            void bootstrapSession()
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
                void bootstrapSession()
            }
        }

        const handleRouteLifecycle = () => {
            void bootstrapSession()
        }

        const handlePageShow = (event) => {
            if (event.persisted) {
                void bootstrapSession()
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
            // React StrictMode intentionally tears down and immediately re-runs
            // effects in development. Invalidate this effect's in-flight request
            // and release the shared guard so the replacement effect can perform
            // the authoritative session bootstrap. The stale request's finally
            // block is request-id guarded and cannot unlock the replacement.
            latestBootstrapRequestId.current += 1
            isBootstrapping.current = false
            window.clearInterval(revalidationInterval)
            window.removeEventListener('storage', handleStorage)
            window.removeEventListener('focus', handleRouteLifecycle)
            window.removeEventListener('online', handleRouteLifecycle)
            window.removeEventListener('popstate', handleRouteLifecycle)
            window.removeEventListener('pageshow', handlePageShow)
            window.removeEventListener(POS_VENDOR_SESSION_AUTHORITY_LOST_EVENT, revalidateCommerceHubVendorAuthority)
            document.removeEventListener('visibilitychange', handleVisibility)
            unsubscribeFromActorTransitions()
        }
    }, [hydrateSession, logout, setHasHydrated])

    return null
}

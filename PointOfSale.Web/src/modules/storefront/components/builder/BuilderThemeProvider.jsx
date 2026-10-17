import { useLayoutEffect, useMemo, useRef, useState } from 'react'
import { ThemeProvider } from '@/storefront/themes/ThemeContext'
import { clearResolvedTenant, getTenantId, isTenantResolved, setResolvedTenant } from '@/bootstrap'
import { useStoreConfigStore } from '@/store/storeConfigStore'
import { normalizeStoreConfig } from './normalizeStoreConfig'

export function BuilderThemeProvider({ tenantId, storeConfig, children }) {
    const setConfig = useStoreConfigStore((state) => state.setConfig)
    const normalizedConfig = useMemo(() => normalizeStoreConfig(storeConfig), [storeConfig])
    const previousConfigRef = useRef(useStoreConfigStore.getState().config)
    const previousTenantRef = useRef(isTenantResolved() ? getTenantId() : null)
    const [isReady, setIsReady] = useState(false)

    useLayoutEffect(() => {
        const previousConfig = previousConfigRef.current
        const previousTenant = previousTenantRef.current
        if (!storeConfig) {
            setIsReady(false)
            return
        }

        setIsReady(false)

        if (tenantId) {
            setResolvedTenant(tenantId, { overwrite: isTenantResolved() })
        }

        if (import.meta.env.DEV) {
            console.log('[BuilderThemeProvider] Injecting storefront config:', {
                storefrontTheme: normalizedConfig.storefrontTheme,
                tenantId,
            })
        }

        setConfig(normalizedConfig)
        setIsReady(true)

        return () => {
            setConfig(previousConfig)
            if (previousTenant) {
                setResolvedTenant(previousTenant, { overwrite: true })
            } else {
                clearResolvedTenant()
            }
            setIsReady(false)
        }
    }, [normalizedConfig, setConfig, storeConfig, tenantId])

    if (!storeConfig || !isReady) {
        return null
    }

    return <ThemeProvider>{children}</ThemeProvider>
}

export default BuilderThemeProvider

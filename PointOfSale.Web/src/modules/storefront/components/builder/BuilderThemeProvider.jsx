import { useLayoutEffect, useMemo, useState } from 'react'
import { ThemeProvider } from '@/storefront/themes/ThemeContext'
import { isTenantResolved, setResolvedTenant } from '@/bootstrap'
import { useStoreConfigStore } from '@/store/storeConfigStore'
// ARCH-1.4R: normalizeStoreConfig extracted to a standalone module so the
// theme-native canvas no longer depends on this legacy provider. Re-exported
// here for backward compatibility until decommission.
import { normalizeStoreConfig } from './normalizeStoreConfig'

export { normalizeStoreConfig }

export function BuilderThemeProvider({ tenantId, storeConfig, children }) {
    const setConfig = useStoreConfigStore((state) => state.setConfig)
    const normalizedConfig = useMemo(() => normalizeStoreConfig(storeConfig), [storeConfig])
    const [isReady, setIsReady] = useState(false)

    useLayoutEffect(() => {
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
    }, [normalizedConfig, setConfig, storeConfig, tenantId])

    if (!storeConfig || !isReady) {
        return null
    }

    return <ThemeProvider>{children}</ThemeProvider>
}

export default BuilderThemeProvider

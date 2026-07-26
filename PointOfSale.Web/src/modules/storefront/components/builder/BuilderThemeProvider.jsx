import { useLayoutEffect, useMemo, useState } from 'react'
import { ThemeProvider } from '@/storefront/themes/ThemeContext'
import { isTenantResolved, setResolvedTenant } from '@/bootstrap'
import { DEFAULT_STORE_CONFIG } from '@/types/storeConfig'
import { useStoreConfigStore } from '@/store/storeConfigStore'

export function normalizeStoreConfig(storeConfig) {
    return {
        ...DEFAULT_STORE_CONFIG,
        ...storeConfig,
        theme: {
            ...DEFAULT_STORE_CONFIG.theme,
            ...(storeConfig?.theme || {}),
            colors: {
                ...DEFAULT_STORE_CONFIG.theme.colors,
                ...(storeConfig?.theme?.colors || {}),
            },
            font: {
                ...DEFAULT_STORE_CONFIG.theme.font,
                ...(storeConfig?.theme?.font || {}),
            },
            ui: {
                ...DEFAULT_STORE_CONFIG.theme.ui,
                ...(storeConfig?.theme?.ui || {}),
            },
            layout: {
                ...DEFAULT_STORE_CONFIG.theme.layout,
                ...(storeConfig?.theme?.layout || {}),
            },
        },
        behavior: {
            ...DEFAULT_STORE_CONFIG.behavior,
            ...(storeConfig?.behavior || {}),
        },
        pages: storeConfig?.pages || DEFAULT_STORE_CONFIG.pages,
    }
}

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

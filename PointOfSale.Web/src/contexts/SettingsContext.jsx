import React, { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react'
import api from '../services/api'
import i18n from '../i18n/config'
import { useAuthStore } from '../store/authStore'
import { getLocaleForLanguage, formatCurrency as formatCurrencyValue } from '../utils/dateFormat'
import { normalizeCurrencyCode } from '../utils/financialMath'

const SettingsContext = createContext()
const DEFAULT_CURRENCY_CODE = 'EGP'
const DEFAULT_PRINT_FORMAT = 'receipt'
const FALLBACK_CURRENCY_SYMBOLS = {
    EGP: 'E\u00A3',
    USD: '$',
    EUR: '\u20AC',
    GBP: '\u00A3',
}

const getStoredPrintFormat = () => (
    typeof localStorage !== 'undefined'
        ? localStorage.getItem('printFormat') || DEFAULT_PRINT_FORMAT
        : DEFAULT_PRINT_FORMAT
)

const getCurrencySymbolFromCode = (currencyCode) => {
    const normalizedCode = normalizeCurrencyCode(currencyCode) || DEFAULT_CURRENCY_CODE

    try {
        const currencyPart = new Intl.NumberFormat('en-US', {
            style: 'currency',
            currency: normalizedCode,
            currencyDisplay: 'narrowSymbol',
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
        }).formatToParts(0).find((part) => part.type === 'currency')?.value?.trim()

        if (currencyPart) {
            return currencyPart
        }
    } catch {
        // Fall through to static mapping.
    }

    return FALLBACK_CURRENCY_SYMBOLS[normalizedCode] || normalizedCode
}

const createDefaultSettings = () => ({
    currencyCode: DEFAULT_CURRENCY_CODE,
    currencySymbol: getCurrencySymbolFromCode(DEFAULT_CURRENCY_CODE),
    enableTax: true,
    taxRate: 0,
    companyName: '',
    companyLogo: '',
    companyAddress: '',
    companyPhone: '',
    companyEmail: '',
    receiptHeader: '',
    receiptFooter: '',
    showLogoOnReceipt: true,
    storefrontUrl: '',
    printFormat: getStoredPrintFormat(),
})

const normalizeSettingsPayload = (incomingSettings = {}, previousSettings = createDefaultSettings()) => {
    const previousCurrencyCode = normalizeCurrencyCode(previousSettings.currencyCode) || DEFAULT_CURRENCY_CODE
    const previousCurrencySymbol = previousSettings.currencySymbol || getCurrencySymbolFromCode(previousCurrencyCode)
    const nextCurrencyCode = normalizeCurrencyCode(
        incomingSettings.currencyCode || incomingSettings.currencySymbol || previousCurrencyCode,
    ) || DEFAULT_CURRENCY_CODE
    const hasIncomingCurrencySymbol = typeof incomingSettings.currencySymbol === 'string'
        && incomingSettings.currencySymbol.trim().length > 0
    const hasIncomingCurrencyCode = typeof incomingSettings.currencyCode === 'string'
        && incomingSettings.currencyCode.trim().length > 0
    const nextCurrencySymbol = hasIncomingCurrencySymbol
        ? incomingSettings.currencySymbol.trim()
        : hasIncomingCurrencyCode && nextCurrencyCode === previousCurrencyCode
            ? previousCurrencySymbol
            : getCurrencySymbolFromCode(nextCurrencyCode)

    return {
        ...createDefaultSettings(),
        ...previousSettings,
        ...incomingSettings,
        currencyCode: nextCurrencyCode,
        currencySymbol: nextCurrencySymbol,
        printFormat: incomingSettings.printFormat || previousSettings.printFormat || getStoredPrintFormat(),
    }
}

export function SettingsProvider({ children }) {
    const [settings, setSettings] = useState(() => createDefaultSettings())
    const [loading, setLoading] = useState(true)
    const isAuthenticated = useAuthStore((state) => state.isAuthenticated)
    const hasHydrated = useAuthStore((state) => state.hasHydrated)
    const tenantId = useAuthStore((state) => state.user?.tenantId)
    const scope = isAuthenticated ? tenantId || 'authenticated' : null
    const scopeRef = useRef(scope)
    scopeRef.current = scope
    const requestRef = useRef(0)

    const fetchSettings = useCallback(async () => {
        const requestId = ++requestRef.current
        const requestedScope = scope
        try {
            if (!isAuthenticated || requestedScope !== scopeRef.current) {
                setLoading(false)
                return false
            }

            const response = await api.get('/settings', {
                headers: {
                    'Cache-Control': 'no-store',
                    Pragma: 'no-cache',
                },
            })
            if (requestId !== requestRef.current || requestedScope !== scopeRef.current) return false
            if (response.data) {
                setSettings((prev) => normalizeSettingsPayload(response.data, prev))
                return true
            }
            return false
        } catch (error) {
            // Permission-gated reads fail closed with 403; keep safe defaults
            // silently instead of spamming the console for restricted roles.
            if (requestId === requestRef.current && requestedScope === scopeRef.current && error?.response?.status !== 403) {
                console.error('Error loading settings:', { status: error?.response?.status })
            }
            return false
        } finally {
            if (requestId === requestRef.current && requestedScope === scopeRef.current) setLoading(false)
        }
    }, [isAuthenticated, scope])

    useEffect(() => {
        if (!hasHydrated) return
        setSettings(createDefaultSettings())
        if (isAuthenticated) {
            setLoading(true)
            void fetchSettings()
        } else {
            setLoading(false)
        }
        return () => { requestRef.current += 1 }
    }, [hasHydrated, isAuthenticated, fetchSettings])

    const updateSettings = async (newSettings) => {
        const requestedScope = scope
        const previousSettings = settings
        const previousPrintFormat = typeof localStorage !== 'undefined'
            ? localStorage.getItem('printFormat')
            : null

        try {
            const normalizedSettings = normalizeSettingsPayload(newSettings, previousSettings)

            if (typeof localStorage !== 'undefined') {
                if (normalizedSettings.printFormat) {
                    localStorage.setItem('printFormat', normalizedSettings.printFormat)
                } else {
                    localStorage.removeItem('printFormat')
                }
            }

            await api.put('/settings', normalizedSettings, {
                headers: {
                    'Cache-Control': 'no-store',
                    Pragma: 'no-cache',
                },
            })
            if (requestedScope !== scopeRef.current) return false
            return await fetchSettings()
        } catch (error) {
            if (requestedScope !== scopeRef.current) return false
            setSettings(previousSettings)

            if (typeof localStorage !== 'undefined') {
                if (previousPrintFormat !== null) {
                    localStorage.setItem('printFormat', previousPrintFormat)
                } else {
                    localStorage.removeItem('printFormat')
                }
            }

            console.error('Error updating settings:', { status: error?.response?.status })
            return false
        }
    }

    const updateLocalSettings = (key, value) => {
        setSettings((prev) => normalizeSettingsPayload({ [key]: value }, prev))
        if (key === 'printFormat') {
            localStorage.setItem('printFormat', value)
        }
    }

    const formatCurrency = (amount, currency = settings.currencyCode || settings.currencySymbol) => {
        if (amount === null || amount === undefined) {
            return ''
        }

        const language = i18n.resolvedLanguage || i18n.language

        return formatCurrencyValue(
            amount,
            currency || settings.currencySymbol || settings.currencyCode || 'EGP',
            getLocaleForLanguage(language),
        )
    }

    const value = {
        settings,
        loading,
        updateSettings,
        updateLocalSettings,
        formatCurrency,
        reloadSettings: fetchSettings,
    }

    return (
        <SettingsContext.Provider value={value}>
            {children}
        </SettingsContext.Provider>
    )
}

export function useSettings() {
    const context = useContext(SettingsContext)
    if (!context) {
        throw new Error('useSettings must be used within a SettingsProvider')
    }
    return context
}

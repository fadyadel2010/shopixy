import React, { createContext, useContext, useEffect, useState } from 'react'
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

    useEffect(() => {
        if (!hasHydrated) {
            return
        }

        if (isAuthenticated) {
            fetchSettings()
            return
        }

        setSettings(createDefaultSettings())
        setLoading(false)
    }, [hasHydrated, isAuthenticated])

    const fetchSettings = async () => {
        try {
            if (!isAuthenticated) {
                setLoading(false)
                return
            }

            const response = await api.get('/settings', {
                headers: {
                    'Cache-Control': 'no-store',
                    Pragma: 'no-cache',
                },
            })
            if (response.data) {
                setSettings((prev) => normalizeSettingsPayload(response.data, prev))
            }
        } catch (error) {
            console.error('Error loading settings:', error)
        } finally {
            setLoading(false)
        }
    }

    const updateSettings = async (newSettings) => {
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
            await fetchSettings()
            return true
        } catch (error) {
            setSettings(previousSettings)

            if (typeof localStorage !== 'undefined') {
                if (previousPrintFormat !== null) {
                    localStorage.setItem('printFormat', previousPrintFormat)
                } else {
                    localStorage.removeItem('printFormat')
                }
            }

            console.error('Error updating settings:', error)
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

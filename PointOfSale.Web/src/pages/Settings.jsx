import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import api from '../services/api'
import { useToast } from '../components/Toast'
import ResetDatabase from './ResetDatabase'
import StorefrontSettings from './StorefrontSettings'
import { useRTL } from '../hooks/useRTL'
import { formatCurrency } from '../utils/dateFormat'
import { canManageStorefrontSettings } from '../auth/storefrontAdminAuthority'
import { useAuthStore } from '../store/authStore'
import { StorefrontBreadcrumbs } from '../modules/storefront/informationArchitecture'

export default function Settings() {
    const { t } = useTranslation()
    const { dir, isRTL } = useRTL()
    const { showSuccess, showError, showWarning } = useToast()
    const user = useAuthStore((state) => state.user)
    const [searchParams] = useSearchParams()
    const requestedTab = searchParams.get('tab')
    const [loading, setLoading] = useState(true)
    const [settingsLoadFailed, setSettingsLoadFailed] = useState(false)
    const [storefrontStatusDirty, setStorefrontStatusDirty] = useState(false)
    const [activeTab, setActiveTab] = useState(() => requestedTab === 'storefront' ? 'storefront' : 'company')
    const [settings, setSettings] = useState(null)
    const [defaultCountry, setDefaultCountry] = useState({
        countryCode: '',
        isConfigured: false,
        tenantName: '',
    })
    const [countryLoading, setCountryLoading] = useState(false)

    const tabLabelDirectionClass = isRTL ? 'flex-row-reverse' : ''
    const previewTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const fileSpacingClass = isRTL ? 'file:ml-4' : 'file:mr-4'
    const canManageStorefront = canManageStorefrontSettings(user)

    const countryOptions = [
        { code: '', label: t('settings.selectCountry') },
        { code: 'EG', label: `EG - ${t('settings.countries.egypt')} (+20)` },
        { code: 'SA', label: `SA - ${t('settings.countries.saudiArabia')} (+966)` },
        { code: 'AE', label: `AE - ${t('settings.countries.uae')} (+971)` },
        { code: 'KW', label: `KW - ${t('settings.countries.kuwait')} (+965)` },
        { code: 'QA', label: `QA - ${t('settings.countries.qatar')} (+974)` },
        { code: 'BH', label: `BH - ${t('settings.countries.bahrain')} (+973)` },
        { code: 'OM', label: `OM - ${t('settings.countries.oman')} (+968)` },
        { code: 'JO', label: `JO - ${t('settings.countries.jordan')} (+962)` },
        { code: 'LB', label: `LB - ${t('settings.countries.lebanon')} (+961)` },
        { code: 'US', label: `US - ${t('settings.countries.unitedStates')} (+1)` },
        { code: 'GB', label: `GB - ${t('settings.countries.unitedKingdom')} (+44)` },
        { code: 'DE', label: `DE - ${t('settings.countries.germany')} (+49)` },
        { code: 'FR', label: `FR - ${t('settings.countries.france')} (+33)` },
    ]

    const currencyOptions = [
        // Major World Currencies
        { code: 'USD', symbol: '$', label: 'USD - US Dollar' },
        { code: 'EUR', symbol: '€', label: 'EUR - Euro' },
        { code: 'GBP', symbol: '£', label: 'GBP - British Pound' },
        { code: 'JPY', symbol: '¥', label: 'JPY - Japanese Yen' },
        { code: 'CHF', symbol: 'CHF', label: 'CHF - Swiss Franc' },
        { code: 'CAD', symbol: 'C$', label: 'CAD - Canadian Dollar' },
        { code: 'AUD', symbol: 'A$', label: 'AUD - Australian Dollar' },
        { code: 'NZD', symbol: 'NZ$', label: 'NZD - New Zealand Dollar' },
        
        // Middle East & North Africa
        { code: 'EGP', symbol: 'E£', label: 'EGP - Egyptian Pound' },
        { code: 'SAR', symbol: 'SR', label: 'SAR - Saudi Riyal' },
        { code: 'AED', symbol: 'د.إ', label: 'AED - UAE Dirham' },
        { code: 'KWD', symbol: 'KD', label: 'KWD - Kuwaiti Dinar' },
        { code: 'QAR', symbol: 'QR', label: 'QAR - Qatari Riyal' },
        { code: 'OMR', symbol: 'OMR', label: 'OMR - Omani Rial' },
        { code: 'BHD', symbol: 'BD', label: 'BHD - Bahraini Dinar' },
        { code: 'JOD', symbol: 'JD', label: 'JOD - Jordanian Dinar' },
        { code: 'LBP', symbol: 'LL', label: 'LBP - Lebanese Pound' },
        { code: 'MAD', symbol: 'MAD', label: 'MAD - Moroccan Dirham' },
        { code: 'TND', symbol: 'TND', label: 'TND - Tunisian Dinar' },
        
        // Asia Pacific
        { code: 'CNY', symbol: '¥', label: 'CNY - Chinese Yuan' },
        { code: 'INR', symbol: '₹', label: 'INR - Indian Rupee' },
        { code: 'SGD', symbol: 'S$', label: 'SGD - Singapore Dollar' },
        { code: 'HKD', symbol: 'HK$', label: 'HKD - Hong Kong Dollar' },
        { code: 'THB', symbol: '฿', label: 'THB - Thai Baht' },
        { code: 'MYR', symbol: 'RM', label: 'MYR - Malaysian Ringgit' },
        { code: 'IDR', symbol: 'Rp', label: 'IDR - Indonesian Rupiah' },
        { code: 'PHP', symbol: '₱', label: 'PHP - Philippine Peso' },
        
        // Europe
        { code: 'SEK', symbol: 'kr', label: 'SEK - Swedish Krona' },
        { code: 'NOK', symbol: 'kr', label: 'NOK - Norwegian Krone' },
        { code: 'DKK', symbol: 'kr', label: 'DKK - Danish Krone' },
        { code: 'PLN', symbol: 'zł', label: 'PLN - Polish Zloty' },
        { code: 'TRY', symbol: '₺', label: 'TRY - Turkish Lira' },
        
        // Americas
        { code: 'MXN', symbol: 'Mex$', label: 'MXN - Mexican Peso' },
        { code: 'BRL', symbol: 'R$', label: 'BRL - Brazilian Real' },
        { code: 'ARS', symbol: 'AR$', label: 'ARS - Argentine Peso' },
        
        // Africa
        { code: 'ZAR', symbol: 'R', label: 'ZAR - South African Rand' },
        { code: 'NGN', symbol: '₦', label: 'NGN - Nigerian Naira' },
    ]

    // Legacy symbol to ISO code mapping for backward compatibility
    const symbolToCodeMap = {
        '$': 'USD',
        'US$': 'USD',
        '€': 'EUR',
        '£': 'GBP',
        'E£': 'EGP',
        'LE': 'EGP',
        'SR': 'SAR',
        'د.إ': 'AED',
        'KD': 'KWD',
        'QR': 'QAR',
        '¥': 'JPY',
        '₹': 'INR',
        'R': 'ZAR',
        '₦': 'NGN',
    }

    const getCurrencySymbol = (currencyCode) => {
        const currency = currencyOptions.find((option) => option.code === currencyCode)
        return currency?.symbol || currencyCode || 'E£'
    }

    const normalizeCurrencyCode = (codeOrSymbol) => {
        if (!codeOrSymbol) return 'EGP'
        
        const trimmed = codeOrSymbol.trim().toUpperCase()
        
        // Check if it's already a valid ISO code in our supported catalog
        if (currencyOptions.some(opt => opt.code === trimmed)) {
            return trimmed
        }
        
        // Try to map from symbol to code
        const mappedCode = symbolToCodeMap[codeOrSymbol.trim()]
        if (mappedCode && currencyOptions.some(opt => opt.code === mappedCode)) {
            return mappedCode
        }
        
        // If it's a 3-letter code but NOT in our supported catalog, preserve it
        // This allows valid ISO 4217 codes not in our UI list to be displayed and edited
        // The UI will show these as "Custom Currency" in the dropdown
        if (/^[A-Z]{3}$/.test(trimmed)) {
            console.warn(`Currency code "${trimmed}" is not in the UI catalog but will be preserved as a valid ISO 4217 code.`)
            return trimmed
        }
        
        // Default to EGP for invalid formats
        console.warn(`Invalid currency format "${codeOrSymbol}". Falling back to EGP.`)
        return 'EGP'
    }

    useEffect(() => {
        fetchSettings()
        fetchDefaultCountry()
    }, [])

    useEffect(() => {
        if (requestedTab !== 'storefront') {
            return
        }

        setActiveTab(canManageStorefront ? 'storefront' : 'company')
    }, [requestedTab, canManageStorefront])

    useEffect(() => {
        if (activeTab === 'storefront' && !canManageStorefront) {
            setActiveTab('company')
        }
        if (activeTab !== 'storefront') {
            setStorefrontStatusDirty(false)
        }
    }, [activeTab, canManageStorefront])

    const fetchSettings = async () => {
        setLoading(true)
        setSettingsLoadFailed(false)

        try {
            const response = await api.get('/settings', {
                headers: {
                    'Cache-Control': 'no-store',
                    Pragma: 'no-cache',
                },
            })
            if (response.data) {
                // Normalize currency: prefer currencyCode, fallback to symbol mapping, default to EGP
                const rawCurrency = response.data.currencyCode || response.data.currencySymbol || 'EGP'
                const normalizedCode = normalizeCurrencyCode(rawCurrency)
                
                // Show warning if currency is not in the standard UI catalog
                if (normalizedCode && normalizedCode !== 'EGP' && !currencyOptions.some(opt => opt.code === normalizedCode)) {
                    showWarning(
                        t('settings.unknownCurrencyWarning', `Currency ${normalizedCode} is not in the standard catalog but will be preserved.`),
                        5000
                    )
                }
                
                setSettings({
                    ...response.data,
                    currencyCode: normalizedCode,
                    currencySymbol: response.data.currencySymbol || getCurrencySymbol(normalizedCode),
                })
                return true
            }

            setSettings(null)
            setSettingsLoadFailed(true)
            return false
        } catch (error) {
            console.error('Error fetching settings:', error)
            setSettings(null)
            setSettingsLoadFailed(true)
            showError(t('settings.failedToLoadSettings'))
            return false
        } finally {
            setLoading(false)
        }
    }

    const fetchDefaultCountry = async () => {
        try {
            const response = await api.get('/settings/default-country', {
                headers: {
                    'Cache-Control': 'no-store',
                    Pragma: 'no-cache',
                },
            })
            if (response.data) {
                setDefaultCountry(response.data)
            }
        } catch (error) {
            console.error('Error fetching default country:', error)
        }
    }

    const handleChange = (e) => {
        const { name, value, type, checked } = e.target
        setSettings((prev) => ({
            ...prev,
            [name]: type === 'checkbox' ? checked : value,
            ...(name === 'currencyCode'
                ? { currencySymbol: getCurrencySymbol(value) }
                : {}),
        }))
    }

    const handleLogoUpload = (e) => {
        const file = e.target.files[0]
        if (!file) {
            return
        }

        const reader = new FileReader()
        reader.onloadend = () => {
            setSettings((prev) => ({
                ...prev,
                companyLogo: reader.result,
            }))
        }
        reader.readAsDataURL(file)
    }

    const handleSubmit = async (e) => {
        if (e?.preventDefault) {
            e.preventDefault()
        }

        setLoading(true)
        try {
            await api.put('/settings', settings, {
                headers: {
                    'Cache-Control': 'no-store',
                    Pragma: 'no-cache',
                },
            })
            const acceptedSettingsLoaded = await fetchSettings()
            if (!acceptedSettingsLoaded) {
                showError(t('settings.failedToLoadSettings'))
                return
            }
            showSuccess(t('settings.settingsSaved'))
        } catch (error) {
            console.error('Error saving settings:', error)
            showError(t('settings.failedToSaveSettings'))
        } finally {
            setLoading(false)
        }
    }

    const handleCountryChange = async (countryCode) => {
        setCountryLoading(true)
        try {
            const response = await api.put('/settings/default-country', { countryCode }, {
                headers: {
                    'Cache-Control': 'no-store',
                    Pragma: 'no-cache',
                },
            })
            if (response.data) {
                setDefaultCountry(response.data)
                showSuccess(t('settings.defaultCountryUpdated'))
            }
        } catch (error) {
            console.error('Error updating default country:', error)
            showError(t('settings.failedToUpdateDefaultCountry'))
        } finally {
            setCountryLoading(false)
        }
    }

    const renderReceiptPreview = () => (
        <div className="mb-6 rounded border border-gray-200 bg-gray-50 p-4">
            <h3 className="mb-2 text-sm font-semibold text-gray-700">{t('settings.receiptPreview')}</h3>
            <div className="mx-auto w-64 border border-gray-100 bg-white p-4 text-center font-mono text-xs shadow-sm">
                {settings.showLogoOnReceipt && settings.companyLogo && (
                    <div className="mb-2 flex justify-center">
                        <img src={settings.companyLogo} className="h-10 w-auto" alt={t('settings.logoAlt')} />
                    </div>
                )}
                <div className="mb-1 text-sm font-bold">
                    {settings.companyName || t('settings.defaultCompanyName')}
                </div>
                <div className="mb-2 text-gray-500">
                    {settings.receiptHeader || t('settings.defaultReceiptHeader')}
                </div>
                <div className="my-2 border-b border-dashed border-gray-300"></div>
                <div className={`space-y-1 ${previewTextAlignClass}`}>
                    <div className="flex justify-between">
                        <span>{t('settings.sampleItemOne')}</span>
                        <span>{formatCurrency(10, settings.currencyCode)}</span>
                    </div>
                    <div className="flex justify-between">
                        <span>{t('settings.sampleItemTwo')}</span>
                        <span>{formatCurrency(20, settings.currencyCode)}</span>
                    </div>
                </div>
                <div className="my-2 border-t border-dashed border-gray-300"></div>
                <div className="flex justify-between font-bold">
                    <span>{t('common.total')}</span>
                    <span>{formatCurrency(30, settings.currencyCode)}</span>
                </div>
                <div className="my-2 border-b border-dashed border-gray-300"></div>
                <div className="mt-2 text-gray-500">
                    {settings.receiptFooter || t('settings.defaultReceiptFooter')}
                </div>
            </div>
        </div>
    )

    const renderCompanyTab = () => (
        <div className="max-w-2xl space-y-6">
            <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
                <div>
                    <label className="label">{t('settings.companyName')}</label>
                    <input
                        type="text"
                        name="companyName"
                        value={settings.companyName}
                        onChange={handleChange}
                        className="input w-full"
                    />
                </div>
                <div>
                    <label className="label">{t('settings.companyPhone')}</label>
                    <input
                        type="text"
                        name="companyPhone"
                        value={settings.companyPhone}
                        onChange={handleChange}
                        className="input w-full"
                    />
                </div>
            </div>

            <div>
                <label className="label">{t('settings.companyEmail')}</label>
                <input
                    type="email"
                    name="companyEmail"
                    value={settings.companyEmail}
                    onChange={handleChange}
                    className="input w-full"
                />
            </div>

            <div>
                <label className="label">{t('settings.companyAddress')}</label>
                <textarea
                    name="companyAddress"
                    value={settings.companyAddress}
                    onChange={handleChange}
                    rows="3"
                    className="input w-full"
                ></textarea>
            </div>

            <div>
                <label className="label">{t('settings.companyLogo')}</label>
                <div className="flex items-start gap-6">
                    <div className="flex h-32 w-32 items-center justify-center overflow-hidden rounded-lg border-2 border-dashed border-gray-300 bg-gray-50">
                        {settings.companyLogo ? (
                            <img src={settings.companyLogo} alt={t('settings.logoAlt')} className="h-full w-full object-contain" />
                        ) : (
                            <span className="text-sm text-gray-400">{t('settings.noLogo')}</span>
                        )}
                    </div>
                    <div className="flex-1">
                        <input
                            type="file"
                            accept="image/*"
                            onChange={handleLogoUpload}
                            className={`block w-full text-sm text-gray-500 file:py-2 file:px-4 file:rounded-full file:border-0 file:text-sm file:font-semibold file:bg-blue-50 file:text-blue-700 hover:file:bg-blue-100 ${fileSpacingClass}`}
                        />
                        <p className="mt-2 text-xs text-gray-500">{t('settings.logoRecommendedSize')}</p>
                        {settings.companyLogo && (
                            <button
                                type="button"
                                onClick={() => setSettings((prev) => ({ ...prev, companyLogo: '' }))}
                                className="mt-2 text-sm text-red-600 hover:text-red-800"
                            >
                                {t('settings.removeLogo')}
                            </button>
                        )}
                    </div>
                </div>
            </div>
        </div>
    )

    const renderTaxTab = () => {
        // Check if current currency is in our predefined list
        const currentCurrencyInList = currencyOptions.some(opt => opt.code === settings.currencyCode)
        
        return (
            <div className="max-w-xl space-y-6">
                <div>
                    <label className="label">{t('settings.currencyCode', 'Currency')}</label>
                    <select
                        name="currencyCode"
                        value={settings.currencyCode || 'EGP'}
                        onChange={handleChange}
                        className="input w-full max-w-xs"
                    >
                        {!currentCurrencyInList && settings.currencyCode && (
                            <option value={settings.currencyCode}>
                                {settings.currencyCode} - {t('settings.customCurrency', 'Custom Currency')}
                            </option>
                        )}
                        {currencyOptions.map((option) => (
                            <option key={option.code} value={option.code}>
                                {option.label}
                            </option>
                        ))}
                    </select>
                    <p className="mt-2 text-sm text-gray-500">
                        {t('settings.currencySymbol')}: {settings.currencySymbol || getCurrencySymbol(settings.currencyCode)}
                    </p>
                </div>

                <div className="border-t border-gray-200 pt-6">
                    <div className="mb-4 flex items-center gap-3">
                        <input
                            type="checkbox"
                            name="enableTax"
                            id="enableTax"
                            checked={settings.enableTax}
                            onChange={handleChange}
                            className="h-5 w-5 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                        />
                        <label htmlFor="enableTax" className="font-medium text-gray-900">
                            {t('settings.enableTax')}
                        </label>
                    </div>

                    <div className={settings.enableTax ? 'opacity-100' : 'pointer-events-none opacity-50'}>
                        <label className="label">{t('settings.taxRate')}</label>
                        <input
                            type="number"
                            name="taxRate"
                            value={settings.taxRate}
                            onChange={handleChange}
                            className="input w-full max-w-xs"
                            min="0"
                            step="0.01"
                        />
                        <p className="mt-1 text-xs text-gray-500">{t('settings.defaultTaxRateHelp')}</p>
                    </div>
                </div>
            </div>
        )
    }

    const renderReceiptTab = () => (
        <div className="max-w-2xl space-y-6">
            {renderReceiptPreview()}

            <div>
                <label className="label">{t('settings.receiptHeader')}</label>
                <textarea
                    name="receiptHeader"
                    value={settings.receiptHeader}
                    onChange={handleChange}
                    rows="2"
                    className="input w-full"
                    placeholder={t('settings.receiptHeaderPlaceholder')}
                ></textarea>
            </div>

            <div>
                <label className="label">{t('settings.receiptFooter')}</label>
                <textarea
                    name="receiptFooter"
                    value={settings.receiptFooter}
                    onChange={handleChange}
                    rows="2"
                    className="input w-full"
                    placeholder={t('settings.receiptFooterPlaceholder')}
                ></textarea>
            </div>

            <div className="flex items-center gap-3 pt-4">
                <input
                    type="checkbox"
                    name="showLogoOnReceipt"
                    id="showLogoOnReceipt"
                    checked={settings.showLogoOnReceipt}
                    onChange={handleChange}
                    className="h-5 w-5 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                />
                <label htmlFor="showLogoOnReceipt" className="font-medium text-gray-900">
                    {t('settings.showLogoOnReceipt')}
                </label>
            </div>
        </div>
    )

    const renderMessagingTab = () => (
        <div className="max-w-2xl space-y-8">
            <div className="rounded-xl border border-green-200 bg-gradient-to-r from-green-50 to-emerald-50 p-6">
                <div className="flex items-start gap-4">
                    <div className="flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-xl bg-green-500 text-sm font-bold text-white">
                        WA
                    </div>
                    <div className="flex-1">
                        <h3 className="mb-1 text-lg font-semibold text-gray-900">{t('settings.defaultCountryTitle')}</h3>
                        <p className="mb-4 text-sm text-gray-600">{t('settings.defaultCountryDescription')}</p>

                        <div className="flex flex-wrap items-center gap-4">
                            <select
                                value={defaultCountry.countryCode || ''}
                                onChange={(e) => handleCountryChange(e.target.value)}
                                disabled={countryLoading}
                                className="input w-64"
                            >
                                {countryOptions.map((option) => (
                                    <option key={option.code} value={option.code}>
                                        {option.label}
                                    </option>
                                ))}
                            </select>

                            {countryLoading && (
                                <span className="text-sm text-gray-500">{t('common.saving')}</span>
                            )}

                            {defaultCountry.isConfigured && !countryLoading && (
                                <span className="inline-flex items-center gap-1.5 rounded-full bg-green-100 px-3 py-1 text-sm font-medium text-green-700">
                                    {t('settings.configured')}
                                </span>
                            )}
                        </div>

                        {defaultCountry.isConfigured && (
                            <div className="mt-4 rounded-lg border border-green-100 bg-white p-3">
                                <p className="text-sm text-gray-600">
                                    <strong>{t('settings.example')}:</strong> {t('settings.defaultCountryExampleIntro')}{' '}
                                    <code className="rounded bg-gray-100 px-1.5 py-0.5 text-green-700">01022174803</code>,
                                    {' '}{t('settings.defaultCountryCountrylessExample')}{' '}
                                    <code className="rounded bg-gray-100 px-1.5 py-0.5 text-green-700">
                                        {defaultCountry.countryCode === 'EG'
                                            ? '+201022174803'
                                            : defaultCountry.countryCode === 'SA'
                                                ? '+966101234567'
                                                : defaultCountry.countryCode === 'AE'
                                                    ? '+971501234567'
                                                    : '+XX...'}
                                    </code>
                                </p>
                            </div>
                        )}
                    </div>
                </div>
            </div>

            <div className="rounded-xl border border-gray-200 bg-gray-50 p-6">
                <h3 className="mb-3 text-lg font-semibold text-gray-900">{t('settings.messagingInfoTitle')}</h3>
                <p className="mb-4 text-sm text-gray-600">{t('settings.messagingInfoDescription')}</p>
                <ul className="space-y-2 text-sm text-gray-600">
                    <li className="flex items-center gap-2">
                        <span className="flex h-5 w-5 items-center justify-center rounded-full bg-green-100 text-xs text-green-600">1</span>
                        {t('settings.messagingUseCaseOtp')}
                    </li>
                    <li className="flex items-center gap-2">
                        <span className="flex h-5 w-5 items-center justify-center rounded-full bg-green-100 text-xs text-green-600">2</span>
                        {t('settings.messagingUseCaseOrderConfirmation')}
                    </li>
                    <li className="flex items-center gap-2">
                        <span className="flex h-5 w-5 items-center justify-center rounded-full bg-green-100 text-xs text-green-600">3</span>
                        {t('settings.messagingUseCaseShippingUpdates')}
                    </li>
                </ul>

                <div className="mt-4 border-t border-gray-200 pt-4">
                    <a
                        href="/whatsapp"
                        className={`inline-flex items-center gap-2 text-sm font-medium text-green-600 hover:text-green-700 ${isRTL ? 'flex-row-reverse' : ''}`}
                    >
                        {t('settings.goToWhatsAppSettings')}
                        <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                        </svg>
                    </a>
                </div>
            </div>
        </div>
    )

    if (!settings) {
        return (
            <div dir={dir} className="space-y-6">
                <div className="flex items-center justify-between">
                    <h1 className="text-2xl font-bold text-gray-900">{t('settings.settings')}</h1>
                </div>
                <div className="rounded-lg bg-white p-6 shadow">
                    {loading && (
                        <p className="text-sm text-gray-600">{t('common.loading')}</p>
                    )}
                    {!loading && settingsLoadFailed && (
                        <div className="space-y-4">
                            <p className="text-sm text-red-600">{t('settings.failedToLoadSettings')}</p>
                            <button type="button" onClick={fetchSettings} className="btn-secondary">
                                {t('common.retry')}
                            </button>
                        </div>
                    )}
                </div>
            </div>
        )
    }

    return (
        <div dir={dir} className="space-y-6">
            {activeTab === 'storefront' ? (
                <StorefrontBreadcrumbs
                    onNavigate={() => (
                        !storefrontStatusDirty || window.confirm(
                            isRTL ? 'لديك تغييرات غير محفوظة. هل أنت متأكد من الخروج؟' : 'You have unsaved changes. Are you sure you want to exit?',
                        )
                    )}
                />
            ) : null}
            <div className="flex items-center justify-between">
                <h1 className="text-2xl font-bold text-gray-900">{t('settings.settings')}</h1>
                <button onClick={handleSubmit} disabled={loading} className="btn-primary">
                    {loading ? t('common.saving') : t('settings.saveSettings')}
                </button>
            </div>

            <div className="flex min-h-[500px] flex-col rounded-lg bg-white shadow">
                <div className="border-b border-gray-200">
                    <nav className="flex -mb-px overflow-x-auto">
                        <button
                            onClick={() => setActiveTab('company')}
                            className={`whitespace-nowrap border-b-2 px-6 py-4 text-sm font-medium transition-colors ${
                                activeTab === 'company'
                                    ? 'border-blue-500 text-blue-600'
                                    : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-700'
                            }`}
                        >
                            {t('settings.companyProfile')}
                        </button>
                        <button
                            onClick={() => setActiveTab('tax')}
                            className={`whitespace-nowrap border-b-2 px-6 py-4 text-sm font-medium transition-colors ${
                                activeTab === 'tax'
                                    ? 'border-blue-500 text-blue-600'
                                    : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-700'
                            }`}
                        >
                            {t('settings.taxAndCurrency')}
                        </button>
                        <button
                            onClick={() => setActiveTab('receipt')}
                            className={`whitespace-nowrap border-b-2 px-6 py-4 text-sm font-medium transition-colors ${
                                activeTab === 'receipt'
                                    ? 'border-blue-500 text-blue-600'
                                    : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-700'
                            }`}
                        >
                            {t('settings.receiptSettings')}
                        </button>
                        <button
                            onClick={() => setActiveTab('messaging')}
                            className={`inline-flex items-center gap-2 whitespace-nowrap border-b-2 px-6 py-4 text-sm font-medium transition-colors ${
                                activeTab === 'messaging'
                                    ? 'border-green-500 text-green-600'
                                    : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-700'
                            } ${tabLabelDirectionClass}`}
                        >
                            <span>WA</span>
                            <span>{t('settings.messaging')}</span>
                        </button>
                        {canManageStorefront && (
                            <button
                                onClick={() => setActiveTab('storefront')}
                                className={`inline-flex items-center gap-2 whitespace-nowrap border-b-2 px-6 py-4 text-sm font-medium transition-colors ${
                                    activeTab === 'storefront'
                                        ? 'border-blue-500 text-blue-600'
                                        : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-700'
                                } ${tabLabelDirectionClass}`}
                            >
                                <span>WEB</span>
                                <span>{t('storefront.onlineStore')}</span>
                            </button>
                        )}
                        <button
                            onClick={() => setActiveTab('danger')}
                            className={`whitespace-nowrap border-b-2 px-6 py-4 text-sm font-medium transition-colors ${
                                activeTab === 'danger'
                                    ? 'border-red-500 text-red-600'
                                    : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-700'
                            }`}
                        >
                            {t('databaseReset.title')}
                        </button>
                    </nav>
                </div>

                <div className="flex-1 p-6">
                    {activeTab === 'company' && renderCompanyTab()}
                    {activeTab === 'tax' && renderTaxTab()}
                    {activeTab === 'receipt' && renderReceiptTab()}
                    {activeTab === 'messaging' && renderMessagingTab()}
                    {activeTab === 'storefront' && canManageStorefront && (
                        <div className="max-w-4xl">
                            <StorefrontSettings onDirtyChange={setStorefrontStatusDirty} />
                        </div>
                    )}
                    {activeTab === 'danger' && (
                        <div className="max-w-4xl">
                            <ResetDatabase />
                        </div>
                    )}
                </div>
            </div>
        </div>
    )
}

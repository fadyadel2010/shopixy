import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { CheckCircle2, ExternalLink, Loader2, Monitor, Paintbrush, Sparkles } from 'lucide-react'
import { useAuthStore } from '../../../store/authStore'
import { useToast } from '../../../components/Toast'
import { storeConfigService } from '../services'
import { deriveCurrentStoreIdentityPresentation } from '../../../auth/commerceHubVendorIdentityAuthority'
import {
    getAvailableMarketplaceThemes,
    getMarketplaceThemeById,
} from '../themes/themeMarketplaceRegistry'

const STOREFRONT_BASE_URL = import.meta.env.VITE_STOREFRONT_URL || 'http://localhost:5174'

function buildPreviewUrl(previewToken) {
    const url = new URL(STOREFRONT_BASE_URL)

    url.searchParams.set('themePreviewToken', previewToken)
    return url.toString()
}

const normalizeRowVersion = (rowVersion) => (
    typeof rowVersion === 'string' && rowVersion.trim() ? rowVersion.trim() : ''
)

const buildIfMatchOptions = (rowVersion) => {
    const acceptedRowVersion = normalizeRowVersion(rowVersion)
    return acceptedRowVersion
        ? { headers: { 'If-Match': `"${acceptedRowVersion}"` } }
        : {}
}

function ThemePreviewArtwork({ theme }) {
    return (
        <div className={`relative h-40 overflow-hidden rounded-2xl bg-gradient-to-br ${theme.accent}`}>
            <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,rgba(255,255,255,0.32),transparent_42%)]" />
            <div className="absolute inset-x-4 bottom-4 rounded-2xl border border-white/30 bg-white/15 p-4 backdrop-blur-sm">
                <div className="mb-2 flex items-center justify-between text-white">
                    <span className="text-xs font-semibold uppercase tracking-[0.24em]">{theme.id}</span>
                    <Sparkles size={16} />
                </div>
                <div className="space-y-2">
                    <div className="h-2.5 w-2/3 rounded-full bg-white/80" />
                    <div className="h-2.5 w-1/2 rounded-full bg-white/55" />
                </div>
            </div>
        </div>
    )
}

function ThemeCard({ theme, isActive, isBusy, onPreview, onActivate, onCustomize, previewLabel, activateLabel, customizeLabel, activeBadge }) {
    return (
        <div className={`rounded-3xl border bg-white p-6 shadow-sm transition-all ${isActive ? 'border-indigo-200 shadow-indigo-100/50' : 'border-gray-200 hover:-translate-y-0.5 hover:shadow-lg'}`}>
            <ThemePreviewArtwork theme={theme} />

            <div className="mt-5 flex items-start justify-between gap-4">
                <div>
                    <h3 className="text-xl font-semibold text-gray-900">{theme.name}</h3>
                    <p className="mt-2 text-sm leading-6 text-gray-500">{theme.description}</p>
                </div>
                {isActive ? (
                    <span className="inline-flex items-center gap-2 rounded-full bg-emerald-50 px-3 py-1 text-xs font-semibold text-emerald-700">
                        <CheckCircle2 size={14} />
                        {activeBadge}
                    </span>
                ) : null}
            </div>

            <div className="mt-6 flex flex-wrap gap-3">
                {isActive ? (
                    <>
                        <button
                            type="button"
                            onClick={onCustomize}
                            className="inline-flex items-center gap-2 rounded-xl border border-indigo-200 px-4 py-2 text-sm font-medium text-indigo-700 transition-colors hover:bg-indigo-50"
                        >
                            <Paintbrush size={16} />
                            {customizeLabel}
                        </button>
                        <button
                            type="button"
                            onClick={onPreview}
                            className="inline-flex items-center gap-2 rounded-xl bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-indigo-700"
                        >
                            <ExternalLink size={16} />
                            {previewLabel}
                        </button>
                    </>
                ) : (
                    <>
                        <button
                            type="button"
                            onClick={onPreview}
                            className="inline-flex items-center gap-2 rounded-xl border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50"
                        >
                            <Monitor size={16} />
                            {previewLabel}
                        </button>
                        <button
                            type="button"
                            onClick={onActivate}
                            disabled={isBusy}
                            className="inline-flex items-center gap-2 rounded-xl bg-gray-900 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-gray-800 disabled:cursor-not-allowed disabled:opacity-60"
                        >
                            {isBusy ? <Loader2 size={16} className="animate-spin" /> : <CheckCircle2 size={16} />}
                            {activateLabel}
                        </button>
                    </>
                )}
            </div>
        </div>
    )
}

export function ThemeMarketplacePage() {
    const { i18n } = useTranslation()
    const navigate = useNavigate()
    const { user } = useAuthStore()
    const { showError, showInfo, showSuccess } = useToast()
    const isRTL = i18n.language === 'ar'

    const themes = useMemo(() => getAvailableMarketplaceThemes(), [])

    const [storeConfig, setStoreConfig] = useState(null)
    const [loading, setLoading] = useState(true)
    const [activatingThemeId, setActivatingThemeId] = useState('')

    const activeThemeId = storeConfig?.storefrontTheme || null
    const activeTheme = activeThemeId ? getMarketplaceThemeById(activeThemeId) : null
    const installedThemes = activeTheme ? themes.filter((theme) => theme.id !== activeTheme.id) : themes

    useEffect(() => {
        let isMounted = true

        const loadThemeConfig = async () => {
            setLoading(true)
            try {
                const response = await storeConfigService.getStoreConfig()
                const config = deriveCurrentStoreIdentityPresentation(response, user)
                if (!config) {
                    throw new Error('Theme marketplace configuration was rejected by backend store authority admission.')
                }

                if (isMounted) {
                    setStoreConfig(config)
                }
            } catch (error) {
                console.error('Failed to load storefront theme marketplace:', error)
                if (isMounted) {
                    showError(isRTL ? 'فشل تحميل الثيمات المتاحة' : 'Failed to load available themes')
                }
            } finally {
                if (isMounted) {
                    setLoading(false)
                }
            }
        }

        loadThemeConfig()
        return () => {
            isMounted = false
        }
    }, [isRTL, showError, user])

    const openPreview = async (themeId) => {
        try {
            const preview = await storeConfigService.createThemePreviewLink(themeId)
            window.open(buildPreviewUrl(preview.previewToken), '_blank', 'noopener,noreferrer')
            showInfo(isRTL ? 'جارٍ فتح معاينة الثيم في المتجر' : 'Opening storefront theme preview...')
        } catch (error) {
            console.error('Failed to create storefront theme preview:', error)
            showError(isRTL ? 'فشل إنشاء معاينة الثيم' : 'Failed to create theme preview')
        }
    }

    const handleActivate = async (themeId) => {
        if (!storeConfig) {
            return
        }

        setActivatingThemeId(themeId)
        try {
            const rowVersion = normalizeRowVersion(storeConfig.rowVersion)
            if (!rowVersion) {
                throw new Error('Theme activation requires accepted backend RowVersion evidence.')
            }

            const response = await storeConfigService.activateTheme(
                themeId,
                buildIfMatchOptions(rowVersion)
            )
            const updatedConfig = deriveCurrentStoreIdentityPresentation(response, user)
            if (!updatedConfig) {
                throw new Error('Theme activation response was rejected by backend store authority admission.')
            }

            setStoreConfig(updatedConfig)
            showSuccess(
                isRTL
                    ? `تم تفعيل ثيم ${getMarketplaceThemeById(themeId)?.name || themeId}`
                    : `${getMarketplaceThemeById(themeId)?.name || themeId} theme activated`
            )
        } catch (error) {
            console.error('Failed to activate storefront theme:', error)
            showError(isRTL ? 'فشل تفعيل الثيم' : 'Failed to activate theme')
        } finally {
            setActivatingThemeId('')
        }
    }

    if (loading) {
        return (
            <div className="flex h-80 items-center justify-center">
                <div className="text-center">
                    <Loader2 className="mx-auto mb-4 animate-spin text-indigo-600" size={40} />
                    <p className="text-sm text-gray-500">{isRTL ? 'جارٍ تحميل مكتبة الثيمات...' : 'Loading theme marketplace...'}</p>
                </div>
            </div>
        )
    }

    return (
        <div className="mx-auto max-w-7xl">
            <div className="mb-8 flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
                <div>
                    <h1 className="text-3xl font-bold text-gray-900">{isRTL ? 'مكتبة ثيمات المتجر' : 'Storefront Themes'}</h1>
                    <p className="mt-2 max-w-3xl text-sm leading-6 text-gray-600">
                        {isRTL
                            ? 'استعرض الثيمات المتاحة، جرّبها على المتجر مباشرة، ثم فعّل الثيم المناسب لمتجرك.'
                            : 'Browse the available themes, preview them on your storefront, and activate the one that fits your brand.'}
                    </p>
                </div>

                <button
                    type="button"
                    onClick={() => navigate('/storefront/theme')}
                    className="inline-flex items-center gap-2 self-start rounded-xl border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50"
                >
                    <Paintbrush size={16} />
                    {isRTL ? 'فتح محرر الثيم' : 'Open Theme Editor'}
                </button>
            </div>

            <section className="mb-10">
                <div className="mb-4 flex items-center gap-2">
                    <CheckCircle2 size={18} className="text-emerald-600" />
                    <h2 className="text-lg font-semibold text-gray-900">{isRTL ? 'الثيم النشط' : 'Active Theme'}</h2>
                </div>
                {activeTheme ? (
                    <ThemeCard
                        theme={activeTheme}
                        isActive
                        isBusy={false}
                        onPreview={() => openPreview(activeTheme.id)}
                        onActivate={() => {}}
                        onCustomize={() => navigate('/storefront/theme')}
                        previewLabel={isRTL ? 'معاينة' : 'Preview'}
                        activateLabel={isRTL ? 'تفعيل' : 'Activate'}
                        customizeLabel={isRTL ? 'تخصيص' : 'Customize'}
                        activeBadge={isRTL ? 'الثيم النشط' : 'Active Theme'}
                    />
                ) : (
                    <div className="rounded-3xl border border-dashed border-gray-300 bg-white p-8 text-sm text-gray-600">
                        {isRTL
                            ? 'لم يقم الخادم بإرسال ثيم نشط حتى الآن. اختر ثيماً من الخيارات المتاحة بالأسفل.'
                            : 'The backend has not provided an active theme yet. Choose one from the available themes below.'}
                    </div>
                )}
            </section>

            <section>
                <div className="mb-4 flex items-center gap-2">
                    <Monitor size={18} className="text-indigo-600" />
                    <h2 className="text-lg font-semibold text-gray-900">{isRTL ? 'الثيمات المتاحة' : 'Available Themes'}</h2>
                </div>

                <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
                    {installedThemes.map((theme) => (
                        <ThemeCard
                            key={theme.id}
                            theme={theme}
                            isActive={false}
                            isBusy={activatingThemeId === theme.id}
                            onPreview={() => openPreview(theme.id)}
                            onActivate={() => handleActivate(theme.id)}
                            onCustomize={() => {}}
                            previewLabel={isRTL ? 'معاينة' : 'Preview'}
                            activateLabel={isRTL ? 'تفعيل' : 'Activate'}
                            customizeLabel={isRTL ? 'تخصيص' : 'Customize'}
                            activeBadge={isRTL ? 'الثيم النشط' : 'Active Theme'}
                        />
                    ))}
                </div>
            </section>
        </div>
    )
}

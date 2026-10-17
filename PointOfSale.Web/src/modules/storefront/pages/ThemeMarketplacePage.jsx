import { useEffect, useMemo, useRef, useState } from 'react'
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
    getThemeArtworkStyle,
} from '../themes/themeMarketplaceRegistry'
import {
    AuthoringActionBar,
    AuthoringConfirmDialog,
    AuthoringConflictDialog,
    AuthoringEmptyState,
    AuthoringFeedback,
    AuthoringLoadingState,
    AuthoringStatusBadge,
} from '../authoringPlatform'

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
        <div
            className="relative h-40 overflow-hidden rounded-2xl"
            style={getThemeArtworkStyle(theme)}
        >
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
    const [pendingThemeId, setPendingThemeId] = useState('')
    const [feedback, setFeedback] = useState(null)
    const [conflict, setConflict] = useState(null)
    const [loadError, setLoadError] = useState(false)
    const [loadAttempt, setLoadAttempt] = useState(0)
    const mutationInFlightRef = useRef(false)

    const activeThemeId = storeConfig?.storefrontTheme || null
    const activeTheme = activeThemeId ? getMarketplaceThemeById(activeThemeId) : null
    const installedThemes = activeTheme ? themes.filter((theme) => theme.id !== activeTheme.id) : themes

    useEffect(() => {
        let isMounted = true

        const loadThemeConfig = async () => {
            setLoading(true)
            setLoadError(false)
            setStoreConfig(null)
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
                    setLoadError(true)
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
    }, [isRTL, showError, user, loadAttempt])

    const openPreview = async (themeId) => {
        try {
            const preview = await storeConfigService.createThemePreviewLink(themeId)
            window.open(preview.previewUrl, '_blank', 'noopener,noreferrer')
            showInfo(isRTL ? 'جارٍ فتح معاينة الثيم في المتجر' : 'Opening storefront theme preview...')
        } catch (error) {
            console.error('Failed to create storefront theme preview:', error)
            showError(isRTL ? 'فشل إنشاء معاينة الثيم' : 'Failed to create theme preview')
        }
    }

    const handleActivate = async (themeId, rowVersionOverride = null) => {
        if (mutationInFlightRef.current || !storeConfig) {
            return
        }

        mutationInFlightRef.current = true
        setActivatingThemeId(themeId)
        try {
            const rowVersion = normalizeRowVersion(rowVersionOverride || storeConfig.rowVersion)
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
            setPendingThemeId('')
            setConflict(null)
            setFeedback({
                kind: 'success',
                title: isRTL ? 'تم تفعيل الثيم' : 'Theme activated',
                message: isRTL
                    ? `أصبح ${getMarketplaceThemeById(themeId)?.name || themeId} هو الثيم المباشر الآن.`
                    : `${getMarketplaceThemeById(themeId)?.name || themeId} is now the live Theme.`,
            })
            showSuccess(
                isRTL
                    ? `تم تفعيل ثيم ${getMarketplaceThemeById(themeId)?.name || themeId}`
                    : `${getMarketplaceThemeById(themeId)?.name || themeId} theme activated`
            )
        } catch (error) {
            console.error('Failed to activate storefront theme:', error)
            if (error?.response?.status === 409) {
                setPendingThemeId('')
                setConflict({ themeId, message: error?.response?.data?.message })
            }
            else setFeedback({
                kind: 'error',
                title: isRTL ? 'لم يتم تفعيل الثيم' : 'Theme not activated',
                message: error?.response?.data?.message || (isRTL ? 'فشل تفعيل الثيم.' : 'Failed to activate theme.'),
            })
        } finally {
            mutationInFlightRef.current = false
            setActivatingThemeId('')
        }
    }

    if (loading) {
        return <AuthoringLoadingState label={isRTL ? 'جارٍ تحميل الثيمات…' : 'Loading Themes…'} />
    }

    if (loadError) {
        return <AuthoringFeedback kind="error" title={isRTL ? 'الثيمات غير متاحة' : 'Themes unavailable'}
            message={isRTL ? 'تعذر تحميل الثيم النشط. لم يتم افتراض ثيم بديل.' : 'The active Theme could not be loaded. No substitute Theme was assumed.'}
            action={{ label: isRTL ? 'إعادة المحاولة' : 'Retry', onClick: () => setLoadAttempt(value => value + 1) }} />
    }

    return (
        <div className="mx-auto max-w-7xl">
            <AuthoringActionBar
                title={isRTL ? 'الثيمات' : 'Themes'}
                subtitle={isRTL
                    ? 'عاين ثيمًا مرشحًا بتوقيع وصلاحية قراءة فقط، ثم فعّل ثيمًا واحدًا بشكل صريح. تفعيل الثيم لا ينشر المحتوى.'
                    : 'Preview a signed, read-only Theme candidate, then explicitly activate one Theme live. Theme activation does not publish content.'}
                backAction={{ label: isRTL ? 'رجوع' : 'Back', onClick: () => navigate('/storefront') }}
                status={<AuthoringStatusBadge state={activeTheme ? 'live' : 'draft'}>{activeTheme ? (isRTL ? `${activeTheme.name} نشط` : `${activeTheme.name} active`) : (isRTL ? 'لا يوجد ثيم نشط' : 'No active Theme')}</AuthoringStatusBadge>}
                leading={<button
                    type="button"
                    onClick={() => navigate('/storefront/appearance/colors-typography')}
                    className="inline-flex items-center gap-2 self-start rounded-xl border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50"
                >
                    <Paintbrush size={16} />
                    {isRTL ? 'فتح الألوان والطباعة' : 'Open Colors & Typography'}
                </button>}
            />
            {feedback ? <div className="my-6"><AuthoringFeedback {...feedback} /></div> : null}

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
                        onCustomize={() => navigate('/storefront/appearance/colors-typography')}
                        previewLabel={isRTL ? 'معاينة القالب' : 'Preview Theme'}
                        activateLabel={isRTL ? 'تفعيل القالب الآن' : 'Activate theme now'}
                        customizeLabel={isRTL ? 'تخصيص' : 'Customize'}
                        activeBadge={isRTL ? 'الثيم النشط' : 'Active Theme'}
                    />
                ) : (
                    <AuthoringEmptyState
                        title={isRTL ? 'لا يوجد ثيم نشط' : 'No active Theme'}
                        description={isRTL ? 'اختر ثيمًا أدناه. المعاينة للقراءة فقط، ويتطلب التفعيل تأكيدًا صريحًا.' : 'Choose a Theme below. Preview is read-only; activation requires explicit confirmation.'}
                    />
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
                            onActivate={() => setPendingThemeId(theme.id)}
                            onCustomize={() => {}}
                            previewLabel={isRTL ? 'معاينة القالب' : 'Preview Theme'}
                            activateLabel={isRTL ? 'تفعيل القالب الآن' : 'Activate theme now'}
                            customizeLabel={isRTL ? 'تخصيص' : 'Customize'}
                            activeBadge={isRTL ? 'الثيم النشط' : 'Active Theme'}
                        />
                    ))}
                </div>
                {installedThemes.length === 0 ? (
                    <AuthoringEmptyState
                        title={isRTL ? 'لا توجد ثيمات إضافية' : 'No additional Themes'}
                        description={isRTL ? 'الثيم النشط هو الثيم الوحيد المتاح حاليًا.' : 'The active Theme is the only Theme currently available.'}
                    />
                ) : null}
            </section>
            <AuthoringConfirmDialog
                open={Boolean(pendingThemeId)}
                title={isRTL ? 'تفعيل الثيم الآن؟' : 'Activate theme now?'}
                description={isRTL ? 'يغيّر التفعيل الثيم المباشر فقط فورًا. ولا ينشر الصفحة الرئيسية أو الصفحات أو التنقل أو التذييل أو قالب المنتج.' : 'Activation immediately changes the live Theme only. It does not publish Homepage, Pages, Navigation, Footer, or Product Template.'}
                confirmLabel={isRTL ? 'تفعيل الثيم الآن' : 'Activate theme now'}
                confirmTone="publish"
                busy={Boolean(activatingThemeId)}
                onClose={() => setPendingThemeId('')}
                onConfirm={() => handleActivate(pendingThemeId)}
            />
            <AuthoringConflictDialog
                open={conflict != null}
                message={conflict?.message}
                canRetry
                onClose={() => setConflict(null)}
                onLoadLatest={async () => {
                    setConflict(null)
                    const response = await storeConfigService.getStoreConfig()
                    const config = deriveCurrentStoreIdentityPresentation(response, user)
                    if (config) setStoreConfig(config)
                }}
                onRetry={async () => {
                    const response = await storeConfigService.getStoreConfig()
                    const config = deriveCurrentStoreIdentityPresentation(response, user)
                    if (!config) return
                    setStoreConfig(config)
                    await handleActivate(conflict.themeId, config.rowVersion)
                }}
            />
        </div>
    )
}

import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
    Check,
    AlertCircle,
    Sun,
    Moon,
    Sparkles,
} from 'lucide-react'
import { storeConfigService } from '../services'
import { useToast } from '../../../components/Toast'
import { useAuthStore } from '../../../store/authStore'
import { deriveCurrentStoreIdentityPresentation } from '../../../auth/commerceHubVendorIdentityAuthority'
import {
    AuthoringActionBar,
    AuthoringConfirmDialog,
    AuthoringConflictDialog,
    AuthoringFeedback,
    AuthoringLoadingState,
    AuthoringStatusBadge,
    UnsavedChangesDialog,
    useAuthoringGuard,
} from '../authoringPlatform'

const DEFAULT_THEME = {
    presetKey: 'default',
    colors: {
        primary: '#2563eb',
        secondary: '#0f172a',
        background: '#ffffff',
        text: '#020617',
        muted: '#64748b',
    },
    font: 'Inter',
    baseSize: '16px',
    borderRadius: 8,
}

/**
 * Apply alpha transparency to a color value.
 * Supports hex (#rgb, #rrggbb), rgb(), rgba(), and CSS variables.
 * @param {string} color - The color value
 * @param {number} alpha - Alpha value between 0 and 1
 * @returns {string} Color with alpha applied
 */
const applyAlpha = (color, alpha) => {
    if (!color || typeof color !== 'string') {
        return color
    }

    const trimmed = color.trim()

    // Handle CSS variables - return as-is (can't apply alpha to variables)
    if (trimmed.startsWith('var(')) {
        return trimmed
    }

    // Handle rgb/rgba
    const rgbMatch = trimmed.match(/^rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*[\d.]+)?\)$/)
    if (rgbMatch) {
        return `rgba(${rgbMatch[1]}, ${rgbMatch[2]}, ${rgbMatch[3]}, ${alpha})`
    }

    // Handle hex colors
    if (trimmed.startsWith('#')) {
        const hex = trimmed.slice(1)
        
        // 3-digit hex (#rgb)
        if (hex.length === 3) {
            // Validate hex characters
            if (!/^[0-9a-fA-F]{3}$/.test(hex)) {
                return trimmed // Fallback for invalid hex
            }
            const r = Number.parseInt(hex[0] + hex[0], 16)
            const g = Number.parseInt(hex[1] + hex[1], 16)
            const b = Number.parseInt(hex[2] + hex[2], 16)
            return `rgba(${r}, ${g}, ${b}, ${alpha})`
        }
        
        // 6-digit hex (#rrggbb)
        if (hex.length === 6) {
            // Validate hex characters
            if (!/^[0-9a-fA-F]{6}$/.test(hex)) {
                return trimmed // Fallback for invalid hex
            }
            const r = Number.parseInt(hex.slice(0, 2), 16)
            const g = Number.parseInt(hex.slice(2, 4), 16)
            const b = Number.parseInt(hex.slice(4, 6), 16)
            return `rgba(${r}, ${g}, ${b}, ${alpha})`
        }
    }

    // Fallback: return original color
    return trimmed
}

const PRESET_ICONS = {
    default: Sun,
    'dark-minimal': Moon,
}

const FONT_OPTIONS = [
    { value: 'Inter', label: 'Inter' },
    { value: 'Roboto', label: 'Roboto' },
    { value: 'Open Sans', label: 'Open Sans' },
    { value: 'Lato', label: 'Lato' },
    { value: 'Poppins', label: 'Poppins' },
    { value: 'Montserrat', label: 'Montserrat' },
    { value: 'Outfit', label: 'Outfit' },
    { value: 'Cairo', label: 'Cairo' },
    { value: 'Tajawal', label: 'Tajawal' },
    { value: 'Noto Sans Arabic', label: 'Noto Sans Arabic' },
]

const HEX_COLOR_PATTERN = /^#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$/
const ALLOWED_FONTS = new Set(FONT_OPTIONS.map((option) => option.value))
const COLOR_FIELDS = [
    { key: 'primary', labelEn: 'Primary Color', labelAr: 'اللون الأساسي' },
    { key: 'secondary', labelEn: 'Secondary Color', labelAr: 'اللون الثانوي' },
    { key: 'background', labelEn: 'Background', labelAr: 'الخلفية' },
    { key: 'text', labelEn: 'Text Color', labelAr: 'لون النص' },
    { key: 'muted', labelEn: 'Muted Color', labelAr: 'لون النص الثانوي' },
]

export const validateThemeCandidate = ({ colors, font, baseSize }) => {
    const colorErrors = Object.fromEntries(COLOR_FIELDS
        .filter(({ key }) => !HEX_COLOR_PATTERN.test(colors?.[key] || ''))
        .map(({ key }) => [key, 'Use #RGB or #RRGGBB with no spaces.']))
    const fontError = ALLOWED_FONTS.has(font) ? '' : 'Select a supported font family.'
    const baseSizeError = /^(?:1[2-9]|2[0-4])px$/.test(baseSize || '')
        ? ''
        : 'Base size must be between 12px and 24px.'

    return {
        colors: colorErrors,
        font: fontError,
        baseSize: baseSizeError,
        isValid: Object.keys(colorErrors).length === 0 && !fontError && !baseSizeError,
    }
}

const normalizeBorderRadius = (value) => {
    if (typeof value === 'number' && Number.isFinite(value)) {
        return Math.max(0, value)
    }

    if (typeof value === 'string') {
        const trimmed = value.trim()
        
        // Handle "px" suffix (e.g., "12px" or "12.5px")
        if (trimmed.endsWith('px')) {
            const parsed = Number.parseFloat(trimmed.slice(0, -2))
            if (Number.isFinite(parsed)) {
                return Math.max(0, parsed)
            }
        }
        
        // Handle unitless numeric strings (e.g., "12" or "12.5")
        if (/^-?\d+(\.\d+)?$/.test(trimmed)) {
            const parsed = Number.parseFloat(trimmed)
            if (Number.isFinite(parsed)) {
                return Math.max(0, parsed)
            }
        }
        
        // Unsupported format (e.g., "0.5rem", "1em") - fall back to default
    }

    return DEFAULT_THEME.borderRadius
}

const normalizeColors = (colors) => ({
    primary: colors?.primary || DEFAULT_THEME.colors.primary,
    secondary: colors?.secondary || DEFAULT_THEME.colors.secondary,
    background: colors?.background || DEFAULT_THEME.colors.background,
    text: colors?.text || DEFAULT_THEME.colors.text,
    muted: colors?.muted || DEFAULT_THEME.colors.muted,
})

const normalizeThemeState = (theme, fallbackPresetKey = DEFAULT_THEME.presetKey) => ({
    presetKey: theme?.presetKey || fallbackPresetKey || DEFAULT_THEME.presetKey,
    colors: normalizeColors(theme?.colors),
    font: theme?.font || DEFAULT_THEME.font,
    baseSize: theme?.baseSize || DEFAULT_THEME.baseSize,
    borderRadius: normalizeBorderRadius(theme?.borderRadius),
})

// The admin contract always supplies complete, materialized draft/live values.
// Missing fields are not permission to replace merchant content with defaults.
const requireMaterializedThemes = (response) => {
    for (const theme of [response?.draftTheme, response?.liveTheme]) {
        if (!theme || typeof theme !== 'object'
            || !COLOR_FIELDS.every(({ key }) => typeof theme.colors?.[key] === 'string' && theme.colors[key].length > 0)
            || typeof theme.presetKey !== 'string' || !theme.presetKey
            || typeof theme.font !== 'string' || !theme.font
            || typeof theme.baseSize !== 'string' || !theme.baseSize
            || theme.borderRadius == null) {
            throw new Error('Complete materialized draft and live themes are required.')
        }
    }
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

const mapPresetResponse = (preset) => {
    const baseTheme = preset?.baseTheme || {}
    const typography = baseTheme.typography || {}
    const radius = baseTheme.radius || {}

    return {
        key: preset?.key || DEFAULT_THEME.presetKey,
        name: preset?.name || preset?.key || DEFAULT_THEME.presetKey,
        icon: PRESET_ICONS[preset?.key] || Sparkles,
        colors: normalizeColors(baseTheme.colors),
        font: typography.fontFamily || DEFAULT_THEME.font,
        baseSize: typography.baseSize || DEFAULT_THEME.baseSize,
        borderRadius: normalizeBorderRadius(radius.base),
    }
}

const ensurePresetOption = (presets, themeState) => {
    if (presets.some((preset) => preset.key === themeState.presetKey)) {
        return presets
    }

    return [
        ...presets,
        {
            key: themeState.presetKey,
            name: themeState.presetKey,
            icon: PRESET_ICONS[themeState.presetKey] || Sparkles,
            colors: themeState.colors,
            font: themeState.font,
            baseSize: themeState.baseSize,
            borderRadius: themeState.borderRadius,
        },
    ]
}

export function ThemeEditorPage() {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const { showSuccess, showInfo } = useToast()
    const { user } = useAuthStore()

    const [isLoading, setIsLoading] = useState(true)
    const [isSaving, setIsSaving] = useState(false)
    const [isPublishing, setIsPublishing] = useState(false)
    const [error, setError] = useState(null)
    const [isDirty, setIsDirty] = useState(false)
    const [feedback, setFeedback] = useState(null)
    const [conflict, setConflict] = useState(null)
    const [isPublishConfirmOpen, setIsPublishConfirmOpen] = useState(false)
    const previewRef = useRef(null)
    const editGenerationRef = useRef(0)
    const mutationInFlightRef = useRef(false)
    const mountedRef = useRef(true)

    const [themePresets, setThemePresets] = useState([
        {
            key: DEFAULT_THEME.presetKey,
            name: 'Default Modern',
            icon: PRESET_ICONS.default,
            colors: DEFAULT_THEME.colors,
            font: DEFAULT_THEME.font,
            baseSize: DEFAULT_THEME.baseSize,
            borderRadius: DEFAULT_THEME.borderRadius,
        },
    ])
    const [selectedPreset, setSelectedPreset] = useState(DEFAULT_THEME.presetKey)
    const [colors, setColors] = useState(DEFAULT_THEME.colors)
    const [font, setFont] = useState(DEFAULT_THEME.font)
    const [baseSize, setBaseSize] = useState(DEFAULT_THEME.baseSize)
    const [borderRadius, setBorderRadius] = useState(DEFAULT_THEME.borderRadius)
    const [, setPublishedState] = useState(DEFAULT_THEME)
    const [acceptedDraftState, setAcceptedDraftState] = useState(DEFAULT_THEME)
    const [acceptedRowVersion, setAcceptedRowVersion] = useState('')

    useEffect(() => {
        mountedRef.current = true
        void loadThemeData()
        return () => { mountedRef.current = false }
    }, [])

    const applyThemeState = (themeState) => {
        setSelectedPreset(themeState.presetKey)
        setColors(themeState.colors)
        setFont(themeState.font)
        setBaseSize(themeState.baseSize)
        setBorderRadius(themeState.borderRadius)
    }

    const syncThemeResponse = (response, currentPresets, { applyCandidate = true } = {}) => {
        const acceptedResponse = deriveCurrentStoreIdentityPresentation(response, user)
        if (!acceptedResponse) {
            throw new Error('Theme response was rejected by backend store authority admission.')
        }
        requireMaterializedThemes(acceptedResponse)

        const liveTheme = normalizeThemeState(
            acceptedResponse.liveTheme,
            acceptedResponse.themePresetKey || DEFAULT_THEME.presetKey
        )
        const draftTheme = normalizeThemeState(
            acceptedResponse.draftTheme || acceptedResponse.liveTheme,
            acceptedResponse.themePresetKey || liveTheme.presetKey
        )

        const nextPresets = ensurePresetOption(
            ensurePresetOption(currentPresets, liveTheme),
            draftTheme
        )

        setThemePresets(nextPresets)
        if (applyCandidate) {
            applyThemeState(draftTheme)
        }
        setAcceptedDraftState(draftTheme)
        setPublishedState(liveTheme)
        const rowVersion = normalizeRowVersion(acceptedResponse.rowVersion)
        setAcceptedRowVersion(rowVersion)

        return { draftTheme, liveTheme, nextPresets, rowVersion }
    }

    const loadThemeData = async () => {
        setIsLoading(true)
        setError(null)

        try {
            const [rawConfig, presetResponse] = await Promise.all([
                storeConfigService.getStoreConfig(),
                storeConfigService.getThemePresets(),
            ])
            const config = deriveCurrentStoreIdentityPresentation(rawConfig, user)
            if (!config) {
                throw new Error('Theme configuration was rejected by backend store authority admission.')
            }
            requireMaterializedThemes(config)

            const presetOptions = (presetResponse || []).map(mapPresetResponse)
            const liveTheme = normalizeThemeState(
                config?.liveTheme,
                config?.themePresetKey || DEFAULT_THEME.presetKey
            )
            const draftTheme = normalizeThemeState(
                config?.draftTheme || config?.liveTheme,
                config?.themePresetKey || liveTheme.presetKey
            )

            const hydratedPresets = ensurePresetOption(
                ensurePresetOption(
                    presetOptions.length > 0
                        ? presetOptions
                        : [
                            {
                                key: DEFAULT_THEME.presetKey,
                                name: 'Default Modern',
                                icon: PRESET_ICONS.default,
                                colors: DEFAULT_THEME.colors,
                                font: DEFAULT_THEME.font,
                                baseSize: DEFAULT_THEME.baseSize,
                                borderRadius: DEFAULT_THEME.borderRadius,
                            },
                        ],
                    liveTheme
                ),
                draftTheme
            )

            setThemePresets(hydratedPresets)
            applyThemeState(draftTheme)
            setAcceptedDraftState(draftTheme)
            setPublishedState(liveTheme)
            setAcceptedRowVersion(normalizeRowVersion(config?.rowVersion))
            setIsDirty(false)
            return true
        } catch (err) {
            console.error('Failed to load theme:', err)
            setError(isRTL ? 'فشل تحميل إعدادات الثيم' : 'Failed to load theme settings')
            return false
        } finally {
            setIsLoading(false)
        }
    }

    const persistDraftTheme = async ({ silent = false, rowVersionOverride = null } = {}) => {
        const rowVersion = normalizeRowVersion(rowVersionOverride || acceptedRowVersion)
        if (!rowVersion) {
            throw new Error('Theme mutation requires accepted backend RowVersion evidence.')
        }

        const submittedGeneration = editGenerationRef.current
        const response = await storeConfigService.updateThemeOverrides({
            presetKey: selectedPreset,
            colors,
            typography: {
                fontFamily: font,
                baseSize,
            },
            borderRadius,
        }, buildIfMatchOptions(rowVersion))

        if (!mountedRef.current) return { nextPresets: themePresets, rowVersion: '', localEditsRetained: true }
        const localEditsRetained = editGenerationRef.current !== submittedGeneration
        const { nextPresets, rowVersion: acceptedResponseRowVersion } = syncThemeResponse(
            response,
            themePresets,
            { applyCandidate: !localEditsRetained }
        )
        setIsDirty(localEditsRetained)

        if (!silent) {
            if (localEditsRetained) {
                showInfo(isRTL ? 'تم حفظ المسودة مع الاحتفاظ بالتعديلات الأحدث' : 'Draft saved. Newer unsaved edits were retained.')
            } else {
                showSuccess(isRTL ? 'تم حفظ المسودة' : 'Draft saved')
            }
        }

        return { nextPresets, rowVersion: acceptedResponseRowVersion, localEditsRetained }
    }

    const markDirty = () => {
        editGenerationRef.current += 1
        setIsDirty(true)
    }

    const handlePresetSelect = (preset) => {
        setSelectedPreset(preset.key)
        setColors({ ...preset.colors })
        setFont(preset.font)
        setBaseSize(preset.baseSize)
        setBorderRadius(preset.borderRadius)
        markDirty()
    }

    const handleColorChange = (key, value) => {
        setColors((current) => ({ ...current, [key]: value }))
        markDirty()
    }

    const handleFontChange = (value) => {
        setFont(value)
        markDirty()
    }

    const handleBaseSizeChange = (value) => {
        setBaseSize(value)
        markDirty()
    }

    const handleBorderRadiusChange = (value) => {
        setBorderRadius(value)
        markDirty()
    }

    const handleSaveDraft = async (rowVersionOverride = null) => {
        if (mutationInFlightRef.current || !validateThemeCandidate({ colors, font, baseSize }).isValid) return
        mutationInFlightRef.current = true
        setIsSaving(true)
        try {
            const { localEditsRetained } = await persistDraftTheme({ rowVersionOverride })
            setFeedback(localEditsRetained
                ? { kind: 'info', title: 'Appearance draft saved', message: 'Newer unsaved edits were retained for the next save.' }
                : { kind: 'success', title: 'Appearance draft saved', message: 'The accepted Appearance draft is up to date.' })
        } catch (err) {
            console.error('Failed to save draft:', err)
            if (err?.response?.status === 409) setConflict({ action: 'save', message: err?.response?.data?.message })
            else setFeedback({ kind: 'error', title: 'Appearance draft not saved', message: err?.response?.data?.message || 'Failed to save draft.' })
        } finally {
            mutationInFlightRef.current = false
            setIsSaving(false)
        }
    }

    const handlePublish = async (rowVersionOverride = null) => {
        if (mutationInFlightRef.current || !validateThemeCandidate({ colors, font, baseSize }).isValid) return
        mutationInFlightRef.current = true
        setIsPublishing(true)
        try {
            const { nextPresets: latestPresets, rowVersion: draftRowVersion, localEditsRetained } = await persistDraftTheme({ silent: true, rowVersionOverride })
            if (!mountedRef.current) return
            if (localEditsRetained) {
                setIsPublishConfirmOpen(false)
                setFeedback({ kind: 'info', title: 'Draft saved, publication paused', message: 'Newer unsaved edits were retained. Review and publish again.' })
                return
            }
            const rowVersion = normalizeRowVersion(draftRowVersion)
            if (!rowVersion) {
                throw new Error('Theme publish requires accepted backend RowVersion evidence.')
            }

            const publishGeneration = editGenerationRef.current
            const response = await storeConfigService.publishTheme(buildIfMatchOptions(rowVersion))
            const localEditsRetainedAfterPublish = editGenerationRef.current !== publishGeneration
            syncThemeResponse(response, latestPresets, { applyCandidate: !localEditsRetainedAfterPublish })
            setIsDirty(localEditsRetainedAfterPublish)
            setIsPublishConfirmOpen(false)
            setFeedback(localEditsRetainedAfterPublish
                ? { kind: 'info', title: 'Colors & Typography published', message: 'The accepted draft is live. Newer local edits were retained.' }
                : { kind: 'success', title: 'Colors & Typography published', message: 'The accepted Appearance draft is now live.' })
        } catch (err) {
            console.error('Failed to publish theme:', err)
            if (err?.response?.status === 409) {
                setIsPublishConfirmOpen(false)
                setConflict({ action: 'publish', message: err?.response?.data?.message })
            }
            else setFeedback({ kind: 'error', title: 'Appearance not published', message: err?.response?.data?.message || 'Failed to publish Colors & Typography.' })
        } finally {
            mutationInFlightRef.current = false
            setIsPublishing(false)
        }
    }

    const handleResetToPublished = () => {
        if (!acceptedDraftState) {
            return
        }

        applyThemeState(acceptedDraftState)
        setIsDirty(false)
        setFeedback({ kind: 'info', title: 'Changes discarded', message: 'The accepted Appearance draft has been restored.' })
    }

    const candidateValidation = validateThemeCandidate({ colors, font, baseSize })

    const { requestNavigation, unsavedDialogProps } = useAuthoringGuard({
        isDirty,
        isBusy: isSaving || isPublishing,
        onSave: () => handleSaveDraft(),
        onPreview: () => previewRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' }),
    })

    const handleLoadLatest = async () => {
        setConflict(null)
        setFeedback(null)
        if (await loadThemeData()) {
            setFeedback({ kind: 'info', title: 'Latest version loaded', message: 'The accepted Appearance draft replaced your local candidate.' })
        }
    }

    const handleRetryConflict = async () => {
        try {
            const rawConfig = await storeConfigService.getStoreConfig()
            if (!mountedRef.current) return
            const config = deriveCurrentStoreIdentityPresentation(rawConfig, user)
            const latestRowVersion = normalizeRowVersion(config?.rowVersion)
            if (!latestRowVersion) throw new Error('Latest Appearance version could not be admitted.')
            setAcceptedRowVersion(latestRowVersion)
            const action = conflict?.action
            setConflict(null)
            if (action === 'publish') await handlePublish(latestRowVersion)
            else await handleSaveDraft(latestRowVersion)
        } catch (retryError) {
            setFeedback({ kind: 'error', title: 'Appearance retry failed', message: retryError?.response?.data?.message || retryError.message })
        }
    }

    if (isLoading) {
        return <AuthoringLoadingState label="Loading Colors & Typography…" />
    }

    if (error) {
        return (
            <div className="max-w-4xl mx-auto">
                <div className="p-6 bg-red-50 border border-red-200 rounded-xl flex items-center gap-4">
                    <AlertCircle size={24} className="text-red-500 flex-shrink-0" />
                    <div>
                        <p className="font-medium text-red-800">{error}</p>
                        <button
                            onClick={() => void loadThemeData()}
                            className="text-sm text-red-600 hover:underline mt-1"
                        >
                            {isRTL ? 'إعادة المحاولة' : 'Try again'}
                        </button>
                    </div>
                </div>
            </div>
        )
    }

    return (
        <div className="max-w-6xl mx-auto">
            <AuthoringActionBar
                title="Colors & Typography"
                subtitle="Edit the Appearance draft, preview the local candidate below, then publish only Colors & Typography."
                backAction={{ label: 'Back', onClick: () => requestNavigation('/storefront') }}
                status={<AuthoringStatusBadge state={isPublishing ? 'publishing' : isSaving ? 'saving' : isDirty ? 'dirty' : 'draft'} />}
                previewAction={{ label: 'Preview Candidate', onClick: () => previewRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' }), disabled: isSaving || isPublishing }}
                historyAction={{ label: 'StoreConfig History', onClick: () => requestNavigation('/storefront/storeconfig-version-history') }}
                discardAction={{ label: 'Discard', onClick: handleResetToPublished, disabled: !isDirty || isSaving || isPublishing }}
                saveAction={{ label: 'Save Draft', busyLabel: 'Saving…', onClick: () => handleSaveDraft(), disabled: !isDirty || isPublishing || !candidateValidation.isValid, busy: isSaving }}
                publishAction={{ label: 'Publish Colors & Typography', busyLabel: 'Publishing…', onClick: () => setIsPublishConfirmOpen(true), disabled: isSaving || !candidateValidation.isValid, busy: isPublishing }}
            />
            {feedback ? <div className="my-6"><AuthoringFeedback {...feedback} /></div> : null}

            <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
                <div className="lg:col-span-2 space-y-8">
                    <div className="bg-white rounded-xl border border-gray-200 p-6">
                        <h2 className="text-lg font-semibold text-gray-900 mb-4">
                            {isRTL ? 'القوالب الجاهزة' : 'Theme Presets'}
                        </h2>
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                            {themePresets.map((preset) => {
                                const Icon = preset.icon
                                const isSelected = selectedPreset === preset.key

                                return (
                                    <button
                                        key={preset.key}
                                        onClick={() => handlePresetSelect(preset)}
                                        className={`p-4 rounded-xl border-2 transition-all text-left ${
                                            isSelected
                                                ? 'border-indigo-500 bg-indigo-50 ring-2 ring-indigo-200'
                                                : 'border-gray-200 hover:border-gray-300'
                                        }`}
                                    >
                                        <div className="flex items-start gap-4">
                                            <div
                                                className="w-12 h-12 rounded-lg flex items-center justify-center flex-shrink-0"
                                                style={{ backgroundColor: preset.colors.primary }}
                                            >
                                                <Icon size={24} className="text-white" />
                                            </div>
                                            <div className="min-w-0 flex-1">
                                                <div className="flex items-center justify-between gap-3">
                                                    <p className="font-medium text-gray-900 truncate">{preset.name}</p>
                                                    {isSelected ? <Check size={16} className="text-indigo-600 flex-shrink-0" /> : null}
                                                </div>
                                                <p className="text-xs text-gray-500 mt-1">{preset.key}</p>
                                                <div className="flex items-center gap-2 mt-3">
                                                    {Object.values(preset.colors).slice(0, 4).map((color) => (
                                                        <span
                                                            key={`${preset.key}-${color}`}
                                                            className="w-5 h-5 rounded-full border border-gray-200"
                                                            style={{ backgroundColor: color }}
                                                        />
                                                    ))}
                                                </div>
                                            </div>
                                        </div>
                                    </button>
                                )
                            })}
                        </div>
                    </div>

                    <div className="bg-white rounded-xl border border-gray-200 p-6">
                        <h2 className="text-lg font-semibold text-gray-900 mb-4">
                            {isRTL ? 'الألوان' : 'Colors'}
                        </h2>
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                            {COLOR_FIELDS.map((color) => (
                                <div key={color.key}>
                                    <label htmlFor={`appearance-${color.key}`} className="block text-sm font-medium text-gray-700 mb-2">
                                        {isRTL ? color.labelAr : color.labelEn}
                                    </label>
                                    <div className="flex items-center gap-3">
                                        <input
                                            type="color"
                                            value={colors[color.key]}
                                            onChange={(event) => handleColorChange(color.key, event.target.value)}
                                            className="w-12 h-12 rounded-lg cursor-pointer border border-gray-300"
                                        />
                                        <input
                                            type="text"
                                            id={`appearance-${color.key}`}
                                            value={colors[color.key]}
                                            onChange={(event) => handleColorChange(color.key, event.target.value)}
                                            aria-invalid={candidateValidation.colors[color.key] ? 'true' : undefined}
                                            aria-describedby={candidateValidation.colors[color.key] ? `appearance-${color.key}-error` : undefined}
                                            className="flex-1 px-3 py-2 border border-gray-300 rounded-lg font-mono text-sm"
                                        />
                                    </div>
                                    {candidateValidation.colors[color.key] ? (
                                        <p id={`appearance-${color.key}-error`} role="alert" className="mt-2 text-xs text-red-600">
                                            {candidateValidation.colors[color.key]}
                                        </p>
                                    ) : null}
                                </div>
                            ))}
                        </div>
                    </div>

                    <div className="bg-white rounded-xl border border-gray-200 p-6">
                        <h2 className="text-lg font-semibold text-gray-900 mb-4">
                            {isRTL ? 'الخطوط والتنسيق' : 'Typography & Styling'}
                        </h2>
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                            <div>
                                <label htmlFor="appearance-font-family" className="block text-sm font-medium text-gray-700 mb-2">
                                    {isRTL ? 'نوع الخط' : 'Font Family'}
                                </label>
                                <select
                                    id="appearance-font-family"
                                    value={font}
                                    onChange={(event) => handleFontChange(event.target.value)}
                                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500"
                                    style={{ fontFamily: font }}
                                >
                                    {FONT_OPTIONS.map((option) => (
                                        <option
                                            key={option.value}
                                            value={option.value}
                                            style={{ fontFamily: option.value }}
                                        >
                                            {option.label}
                                        </option>
                                    ))}
                                </select>
                            </div>

                            <div>
                                <label htmlFor="appearance-base-size" className="block text-sm font-medium text-gray-700 mb-2">
                                    {isRTL ? 'حجم الخط الأساسي' : 'Base Font Size'}: {baseSize}
                                </label>
                                <input
                                    id="appearance-base-size"
                                    type="range"
                                    min="12"
                                    max="24"
                                    value={Number.parseInt(baseSize, 10)}
                                    onChange={(event) => handleBaseSizeChange(`${event.target.value}px`)}
                                    className="w-full h-2 bg-gray-200 rounded-lg appearance-none cursor-pointer"
                                />
                            </div>

                            <div>
                                <label htmlFor="appearance-border-radius" className="block text-sm font-medium text-gray-700 mb-2">
                                    {isRTL ? 'انحناء الحواف' : 'Border Radius'}: {borderRadius}px
                                </label>
                                <input
                                    id="appearance-border-radius"
                                    type="range"
                                    min="0"
                                    max="24"
                                    value={borderRadius}
                                    onChange={(event) => handleBorderRadiusChange(Number.parseInt(event.target.value, 10))}
                                    className="w-full h-2 bg-gray-200 rounded-lg appearance-none cursor-pointer"
                                />
                                <div className="flex justify-between text-xs text-gray-400 mt-1">
                                    <span>{isRTL ? 'حاد' : 'Sharp'}</span>
                                    <span>{isRTL ? 'مستدير' : 'Rounded'}</span>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>

                <div className="lg:col-span-1">
                    <div ref={previewRef} className="sticky top-6" tabIndex={-1} aria-label="Appearance candidate preview">
                        <h2 className="text-lg font-semibold text-gray-900 mb-4">
                            {isRTL ? 'معاينة حية' : 'Live Preview'}
                        </h2>
                        <div
                            className="rounded-xl overflow-hidden border border-gray-200 shadow-lg"
                            style={{
                                backgroundColor: colors.background,
                                fontFamily: font,
                                fontSize: baseSize,
                            }}
                        >
                            <div
                                className="p-4"
                                style={{ backgroundColor: colors.primary }}
                            >
                                <div className="flex items-center justify-between">
                                    <span className="text-white font-bold">Store Name</span>
                                    <div className="flex gap-2">
                                        <div className="w-6 h-6 bg-white/20 rounded" />
                                        <div className="w-6 h-6 bg-white/20 rounded" />
                                    </div>
                                </div>
                            </div>

                            <div className="p-4 space-y-4">
                                <div
                                    className="p-4 text-center"
                                    style={{
                                        backgroundColor: applyAlpha(colors.primary, 0.08),
                                        borderRadius,
                                    }}
                                >
                                    <h3
                                        className="text-lg font-bold mb-2"
                                        style={{ color: colors.text }}
                                    >
                                        Welcome
                                    </h3>
                                    <button
                                        className="px-4 py-2 text-white text-sm font-medium"
                                        style={{
                                            backgroundColor: colors.primary,
                                            borderRadius,
                                        }}
                                    >
                                        Shop Now
                                    </button>
                                </div>

                                <div className="grid grid-cols-2 gap-3">
                                    {[1, 2].map((itemIndex) => (
                                        <div
                                            key={itemIndex}
                                            className="border p-3"
                                            style={{
                                                borderColor: applyAlpha(colors.muted, 0.2),
                                                borderRadius,
                                            }}
                                        >
                                            <div
                                                className="aspect-square mb-2"
                                                style={{
                                                    backgroundColor: applyAlpha(colors.muted, 0.13),
                                                    borderRadius,
                                                }}
                                            />
                                            <p
                                                className="text-sm font-medium"
                                                style={{ color: colors.text }}
                                            >
                                                Product {itemIndex}
                                            </p>
                                            <p
                                                className="text-sm font-bold"
                                                style={{ color: colors.secondary }}
                                            >
                                                $99.99
                                            </p>
                                        </div>
                                    ))}
                                </div>
                            </div>

                            <div
                                className="p-3 text-center text-xs"
                                style={{
                                    backgroundColor: applyAlpha(colors.muted, 0.13),
                                    color: applyAlpha(colors.text, 0.7),
                                }}
                            >
                                © 2026 Your Store
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            <AuthoringConfirmDialog
                open={isPublishConfirmOpen}
                title="Publish Colors & Typography?"
                description="The accepted Appearance draft will become live. This does not activate a Theme or publish any content surface."
                confirmLabel="Publish Colors & Typography"
                confirmTone="publish"
                busy={isPublishing}
                onClose={() => setIsPublishConfirmOpen(false)}
                onConfirm={() => handlePublish()}
            />
            <AuthoringConflictDialog
                open={conflict != null}
                message={conflict?.message}
                busy={isSaving || isPublishing}
                onClose={() => setConflict(null)}
                onLoadLatest={handleLoadLatest}
                onRetry={handleRetryConflict}
            />
            <UnsavedChangesDialog {...unsavedDialogProps} />
        </div>
    )
}

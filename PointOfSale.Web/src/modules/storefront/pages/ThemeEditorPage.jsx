import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
    Palette,
    Check,
    Upload,
    RotateCcw,
    Loader2,
    AlertCircle,
    Sun,
    Moon,
    Sparkles,
} from 'lucide-react'
import { storeConfigService } from '../services'
import { useToast } from '../../../components/Toast'
import { useAuthStore } from '../../../store/authStore'
import { deriveCurrentStoreIdentityPresentation } from '../../../auth/commerceHubVendorIdentityAuthority'

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
    borderRadius: normalizeBorderRadius(theme?.borderRadius),
})

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
            borderRadius: themeState.borderRadius,
        },
    ]
}

export function ThemeEditorPage() {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const { showSuccess, showError, showInfo } = useToast()
    const { user } = useAuthStore()

    const [isLoading, setIsLoading] = useState(true)
    const [isSaving, setIsSaving] = useState(false)
    const [isPublishing, setIsPublishing] = useState(false)
    const [error, setError] = useState(null)
    const [isDirty, setIsDirty] = useState(false)

    const [themePresets, setThemePresets] = useState([
        {
            key: DEFAULT_THEME.presetKey,
            name: 'Default Modern',
            icon: PRESET_ICONS.default,
            colors: DEFAULT_THEME.colors,
            font: DEFAULT_THEME.font,
            borderRadius: DEFAULT_THEME.borderRadius,
        },
    ])
    const [selectedPreset, setSelectedPreset] = useState(DEFAULT_THEME.presetKey)
    const [colors, setColors] = useState(DEFAULT_THEME.colors)
    const [font, setFont] = useState(DEFAULT_THEME.font)
    const [borderRadius, setBorderRadius] = useState(DEFAULT_THEME.borderRadius)
    const [publishedState, setPublishedState] = useState(DEFAULT_THEME)
    const [acceptedRowVersion, setAcceptedRowVersion] = useState('')

    useEffect(() => {
        void loadThemeData()
    }, [])

    const applyThemeState = (themeState) => {
        setSelectedPreset(themeState.presetKey)
        setColors(themeState.colors)
        setFont(themeState.font)
        setBorderRadius(themeState.borderRadius)
    }

    const syncThemeResponse = (response, currentPresets) => {
        const acceptedResponse = deriveCurrentStoreIdentityPresentation(response, user)
        if (!acceptedResponse) {
            throw new Error('Theme response was rejected by backend store authority admission.')
        }

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
        applyThemeState(draftTheme)
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
                                borderRadius: DEFAULT_THEME.borderRadius,
                            },
                        ],
                    liveTheme
                ),
                draftTheme
            )

            setThemePresets(hydratedPresets)
            applyThemeState(draftTheme)
            setPublishedState(liveTheme)
            setAcceptedRowVersion(normalizeRowVersion(config?.rowVersion))
            setIsDirty(false)
        } catch (err) {
            console.error('Failed to load theme:', err)
            setError(isRTL ? 'فشل تحميل إعدادات الثيم' : 'Failed to load theme settings')
        } finally {
            setIsLoading(false)
        }
    }

    const persistDraftTheme = async ({ silent = false } = {}) => {
        const rowVersion = normalizeRowVersion(acceptedRowVersion)
        if (!rowVersion) {
            throw new Error('Theme mutation requires accepted backend RowVersion evidence.')
        }

        const response = await storeConfigService.updateThemeOverrides({
            presetKey: selectedPreset,
            colors,
            font,
            borderRadius,
        }, buildIfMatchOptions(rowVersion))

        const { nextPresets, rowVersion: acceptedResponseRowVersion } = syncThemeResponse(response, themePresets)
        setIsDirty(false)

        if (!silent) {
            showSuccess(isRTL ? 'تم حفظ المسودة' : 'Draft saved')
        }

        return { nextPresets, rowVersion: acceptedResponseRowVersion }
    }

    const handlePresetSelect = (preset) => {
        setSelectedPreset(preset.key)
        setColors({ ...preset.colors })
        setFont(preset.font)
        setBorderRadius(preset.borderRadius)
        setIsDirty(true)
    }

    const handleColorChange = (key, value) => {
        setColors((current) => ({ ...current, [key]: value }))
        setIsDirty(true)
    }

    const handleFontChange = (value) => {
        setFont(value)
        setIsDirty(true)
    }

    const handleBorderRadiusChange = (value) => {
        setBorderRadius(value)
        setIsDirty(true)
    }

    const handleSaveDraft = async () => {
        setIsSaving(true)
        try {
            await persistDraftTheme()
        } catch (err) {
            console.error('Failed to save draft:', err)
            showError(isRTL ? 'فشل حفظ المسودة' : 'Failed to save draft')
        } finally {
            setIsSaving(false)
        }
    }

    const handlePublish = async () => {
        setIsPublishing(true)
        try {
            const { nextPresets: latestPresets, rowVersion: draftRowVersion } = await persistDraftTheme({ silent: true })
            const rowVersion = normalizeRowVersion(draftRowVersion)
            if (!rowVersion) {
                throw new Error('Theme publish requires accepted backend RowVersion evidence.')
            }

            const response = await storeConfigService.publishTheme(buildIfMatchOptions(rowVersion))
            syncThemeResponse(response, latestPresets)
            setIsDirty(false)
            showSuccess(isRTL ? 'تم نشر الثيم بنجاح!' : 'Theme published successfully!')
        } catch (err) {
            console.error('Failed to publish theme:', err)
            showError(isRTL ? 'فشل نشر الثيم' : 'Failed to publish theme')
        } finally {
            setIsPublishing(false)
        }
    }

    const handleResetToPublished = () => {
        if (!publishedState) {
            return
        }

        applyThemeState(publishedState)
        setIsDirty(false)
        showInfo(isRTL ? 'تمت إعادة التعيين للنسخة المنشورة' : 'Reset to published version')
    }

    if (isLoading) {
        return (
            <div className="flex items-center justify-center h-96">
                <div className="text-center">
                    <Loader2 size={48} className="animate-spin text-indigo-600 mx-auto mb-4" />
                    <p className="text-gray-500">{isRTL ? 'جاري تحميل الثيم...' : 'Loading theme...'}</p>
                </div>
            </div>
        )
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
            <div className="flex items-center justify-between mb-8">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900 flex items-center gap-3">
                        <Palette className="text-indigo-600" />
                        {isRTL ? 'تخصيص الثيم' : 'Theme Customization'}
                    </h1>
                    <p className="text-gray-500 mt-1">
                        {isRTL
                            ? 'خصص ألوان وخطوط متجرك الإلكتروني بالاعتماد على إعداداتك الفعلية.'
                            : 'Customize your live storefront theme using the same preset and draft data the backend renders.'
                        }
                    </p>
                </div>

                <div className="flex items-center gap-3">
                    {isDirty && (
                        <span className="text-sm text-yellow-600 bg-yellow-100 px-3 py-1 rounded-full">
                            {isRTL ? 'تغييرات غير محفوظة' : 'Unsaved changes'}
                        </span>
                    )}
                    <button
                        onClick={handleResetToPublished}
                        disabled={!publishedState || isPublishing}
                        className="flex items-center gap-2 px-4 py-2 text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50 transition-colors"
                    >
                        <RotateCcw size={16} />
                        {isRTL ? 'إعادة تعيين' : 'Reset'}
                    </button>
                    <button
                        onClick={handleSaveDraft}
                        disabled={!isDirty || isSaving || isPublishing}
                        className="flex items-center gap-2 px-4 py-2 text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50 transition-colors"
                    >
                        {isSaving ? <Loader2 size={16} className="animate-spin" /> : <Check size={16} />}
                        {isSaving ? (isRTL ? 'جاري الحفظ...' : 'Saving...') : (isRTL ? 'حفظ المسودة' : 'Save Draft')}
                    </button>
                    <button
                        onClick={handlePublish}
                        disabled={isPublishing || isSaving}
                        className="flex items-center gap-2 px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50 transition-colors"
                    >
                        {isPublishing ? <Loader2 size={16} className="animate-spin" /> : <Upload size={16} />}
                        {isPublishing ? (isRTL ? 'جاري النشر...' : 'Publishing...') : (isRTL ? 'نشر الثيم' : 'Publish Theme')}
                    </button>
                </div>
            </div>

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
                            {[
                                { key: 'primary', labelEn: 'Primary Color', labelAr: 'اللون الأساسي' },
                                { key: 'secondary', labelEn: 'Secondary Color', labelAr: 'اللون الثانوي' },
                                { key: 'background', labelEn: 'Background', labelAr: 'الخلفية' },
                                { key: 'text', labelEn: 'Text Color', labelAr: 'لون النص' },
                                { key: 'muted', labelEn: 'Muted Color', labelAr: 'لون النص الثانوي' },
                            ].map((color) => (
                                <div key={color.key}>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">
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
                                            value={colors[color.key]}
                                            onChange={(event) => handleColorChange(color.key, event.target.value)}
                                            className="flex-1 px-3 py-2 border border-gray-300 rounded-lg font-mono text-sm"
                                        />
                                    </div>
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
                                <label className="block text-sm font-medium text-gray-700 mb-2">
                                    {isRTL ? 'نوع الخط' : 'Font Family'}
                                </label>
                                <select
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
                                <label className="block text-sm font-medium text-gray-700 mb-2">
                                    {isRTL ? 'انحناء الحواف' : 'Border Radius'}: {borderRadius}px
                                </label>
                                <input
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
                    <div className="sticky top-6">
                        <h2 className="text-lg font-semibold text-gray-900 mb-4">
                            {isRTL ? 'معاينة حية' : 'Live Preview'}
                        </h2>
                        <div
                            className="rounded-xl overflow-hidden border border-gray-200 shadow-lg"
                            style={{
                                backgroundColor: colors.background,
                                fontFamily: font,
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
        </div>
    )
}

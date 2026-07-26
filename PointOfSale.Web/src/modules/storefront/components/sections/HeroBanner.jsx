/**
 * Hero Banner Section
 * Phase 9.8: POS Integration
 */

import { useTranslation } from 'react-i18next'

export const settingsSchema = [
    { key: 'title', type: 'text', label: 'Title' },
    { key: 'subtitle', type: 'textarea', label: 'Subtitle', rows: 2 },
    { key: 'ctaText', type: 'text', label: 'CTA Text' },
    { key: 'ctaLink', type: 'text', label: 'CTA Link' },
    { key: 'backgroundImage', type: 'image', label: 'Background Image' },
    { key: 'backgroundColor', type: 'color', label: 'Background Color' },
    { key: 'overlayOpacity', type: 'number', label: 'Overlay Opacity', min: 0, max: 100 },
    { key: 'textColor', type: 'color', label: 'Text Color' },
    {
        key: 'alignment',
        type: 'select',
        label: 'Alignment',
        options: [
            { value: 'left', label: 'Left' },
            { value: 'center', label: 'Center' },
            { value: 'right', label: 'Right' },
        ],
    },
    {
        key: 'height',
        type: 'select',
        label: 'Height',
        options: [
            { value: 'small', label: 'Small' },
            { value: 'medium', label: 'Medium' },
            { value: 'large', label: 'Large' },
        ],
    },
]

/**
 * Hero Banner Editor
 */
export function HeroBannerEditor({ section, onUpdate }) {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const settings = section.settings

    const handleChange = (key, value) => {
        onUpdate({ [key]: value })
    }

    return (
        <div className="space-y-4">
            <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                    {isRTL ? 'العنوان' : 'Title'}
                </label>
                <input
                    type="text"
                    value={settings.title || ''}
                    onChange={(e) => handleChange('title', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                />
            </div>

            <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                    {isRTL ? 'العنوان الفرعي' : 'Subtitle'}
                </label>
                <textarea
                    value={settings.subtitle || ''}
                    onChange={(e) => handleChange('subtitle', e.target.value)}
                    rows={2}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                />
            </div>

            <div className="grid grid-cols-2 gap-4">
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                        {isRTL ? 'نص الزر' : 'CTA Text'}
                    </label>
                    <input
                        type="text"
                        value={settings.ctaText || ''}
                        onChange={(e) => handleChange('ctaText', e.target.value)}
                        className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                        {isRTL ? 'رابط الزر' : 'CTA Link'}
                    </label>
                    <input
                        type="text"
                        value={settings.ctaLink || ''}
                        onChange={(e) => handleChange('ctaLink', e.target.value)}
                        className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                    />
                </div>
            </div>

            <div className="grid grid-cols-2 gap-4">
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                        {isRTL ? 'لون الخلفية' : 'Background Color'}
                    </label>
                    <input
                        type="color"
                        value={settings.backgroundColor || '#1e40af'}
                        onChange={(e) => handleChange('backgroundColor', e.target.value)}
                        className="w-full h-10 rounded-lg cursor-pointer"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                        {isRTL ? 'لون النص' : 'Text Color'}
                    </label>
                    <input
                        type="color"
                        value={settings.textColor || '#ffffff'}
                        onChange={(e) => handleChange('textColor', e.target.value)}
                        className="w-full h-10 rounded-lg cursor-pointer"
                    />
                </div>
            </div>

            <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                    {isRTL ? 'المحاذاة' : 'Alignment'}
                </label>
                <select
                    value={settings.alignment || 'center'}
                    onChange={(e) => handleChange('alignment', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                >
                    <option value="left">{isRTL ? 'يسار' : 'Left'}</option>
                    <option value="center">{isRTL ? 'وسط' : 'Center'}</option>
                    <option value="right">{isRTL ? 'يمين' : 'Right'}</option>
                </select>
            </div>

            <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                    {isRTL ? 'الارتفاع' : 'Height'}
                </label>
                <select
                    value={settings.height || 'medium'}
                    onChange={(e) => handleChange('height', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                >
                    <option value="small">{isRTL ? 'صغير' : 'Small'}</option>
                    <option value="medium">{isRTL ? 'متوسط' : 'Medium'}</option>
                    <option value="large">{isRTL ? 'كبير' : 'Large'}</option>
                </select>
            </div>
        </div>
    )
}

/**
 * Hero Banner Preview
 */
export function HeroBannerPreview({ section }) {
    const settings = section.settings
    const overlayOpacity = Math.min(Math.max(Number(settings.overlayOpacity ?? 45), 0), 100) / 100

    const heightClass = {
        small: 'py-12',
        medium: 'py-20',
        large: 'py-32',
    }[settings.height || 'medium']

    const alignmentClass = {
        left: 'text-left items-start',
        center: 'text-center items-center',
        right: 'text-right items-end',
    }[settings.alignment || 'center']

    return (
        <div
            className={`relative overflow-hidden ${heightClass} px-8 flex flex-col justify-center ${alignmentClass}`}
            style={{
                backgroundColor: settings.backgroundColor || '#1e40af',
                color: settings.textColor || '#ffffff',
            }}
        >
            {settings.backgroundImage && (
                <img
                    src={settings.backgroundImage}
                    alt={settings.title || 'Hero background'}
                    className="absolute inset-0 h-full w-full object-cover"
                />
            )}
            {settings.backgroundImage && (
                <div
                    className="absolute inset-0 bg-black"
                    style={{ opacity: overlayOpacity }}
                />
            )}
            <div className="relative z-10 flex max-w-3xl flex-col">
                <h1 className="text-3xl font-bold mb-3">
                    {settings.title || 'Welcome to Our Store'}
                </h1>
                <p className="text-lg opacity-90 mb-6 max-w-xl">
                    {settings.subtitle || 'Discover amazing products'}
                </p>
                {settings.ctaText && (
                    <button className="px-6 py-3 bg-white text-gray-900 font-semibold rounded-lg hover:bg-gray-100 transition-colors">
                        {settings.ctaText}
                    </button>
                )}
            </div>
        </div>
    )
}

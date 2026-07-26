/**
 * Image Block Section
 * Phase 9.8: POS Integration
 */

import { useTranslation } from 'react-i18next'

export const settingsSchema = [
    { key: 'images', type: 'image-list', label: 'Images' },
    {
        key: 'layout',
        type: 'select',
        label: 'Layout',
        options: [
            { value: 'single', label: 'Single' },
            { value: 'side-by-side', label: 'Side by Side' },
            { value: 'grid', label: 'Grid' },
        ],
    },
    {
        key: 'aspectRatio',
        type: 'select',
        label: 'Aspect Ratio',
        options: [
            { value: '16:9', label: '16:9' },
            { value: '4:3', label: '4:3' },
            { value: '1:1', label: '1:1' },
            { value: 'auto', label: 'Auto' },
        ],
    },
    { key: 'fullWidth', type: 'toggle', label: 'Full Width' },
]

export function ImageBlockEditor({ section, onUpdate }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const settings = section.settings

    const handleChange = (key, value) => {
        onUpdate({ [key]: value })
    }

    return (
        <div className="space-y-4">
            <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                    {isRTL ? 'Ø§Ù„ØªØ®Ø·ÙŠØ·' : 'Layout'}
                </label>
                <select
                    value={settings.layout || 'single'}
                    onChange={(e) => handleChange('layout', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                >
                    <option value="single">{isRTL ? 'ØµÙˆØ±Ø© ÙˆØ§Ø­Ø¯Ø©' : 'Single'}</option>
                    <option value="side-by-side">{isRTL ? 'Ø¬Ù†Ø¨Ø§Ù‹ Ø¥Ù„Ù‰ Ø¬Ù†Ø¨' : 'Side by Side'}</option>
                    <option value="grid">{isRTL ? 'Ø´Ø¨ÙƒØ©' : 'Grid'}</option>
                </select>
            </div>

            <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                    {isRTL ? 'Ù†Ø³Ø¨Ø© Ø§Ù„Ø¹Ø±Ø¶ Ø¥Ù„Ù‰ Ø§Ù„Ø§Ø±ØªÙØ§Ø¹' : 'Aspect Ratio'}
                </label>
                <select
                    value={settings.aspectRatio || '16:9'}
                    onChange={(e) => handleChange('aspectRatio', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                >
                    <option value="16:9">16:9</option>
                    <option value="4:3">4:3</option>
                    <option value="1:1">1:1</option>
                    <option value="auto">{isRTL ? 'ØªÙ„Ù‚Ø§Ø¦ÙŠ' : 'Auto'}</option>
                </select>
            </div>

            <label className="flex items-center gap-2">
                <input
                    type="checkbox"
                    checked={settings.fullWidth ?? false}
                    onChange={(e) => handleChange('fullWidth', e.target.checked)}
                    className="w-4 h-4 text-indigo-600 rounded focus:ring-indigo-500"
                />
                <span className="text-sm text-gray-700">{isRTL ? 'Ø¹Ø±Ø¶ ÙƒØ§Ù…Ù„' : 'Full Width'}</span>
            </label>

            <div className="p-3 bg-gray-50 rounded-lg border border-dashed border-gray-300">
                <p className="text-sm text-gray-500 text-center">
                    {isRTL ? 'ØµÙˆØ± Ø³ÙŠØªÙ… ØªØ­Ù…ÙŠÙ„Ù‡Ø§ Ø¹Ø¨Ø± Ø§Ù„Ù€ CMS' : 'Images will be uploaded via CMS'}
                </p>
            </div>
        </div>
    )
}

export function ImageBlockPreview({ section }) {
    const settings = section.settings
    const layout = settings.layout || 'single'
    const images = Array.isArray(settings.images) && settings.images.length > 0
        ? settings.images
        : [{ url: '' }]

    const cols = layout === 'single' ? 1 : layout === 'side-by-side' ? 2 : 3

    return (
        <div className={`p-4 ${settings.fullWidth ? '' : 'max-w-4xl mx-auto'}`}>
            <div
                className="grid gap-4"
                style={{ gridTemplateColumns: `repeat(${cols}, 1fr)` }}
            >
                {Array.from({ length: Math.max(cols, images.length) }, (_, i) => {
                    const image = images[i]
                    const imageUrl = typeof image === 'string' ? image : image?.url

                    return (
                        <div
                            key={i}
                            className="bg-gray-200 rounded-lg flex items-center justify-center overflow-hidden"
                            style={{
                                aspectRatio: settings.aspectRatio === 'auto' ? undefined : settings.aspectRatio.replace(':', '/'),
                                minHeight: settings.aspectRatio === 'auto' ? 200 : undefined,
                            }}
                        >
                            {imageUrl ? (
                                <img
                                    src={imageUrl}
                                    alt={`Block image ${i + 1}`}
                                    className="h-full w-full object-cover"
                                />
                            ) : (
                                <span className="text-gray-400 text-4xl">ðŸ–¼ï¸</span>
                            )}
                        </div>
                    )
                })}
            </div>
        </div>
    )
}

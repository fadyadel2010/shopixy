/**
 * Testimonials Section
 * Phase 9.8: POS Integration
 */

import { useTranslation } from 'react-i18next'

export const settingsSchema = [
    { key: 'heading', type: 'text', label: 'Heading' },
    {
        key: 'layout',
        type: 'select',
        label: 'Layout',
        options: [
            { value: 'carousel', label: 'Carousel' },
            { value: 'grid', label: 'Grid' },
        ],
    },
    { key: 'showRating', type: 'toggle', label: 'Show Rating' },
    { key: 'autoplay', type: 'toggle', label: 'Autoplay' },
    {
        key: 'testimonials',
        type: 'list',
        label: 'Testimonials',
        itemLabel: 'Testimonial',
        defaultItem: {
            name: '',
            avatar: '',
            rating: 5,
            text: '',
        },
        fields: [
            { key: 'name', type: 'text', label: 'Name' },
            { key: 'avatar', type: 'image', label: 'Avatar' },
            { key: 'rating', type: 'number', label: 'Rating', min: 1, max: 5 },
            { key: 'text', type: 'textarea', label: 'Text', rows: 3 },
        ],
    },
]

export function TestimonialsEditor({ section, onUpdate }) {
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
                    {isRTL ? 'Ø§Ù„Ø¹Ù†ÙˆØ§Ù†' : 'Heading'}
                </label>
                <input
                    type="text"
                    value={settings.heading || ''}
                    onChange={(e) => handleChange('heading', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                />
            </div>

            <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                    {isRTL ? 'Ø§Ù„ØªØ®Ø·ÙŠØ·' : 'Layout'}
                </label>
                <select
                    value={settings.layout || 'carousel'}
                    onChange={(e) => handleChange('layout', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                >
                    <option value="carousel">{isRTL ? 'Ø¹Ø±Ø¶ Ù…ØªØ­Ø±Ùƒ' : 'Carousel'}</option>
                    <option value="grid">{isRTL ? 'Ø´Ø¨ÙƒØ©' : 'Grid'}</option>
                </select>
            </div>

            <label className="flex items-center gap-2">
                <input
                    type="checkbox"
                    checked={settings.showRating ?? true}
                    onChange={(e) => handleChange('showRating', e.target.checked)}
                    className="w-4 h-4 text-indigo-600 rounded focus:ring-indigo-500"
                />
                <span className="text-sm text-gray-700">{isRTL ? 'Ø¹Ø±Ø¶ Ø§Ù„ØªÙ‚ÙŠÙŠÙ…' : 'Show Rating'}</span>
            </label>

            <label className="flex items-center gap-2">
                <input
                    type="checkbox"
                    checked={settings.autoplay ?? true}
                    onChange={(e) => handleChange('autoplay', e.target.checked)}
                    className="w-4 h-4 text-indigo-600 rounded focus:ring-indigo-500"
                />
                <span className="text-sm text-gray-700">{isRTL ? 'ØªØ´ØºÙŠÙ„ ØªÙ„Ù‚Ø§Ø¦ÙŠ' : 'Autoplay'}</span>
            </label>
        </div>
    )
}

export function TestimonialsPreview({ section }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const settings = section.settings
    const testimonials = Array.isArray(settings.testimonials) ? settings.testimonials : []

    return (
        <div className="py-12 px-8 bg-gray-50">
            <h2 className="text-2xl font-bold text-gray-900 text-center mb-8">
                {settings.heading || (isRTL ? 'Ù…Ø§Ø°Ø§ ÙŠÙ‚ÙˆÙ„ Ø¹Ù…Ù„Ø§Ø¤Ù†Ø§' : 'What Our Customers Say')}
            </h2>
            {testimonials.length === 0 ? (
                <div className="rounded-xl border border-dashed border-gray-300 bg-white px-6 py-12 text-center text-sm text-gray-500">
                    {isRTL ? 'Ø£Ø¶Ù Ø´Ù‡Ø§Ø¯Ø§Øª Ø§Ù„Ø¹Ù…Ù„Ø§Ø¡ Ù…Ù† Ù„ÙˆØ­Ø© Ø§Ù„Ø¥Ø¹Ø¯Ø§Ø¯Ø§Øª' : 'Add testimonials from the settings panel'}
                </div>
            ) : (
                <div
                    className={`${settings.layout === 'grid' ? 'grid grid-cols-3 gap-6' : 'flex gap-6 overflow-x-auto'}`}
                >
                    {testimonials.map((t, index) => (
                        <div
                            key={`${t.name || 'testimonial'}-${index}`}
                            className="bg-white rounded-xl p-6 shadow-sm flex-shrink-0"
                            style={{ minWidth: settings.layout === 'carousel' ? 300 : undefined }}
                        >
                            {settings.showRating && (
                                <div className="flex gap-1 mb-3">
                                    {Array.from({ length: Math.max(1, Math.min(Number(t.rating || 5), 5)) }, (_, i) => (
                                        <span key={i} className="text-yellow-400">â˜…</span>
                                    ))}
                                </div>
                            )}
                            <p className="text-gray-600 mb-4">"{t.text}"</p>
                            <div className="flex items-center gap-3">
                                {t.avatar ? (
                                    <img src={t.avatar} alt={t.name} className="h-10 w-10 rounded-full object-cover" />
                                ) : (
                                    <div className="w-10 h-10 bg-indigo-100 rounded-full flex items-center justify-center text-indigo-600 font-semibold">
                                        {(t.name || '?')[0]}
                                    </div>
                                )}
                                <span className="font-medium text-gray-900">{t.name}</span>
                            </div>
                        </div>
                    ))}
                </div>
            )}
        </div>
    )
}

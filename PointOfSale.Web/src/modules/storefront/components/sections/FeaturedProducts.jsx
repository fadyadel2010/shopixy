/**
 * Featured Products Section
 * Phase 9.8: POS Integration
 */

import { useTranslation } from 'react-i18next'

export const settingsSchema = [
    { key: 'title', type: 'text', label: 'Title' },
    { key: 'limit', type: 'number', label: 'Limit', min: 1, max: 12 },
    { key: 'showPrice', type: 'toggle', label: 'Show Price' },
    { key: 'showRating', type: 'toggle', label: 'Show Rating' },
    {
        key: 'displayStyle',
        type: 'select',
        label: 'Display Style',
        options: [
            { value: 'carousel', label: 'Carousel' },
            { value: 'grid', label: 'Grid' },
        ],
    },
    { key: 'showBadge', type: 'toggle', label: 'Show Badge' },
    { key: 'badgeText', type: 'text', label: 'Badge Text' },
]

export function FeaturedProductsEditor({ section, onUpdate }) {
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
                    value={settings.title || settings.heading || ''}
                    onChange={(e) => handleChange('title', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                />
            </div>

            <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                    {isRTL ? 'Ù†Ù…Ø· Ø§Ù„Ø¹Ø±Ø¶' : 'Display Style'}
                </label>
                <select
                    value={settings.displayStyle || 'carousel'}
                    onChange={(e) => handleChange('displayStyle', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                >
                    <option value="carousel">{isRTL ? 'Ø¹Ø±Ø¶ Ù…ØªØ­Ø±Ùƒ' : 'Carousel'}</option>
                    <option value="grid">{isRTL ? 'Ø´Ø¨ÙƒØ©' : 'Grid'}</option>
                </select>
            </div>

            <label className="flex items-center gap-2">
                <input
                    type="checkbox"
                    checked={settings.showBadge ?? true}
                    onChange={(e) => handleChange('showBadge', e.target.checked)}
                    className="w-4 h-4 text-indigo-600 rounded focus:ring-indigo-500"
                />
                <span className="text-sm text-gray-700">{isRTL ? 'Ø¹Ø±Ø¶ Ø§Ù„Ø´Ø§Ø±Ø©' : 'Show Badge'}</span>
            </label>

            {settings.showBadge && (
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                        {isRTL ? 'Ù†Øµ Ø§Ù„Ø´Ø§Ø±Ø©' : 'Badge Text'}
                    </label>
                    <input
                        type="text"
                        value={settings.badgeText || ''}
                        onChange={(e) => handleChange('badgeText', e.target.value)}
                        className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                    />
                </div>
            )}
        </div>
    )
}

export function FeaturedProductsPreview({ section }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const settings = section.settings
    const limit = Math.max(1, Math.min(Number(settings.limit || 4), 12))

    const products = [
        { id: 1, name: isRTL ? 'Ù…Ù†ØªØ¬ Ù…Ù…ÙŠØ² 1' : 'Featured Product 1', price: 149.99 },
        { id: 2, name: isRTL ? 'Ù…Ù†ØªØ¬ Ù…Ù…ÙŠØ² 2' : 'Featured Product 2', price: 199.99 },
        { id: 3, name: isRTL ? 'Ù…Ù†ØªØ¬ Ù…Ù…ÙŠØ² 3' : 'Featured Product 3', price: 249.99 },
        { id: 4, name: isRTL ? 'Ù…Ù†ØªØ¬ Ù…Ù…ÙŠØ² 4' : 'Featured Product 4', price: 99.99 },
        { id: 5, name: isRTL ? 'Ù…Ù†ØªØ¬ Ù…Ù…ÙŠØ² 5' : 'Featured Product 5', price: 129.99 },
        { id: 6, name: isRTL ? 'Ù…Ù†ØªØ¬ Ù…Ù…ÙŠØ² 6' : 'Featured Product 6', price: 89.99 },
    ].slice(0, limit)

    return (
        <div className="py-8 px-6 bg-white">
            <h2 className="text-2xl font-bold text-gray-900 mb-6">
                {settings.title || settings.heading || (isRTL ? 'Ø§Ù„Ù…Ù†ØªØ¬Ø§Øª Ø§Ù„Ù…Ù…ÙŠØ²Ø©' : 'Featured Products')}
            </h2>
            <div
                className={`${settings.displayStyle === 'grid' ? 'grid grid-cols-4 gap-6' : 'flex gap-6 overflow-x-auto'}`}
            >
                {products.map((product) => (
                    <div
                        key={product.id}
                        className="relative bg-gray-50 rounded-xl p-4 flex-shrink-0"
                        style={{ minWidth: settings.displayStyle === 'carousel' ? 220 : undefined }}
                    >
                        {settings.showBadge && (
                            <span className="absolute top-2 right-2 bg-indigo-600 text-white text-xs px-2 py-1 rounded-full">
                                {settings.badgeText || 'Featured'}
                            </span>
                        )}
                        <div className="bg-gray-200 rounded-lg aspect-square mb-3 flex items-center justify-center">
                            <span className="text-gray-400 text-4xl">â­</span>
                        </div>
                        <h3 className="font-medium text-gray-900 mb-1">{product.name}</h3>
                        {settings.showPrice !== false && (
                            <p className="text-indigo-600 font-semibold">${product.price}</p>
                        )}
                        {settings.showRating !== false && (
                            <div className="mt-2 text-xs text-yellow-500">â˜…â˜…â˜…â˜…â˜…</div>
                        )}
                    </div>
                ))}
            </div>
        </div>
    )
}

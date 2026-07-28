/**
 * Product Grid Section
 * Phase 9.8: POS Integration
 */

import { useTranslation } from 'react-i18next'

export const settingsSchema = [
    { key: 'title', type: 'text', label: 'Title' },
    {
        key: 'source',
        type: 'select',
        label: 'Product Source',
        options: [
            { value: 'all', label: 'All Products' },
            { value: 'random', label: 'Random' },
            { value: 'curated', label: 'Curated' },
        ],
    },
    { key: 'columns', type: 'number', label: 'Columns', min: 2, max: 5 },
    { key: 'rows', type: 'number', label: 'Rows', min: 1, max: 4 },
    { key: 'showPrice', type: 'toggle', label: 'Show Price' },
    { key: 'showRating', type: 'toggle', label: 'Show Rating' },
    { key: 'showAddToCart', type: 'toggle', label: 'Add to Cart Button' },
]

/**
 * Product Grid Editor
 */
export function ProductGridEditor({ section, onUpdate }) {
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
                    {isRTL ? 'مصدر المنتجات' : 'Product Source'}
                </label>
                <select
                    value={settings.source || 'all'}
                    onChange={(e) => handleChange('source', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                >
                    <option value="all">{isRTL ? 'كل المنتجات' : 'All Products'}</option>
                    <option value="random">{isRTL ? 'عشوائي' : 'Random'}</option>
                    <option value="curated">{isRTL ? 'مختار' : 'Curated'}</option>
                </select>
            </div>

            <div className="grid grid-cols-2 gap-4">
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                        {isRTL ? 'الأعمدة' : 'Columns'}
                    </label>
                    <select
                        value={settings.columns || 4}
                        onChange={(e) => handleChange('columns', parseInt(e.target.value))}
                        className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                    >
                        <option value={2}>2</option>
                        <option value={3}>3</option>
                        <option value={4}>4</option>
                        <option value={5}>5</option>
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                        {isRTL ? 'الصفوف' : 'Rows'}
                    </label>
                    <select
                        value={settings.rows || 2}
                        onChange={(e) => handleChange('rows', parseInt(e.target.value))}
                        className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                    >
                        <option value={1}>1</option>
                        <option value={2}>2</option>
                        <option value={3}>3</option>
                        <option value={4}>4</option>
                    </select>
                </div>
            </div>

            <div className="space-y-2">
                <label className="flex items-center gap-2">
                    <input
                        type="checkbox"
                        checked={settings.showPrice ?? true}
                        onChange={(e) => handleChange('showPrice', e.target.checked)}
                        className="w-4 h-4 text-indigo-600 rounded focus:ring-indigo-500"
                    />
                    <span className="text-sm text-gray-700">{isRTL ? 'عرض السعر' : 'Show Price'}</span>
                </label>
                <label className="flex items-center gap-2">
                    <input
                        type="checkbox"
                        checked={settings.showRating ?? true}
                        onChange={(e) => handleChange('showRating', e.target.checked)}
                        className="w-4 h-4 text-indigo-600 rounded focus:ring-indigo-500"
                    />
                    <span className="text-sm text-gray-700">{isRTL ? 'عرض التقييم' : 'Show Rating'}</span>
                </label>
                <label className="flex items-center gap-2">
                    <input
                        type="checkbox"
                        checked={settings.showAddToCart ?? true}
                        onChange={(e) => handleChange('showAddToCart', e.target.checked)}
                        className="w-4 h-4 text-indigo-600 rounded focus:ring-indigo-500"
                    />
                    <span className="text-sm text-gray-700">{isRTL ? 'زر أضف للسلة' : 'Add to Cart Button'}</span>
                </label>
            </div>
        </div>
    )
}

/**
 * Product Grid Preview
 */
export function ProductGridPreview({ section }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const settings = section.settings
    const columns = settings.columns || 4
    const rows = settings.rows || 2

    // Mock products for preview
    const mockProducts = Array.from({ length: columns * rows }, (_, i) => ({
        id: i + 1,
        name: isRTL ? `منتج ${i + 1}` : `Product ${i + 1}`,
        price: 99.99,
        rating: 4.5,
    }))

    return (
        <div className="p-6 bg-white">
            {settings.title && (
                <h2 className="text-xl font-bold text-gray-900 mb-4">{settings.title}</h2>
            )}
            <div
                className="grid gap-4"
                style={{ gridTemplateColumns: `repeat(${columns}, 1fr)` }}
            >
                {mockProducts.map((product) => (
                    <div
                        key={product.id}
                        className="border border-gray-200 rounded-lg p-3 hover:shadow-md transition-shadow"
                    >
                        <div className="bg-gray-100 rounded-lg aspect-square mb-3 flex items-center justify-center">
                            <span className="text-gray-400 text-2xl">📦</span>
                        </div>
                        <h3 className="font-medium text-gray-900 text-sm mb-1">{product.name}</h3>
                        {settings.showPrice && (
                            <p className="text-indigo-600 font-semibold text-sm">${product.price}</p>
                        )}
                        {settings.showRating && (
                            <div className="flex items-center gap-1 mt-1">
                                <span className="text-yellow-400 text-xs">★★★★☆</span>
                                <span className="text-xs text-gray-500">{product.rating}</span>
                            </div>
                        )}
                        {settings.showAddToCart && (
                            <button className="w-full mt-2 py-1.5 bg-indigo-600 text-white text-xs font-medium rounded hover:bg-indigo-700 transition-colors">
                                {isRTL ? 'أضف للسلة' : 'Add to Cart'}
                            </button>
                        )}
                    </div>
                ))}
            </div>
        </div>
    )
}

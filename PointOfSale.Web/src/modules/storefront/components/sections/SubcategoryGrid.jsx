/**
 * Subcategory Grid Section
 * Displays subcategories (children of main categories) in a grid
 */

import { useTranslation } from 'react-i18next'

export const settingsSchema = [
    { key: 'title', type: 'text', label: 'Title' },
    { key: 'columns', type: 'number', label: 'Columns', min: 2, max: 4 },
    {
        key: 'cardStyle',
        type: 'select',
        label: 'Card Style',
        options: [
            { value: 'overlay', label: 'Overlay' },
            { value: 'card', label: 'Card' },
            { value: 'minimal', label: 'Minimal' },
        ],
    },
    { key: 'showProductCount', type: 'toggle', label: 'Show Product Count' },
]

/**
 * Subcategory Grid Editor
 */
export function SubcategoryGridEditor({ section, onUpdate }) {
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
                    placeholder={isRTL ? 'الأقسام الفرعية' : 'Subcategories'}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                />
            </div>

            <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                    {isRTL ? 'عدد الأعمدة' : 'Columns'}
                </label>
                <select
                    value={settings.columns || 3}
                    onChange={(e) => handleChange('columns', parseInt(e.target.value))}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                >
                    <option value={2}>2</option>
                    <option value={3}>3</option>
                    <option value={4}>4</option>
                </select>
            </div>

            <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                    {isRTL ? 'نمط البطاقة' : 'Card Style'}
                </label>
                <select
                    value={settings.cardStyle || 'overlay'}
                    onChange={(e) => handleChange('cardStyle', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                >
                    <option value="overlay">{isRTL ? 'تراكب' : 'Overlay'}</option>
                    <option value="card">{isRTL ? 'بطاقة' : 'Card'}</option>
                    <option value="minimal">{isRTL ? 'بسيط' : 'Minimal'}</option>
                </select>
            </div>

            <label className="flex items-center gap-2">
                <input
                    type="checkbox"
                    checked={settings.showProductCount ?? true}
                    onChange={(e) => handleChange('showProductCount', e.target.checked)}
                    className="w-4 h-4 text-indigo-600 rounded focus:ring-indigo-500"
                />
                <span className="text-sm text-gray-700">
                    {isRTL ? 'عرض عدد المنتجات' : 'Show Product Count'}
                </span>
            </label>

            <div className="pt-4 border-t border-gray-200">
                <p className="text-xs text-gray-500">
                    {isRTL
                        ? 'ملاحظة: يعرض هذا القسم الأقسام الفرعية (أبناء الأقسام الرئيسية) تلقائياً.'
                        : 'Note: This section automatically displays subcategories (children of main categories).'}
                </p>
            </div>
        </div>
    )
}

/**
 * Subcategory Grid Preview
 */
export function SubcategoryGridPreview({ section }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const settings = section.settings
    const columns = settings.columns || 3

    // Mock subcategories
    const subcategories = [
        { id: 1, name: isRTL ? 'هواتف' : 'Phones', count: 45 },
        { id: 2, name: isRTL ? 'لابتوب' : 'Laptops', count: 32 },
        { id: 3, name: isRTL ? 'أجهزة لوحية' : 'Tablets', count: 18 },
    ].slice(0, columns)

    return (
        <div className="p-6 bg-white">
            <h2 className="text-xl font-bold text-gray-900 mb-4">
                {settings.title || (isRTL ? 'الأقسام الفرعية' : 'Subcategories')}
            </h2>
            <div
                className="grid gap-4"
                style={{ gridTemplateColumns: `repeat(${columns}, 1fr)` }}
            >
                {subcategories.map((category) => (
                    <div
                        key={category.id}
                        className={`relative rounded-xl overflow-hidden cursor-pointer transition-transform hover:scale-105 ${settings.cardStyle === 'card'
                            ? 'bg-gray-100 p-4'
                            : settings.cardStyle === 'minimal'
                                ? 'border border-gray-200 p-4'
                                : 'aspect-video bg-gradient-to-br from-emerald-500 to-teal-600'
                            }`}
                    >
                        {settings.cardStyle === 'overlay' ? (
                            <div className="absolute inset-0 flex flex-col items-center justify-center text-white">
                                <span className="text-3xl mb-2">📂</span>
                                <h3 className="font-semibold">{category.name}</h3>
                                {settings.showProductCount && (
                                    <p className="text-sm opacity-80">
                                        {category.count} {isRTL ? 'منتج' : 'products'}
                                    </p>
                                )}
                            </div>
                        ) : (
                            <>
                                <div className="text-3xl mb-2">📂</div>
                                <h3 className="font-semibold text-gray-900">{category.name}</h3>
                                {settings.showProductCount && (
                                    <p className="text-sm text-gray-500">
                                        {category.count} {isRTL ? 'منتج' : 'products'}
                                    </p>
                                )}
                            </>
                        )}
                    </div>
                ))}
            </div>
        </div>
    )
}

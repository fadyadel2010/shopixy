/**
 * Text Block Section
 * Phase 9.8: POS Integration
 */

import { useTranslation } from 'react-i18next'

export const settingsSchema = [
    { key: 'heading', type: 'text', label: 'Heading' },
    { key: 'content', type: 'textarea', label: 'Content', rows: 5 },
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
    { key: 'backgroundColor', type: 'color', label: 'Background' },
    { key: 'textColor', type: 'color', label: 'Text Color' },
]

export function TextBlockEditor({ section, onUpdate }) {
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
                    {isRTL ? 'العنوان' : 'Heading'}
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
                    {isRTL ? 'المحتوى' : 'Content'}
                </label>
                <textarea
                    value={settings.content || ''}
                    onChange={(e) => handleChange('content', e.target.value)}
                    rows={5}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                />
            </div>

            <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                    {isRTL ? 'المحاذاة' : 'Alignment'}
                </label>
                <select
                    value={settings.alignment || 'left'}
                    onChange={(e) => handleChange('alignment', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                >
                    <option value="left">{isRTL ? 'يسار' : 'Left'}</option>
                    <option value="center">{isRTL ? 'وسط' : 'Center'}</option>
                    <option value="right">{isRTL ? 'يمين' : 'Right'}</option>
                </select>
            </div>

            <div className="grid grid-cols-2 gap-4">
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                        {isRTL ? 'لون الخلفية' : 'Background'}
                    </label>
                    <input
                        type="color"
                        value={settings.backgroundColor || '#ffffff'}
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
                        value={settings.textColor || '#1f2937'}
                        onChange={(e) => handleChange('textColor', e.target.value)}
                        className="w-full h-10 rounded-lg cursor-pointer"
                    />
                </div>
            </div>
        </div>
    )
}

export function TextBlockPreview({ section }) {
    const settings = section.settings

    return (
        <div
            className="p-8"
            style={{
                backgroundColor: settings.backgroundColor || '#ffffff',
                color: settings.textColor || '#1f2937',
                textAlign: settings.alignment || 'left',
            }}
        >
            {settings.heading && (
                <h2 className="text-2xl font-bold mb-4">{settings.heading}</h2>
            )}
            <p className="whitespace-pre-wrap">{settings.content || 'Add your content here...'}</p>
        </div>
    )
}

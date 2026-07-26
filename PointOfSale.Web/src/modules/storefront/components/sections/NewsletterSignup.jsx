/**
 * Newsletter Signup Section
 * Phase 9.8: POS Integration
 */

import { useTranslation } from 'react-i18next'

export const settingsSchema = [
    { key: 'heading', type: 'text', label: 'Heading' },
    { key: 'subheading', type: 'text', label: 'Subheading' },
    { key: 'buttonText', type: 'text', label: 'Button Text' },
    { key: 'backgroundColor', type: 'color', label: 'Background' },
    { key: 'textColor', type: 'color', label: 'Text Color' },
    { key: 'showName', type: 'toggle', label: 'Show Name Field' },
]

export function NewsletterSignupEditor({ section, onUpdate }) {
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
                    {isRTL ? 'العنوان الفرعي' : 'Subheading'}
                </label>
                <input
                    type="text"
                    value={settings.subheading || ''}
                    onChange={(e) => handleChange('subheading', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                />
            </div>

            <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                    {isRTL ? 'نص الزر' : 'Button Text'}
                </label>
                <input
                    type="text"
                    value={settings.buttonText || ''}
                    onChange={(e) => handleChange('buttonText', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                />
            </div>

            <div className="grid grid-cols-2 gap-4">
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                        {isRTL ? 'لون الخلفية' : 'Background'}
                    </label>
                    <input
                        type="color"
                        value={settings.backgroundColor || '#0f172a'}
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

            <label className="flex items-center gap-2">
                <input
                    type="checkbox"
                    checked={settings.showName ?? false}
                    onChange={(e) => handleChange('showName', e.target.checked)}
                    className="w-4 h-4 text-indigo-600 rounded focus:ring-indigo-500"
                />
                <span className="text-sm text-gray-700">{isRTL ? 'حقل الاسم' : 'Show Name Field'}</span>
            </label>
        </div>
    )
}

export function NewsletterSignupPreview({ section }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const settings = section.settings

    return (
        <div
            className="py-12 px-8 text-center"
            style={{
                backgroundColor: settings.backgroundColor || '#0f172a',
                color: settings.textColor || '#ffffff',
            }}
        >
            <h2 className="text-2xl font-bold mb-2">
                {settings.heading || (isRTL ? 'ابق على اطلاع' : 'Stay Updated')}
            </h2>
            <p className="opacity-80 mb-6">
                {settings.subheading || (isRTL ? 'اشترك في نشرتنا الإخبارية' : 'Subscribe to our newsletter')}
            </p>
            <div className="flex justify-center gap-3 max-w-md mx-auto">
                {settings.showName && (
                    <input
                        type="text"
                        placeholder={isRTL ? 'الاسم' : 'Name'}
                        className="flex-1 px-4 py-2 rounded-lg text-gray-900"
                        disabled
                    />
                )}
                <input
                    type="email"
                    placeholder={isRTL ? 'البريد الإلكتروني' : 'Email address'}
                    className="flex-1 px-4 py-2 rounded-lg text-gray-900"
                    disabled
                />
                <button className="px-6 py-2 bg-indigo-600 text-white font-medium rounded-lg">
                    {settings.buttonText || (isRTL ? 'اشتراك' : 'Subscribe')}
                </button>
            </div>
        </div>
    )
}

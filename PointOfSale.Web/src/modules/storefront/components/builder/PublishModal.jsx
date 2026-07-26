/**
 * Publish Modal
 * Phase 9.8: POS Integration
 */

import { useTranslation } from 'react-i18next'
import { Upload, AlertTriangle, X } from 'lucide-react'

export function PublishModal({ isOpen, onClose, onConfirm, pageKey, isPublishing }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    if (!isOpen) return null

    const pageName = {
        home: isRTL ? 'الصفحة الرئيسية' : 'Home Page',
        product: isRTL ? 'صفحة المنتج' : 'Product Page',
        category: isRTL ? 'صفحة التصنيف' : 'Category Page',
    }[pageKey] ?? pageKey

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
            <div className="bg-white rounded-2xl w-full max-w-md mx-4 shadow-2xl">
                {/* Header */}
                <div className="p-6 border-b border-gray-200 flex items-center justify-between">
                    <div className="flex items-center gap-3">
                        <div className="w-10 h-10 bg-green-100 rounded-lg flex items-center justify-center">
                            <Upload size={20} className="text-green-600" />
                        </div>
                        <h2 className="text-lg font-semibold text-gray-900">
                            {isRTL ? 'نشر التغييرات' : 'Publish Changes'}
                        </h2>
                    </div>
                    <button
                        onClick={onClose}
                        className="p-2 rounded-lg hover:bg-gray-100 transition-colors"
                    >
                        <X size={20} className="text-gray-500" />
                    </button>
                </div>

                {/* Content */}
                <div className="p-6">
                    <p className="text-gray-600 mb-4">
                        {isRTL
                            ? `أنت على وشك نشر التغييرات على "${pageName}".`
                            : `You are about to publish changes to "${pageName}".`
                        }
                    </p>

                    {/* Warning */}
                    <div className="p-4 bg-yellow-50 border border-yellow-200 rounded-lg flex gap-3">
                        <AlertTriangle size={20} className="text-yellow-600 flex-shrink-0 mt-0.5" />
                        <div>
                            <p className="text-sm font-medium text-yellow-800">
                                {isRTL ? 'تغييرات مباشرة' : 'Live Changes'}
                            </p>
                            <p className="text-sm text-yellow-700 mt-1">
                                {isRTL
                                    ? 'سيتم تطبيق هذه التغييرات على متجرك المباشر فوراً. يمكنك التراجع لاحقاً.'
                                    : 'These changes will be immediately visible on your live storefront. You can rollback later.'
                                }
                            </p>
                        </div>
                    </div>

                    {/* What happens */}
                    <ul className="mt-4 space-y-2 text-sm text-gray-600">
                        <li className="flex items-center gap-2">
                            <span className="w-1.5 h-1.5 bg-indigo-500 rounded-full" />
                            {isRTL ? 'إنشاء نسخة جديدة' : 'New version created'}
                        </li>
                        <li className="flex items-center gap-2">
                            <span className="w-1.5 h-1.5 bg-indigo-500 rounded-full" />
                            {isRTL ? 'تحديث ذاكرة التخزين المؤقت' : 'Cache automatically updated'}
                        </li>
                        <li className="flex items-center gap-2">
                            <span className="w-1.5 h-1.5 bg-indigo-500 rounded-full" />
                            {isRTL ? 'التغييرات ستكون مرئية فوراً' : 'Changes visible immediately'}
                        </li>
                    </ul>
                </div>

                {/* Actions */}
                <div className="p-6 border-t border-gray-200 flex justify-end gap-3">
                    <button
                        onClick={onClose}
                        disabled={isPublishing}
                        className="px-4 py-2 text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors disabled:opacity-50"
                    >
                        {isRTL ? 'إلغاء' : 'Cancel'}
                    </button>
                    <button
                        onClick={onConfirm}
                        disabled={isPublishing}
                        className="flex items-center gap-2 px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 transition-colors disabled:opacity-50"
                    >
                        <Upload size={16} />
                        {isPublishing
                            ? (isRTL ? 'جاري النشر...' : 'Publishing...')
                            : (isRTL ? 'نشر الآن' : 'Publish Now')
                        }
                    </button>
                </div>
            </div>
        </div>
    )
}

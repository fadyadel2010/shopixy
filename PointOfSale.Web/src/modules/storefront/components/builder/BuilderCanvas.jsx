/**
 * Builder Canvas
 * Phase 9.8: POS Integration
 */

import { useTranslation } from 'react-i18next'
import { DragOverlay } from '@dnd-kit/core'
import { SortableContext, verticalListSortingStrategy, useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { GripVertical, Eye, EyeOff, Copy, Trash2, Settings } from 'lucide-react'
import { useBuilderStore, selectSections, selectSelectedSectionId, selectCurrentPage } from '../../store'
import { sectionRegistry } from '../../builder'
import { BuilderThemeProvider } from './BuilderThemeProvider'
import { SectionRenderer as StorefrontSectionRenderer } from '@/components/sections/SectionRenderer'
import { StorefrontLayout } from '@/storefront/layout/StorefrontLayout'
import { getProductTemplatePreviewProduct } from '@/storefront/products/ProductTemplateRegistry'

/**
 * Sortable Section Card
 */
function SortableSectionCard({ section, isSelected, onSelect, onToggle, onDuplicate, onDelete, sectionProps }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: section.id })

    const definition = sectionRegistry.get(section.type)
    const Icon = definition?.icon

    const style = {
        transform: CSS.Transform.toString(transform),
        transition,
        zIndex: isDragging ? 100 : undefined,
    }

    return (
        <div
            ref={setNodeRef}
            style={style}
            className={`relative group rounded-xl overflow-hidden border-2 transition-all duration-200 ${isSelected
                    ? 'border-indigo-500 ring-2 ring-indigo-200'
                    : 'border-gray-200 hover:border-gray-300'
                } ${isDragging ? 'opacity-50' : ''} ${!section.enabled ? 'opacity-50' : ''}`}
            onClick={onSelect}
        >
            {/* Section Header */}
            <div className="absolute top-0 left-0 right-0 z-10 flex items-center justify-between p-2 bg-gradient-to-b from-gray-900/80 to-transparent">
                {/* Drag Handle & Title */}
                <div className="flex items-center gap-2">
                    <button
                        {...attributes}
                        {...listeners}
                        className="p-1 rounded hover:bg-white/20 cursor-grab active:cursor-grabbing"
                    >
                        <GripVertical size={16} className="text-white" />
                    </button>
                    <div className="flex items-center gap-2">
                        {Icon && <Icon size={14} className="text-indigo-300" />}
                        <span className="text-sm font-medium text-white">
                            {isRTL ? definition?.displayName.ar : definition?.displayName.en}
                        </span>
                    </div>
                </div>

                {/* Actions */}
                <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                    <button
                        onClick={(e) => { e.stopPropagation(); onToggle() }}
                        className="p-1.5 rounded hover:bg-white/20 transition-colors"
                        title={section.enabled ? (isRTL ? 'إخفاء' : 'Hide') : (isRTL ? 'إظهار' : 'Show')}
                    >
                        {section.enabled ? <Eye size={14} className="text-white" /> : <EyeOff size={14} className="text-white/60" />}
                    </button>
                    <button
                        onClick={(e) => { e.stopPropagation(); onDuplicate() }}
                        className="p-1.5 rounded hover:bg-white/20 transition-colors"
                        title={isRTL ? 'تكرار' : 'Duplicate'}
                    >
                        <Copy size={14} className="text-white" />
                    </button>
                    <button
                        onClick={(e) => { e.stopPropagation(); onDelete() }}
                        className="p-1.5 rounded hover:bg-red-500/50 transition-colors"
                        title={isRTL ? 'حذف' : 'Delete'}
                    >
                        <Trash2 size={14} className="text-red-300" />
                    </button>
                    <button
                        onClick={(e) => { e.stopPropagation(); onSelect() }}
                        className="p-1.5 rounded hover:bg-indigo-500/50 transition-colors"
                        title={isRTL ? 'تعديل' : 'Edit'}
                    >
                        <Settings size={14} className="text-indigo-300" />
                    </button>
                </div>
            </div>

            {/* Section Preview */}
            <div className="min-h-[100px] bg-white">
                {definition ? (
                    <StorefrontSectionRenderer section={section} sectionProps={sectionProps} />
                ) : (
                    <div className="p-8 text-center text-gray-400">
                        <p>{isRTL ? 'لا توجد معاينة' : 'No preview available'}</p>
                    </div>
                )}
            </div>

            {/* Disabled Overlay */}
            {!section.enabled && (
                <div className="absolute inset-0 bg-gray-900/40 flex items-center justify-center">
                    <div className="px-3 py-1.5 bg-gray-800 rounded-full text-gray-300 text-sm font-medium flex items-center gap-2">
                        <EyeOff size={14} />
                        {isRTL ? 'مخفي' : 'Hidden'}
                    </div>
                </div>
            )}
        </div>
    )
}

/**
 * Empty State
 */
function EmptyCanvasState({ onLoadDemo }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    return (
        <div className="h-full flex items-center justify-center">
            <div className="text-center max-w-md mx-auto">
                <div className="w-16 h-16 bg-gray-100 rounded-2xl flex items-center justify-center mx-auto mb-4">
                    <Settings size={32} className="text-gray-400" />
                </div>
                <h3 className="text-xl font-semibold text-gray-900 mb-2">
                    {isRTL ? 'ابدأ في بناء صفحتك' : 'Start Building Your Page'}
                </h3>
                <p className="text-gray-500 mb-6">
                    {isRTL
                        ? 'اسحب الأقسام من اللوحة الجانبية لبدء تصميم صفحة متجرك'
                        : 'Drag sections from the palette to start designing your storefront page.'
                    }
                </p>
                <div className="flex flex-col gap-3">
                    <button
                        onClick={onLoadDemo}
                        className="w-full px-4 py-2 bg-indigo-600 text-white font-medium rounded-lg hover:bg-indigo-700 transition-colors"
                    >
                        {isRTL ? 'تحميل قالب تجريبي' : 'Load Demo Template'}
                    </button>
                    <div className="p-4 bg-gray-100 rounded-lg border border-dashed border-gray-300">
                        <p className="text-sm text-gray-500">
                            {isRTL ? 'أو أفلت الأقسام هنا' : 'Or drop sections here'}
                        </p>
                    </div>
                </div>
            </div>
        </div>
    )
}

/**
 * Builder Canvas
 */
export function BuilderCanvas({ activeId, onLoadDemo, storeConfig, tenantId }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    const sections = useBuilderStore(selectSections)
    const selectedSectionId = useBuilderStore(selectSelectedSectionId)
    const currentPage = useBuilderStore(selectCurrentPage)
    const selectSection = useBuilderStore((s) => s.selectSection)
    const toggleSection = useBuilderStore((s) => s.toggleSection)
    const duplicateSection = useBuilderStore((s) => s.duplicateSection)
    const removeSection = useBuilderStore((s) => s.removeSection)

    const sectionIds = sections.map((s) => s.id)
    const activeSection = activeId ? sections.find((s) => s.id === activeId) : null
    const activeDef = activeId?.startsWith('palette-') ? sectionRegistry.get(activeId.replace('palette-', '')) : null
    const isConfigReady = Boolean(storeConfig)
    const sectionProps = currentPage === 'product-template'
        ? { product: getProductTemplatePreviewProduct() }
        : undefined

    return (
        <div className="flex-1 h-full bg-gray-100 overflow-auto">
            <div className="max-w-4xl mx-auto p-6 min-h-full">
                {isConfigReady ? (
                    <BuilderThemeProvider tenantId={tenantId} storeConfig={storeConfig}>
                        <div className="overflow-hidden rounded-3xl border border-gray-200 bg-white shadow-xl">
                            <StorefrontLayout>
                                {sections.length === 0 ? (
                                    <EmptyCanvasState onLoadDemo={onLoadDemo} />
                                ) : (
                                    <SortableContext items={sectionIds} strategy={verticalListSortingStrategy}>
                                        <div className="space-y-4 p-4 md:p-6">
                                            {sections.map((section) => (
                                                <SortableSectionCard
                                                    key={section.id}
                                                    section={section}
                                                    sectionProps={sectionProps}
                                                    isSelected={section.id === selectedSectionId}
                                                    onSelect={() => selectSection(section.id)}
                                                    onToggle={() => toggleSection(section.id)}
                                                    onDuplicate={() => duplicateSection(section.id)}
                                                    onDelete={() => removeSection(section.id)}
                                                />
                                            ))}
                                        </div>
                                    </SortableContext>
                                )}
                            </StorefrontLayout>
                        </div>
                    </BuilderThemeProvider>
                ) : (
                    <div className="flex min-h-[320px] items-center justify-center rounded-xl border border-dashed border-gray-300 bg-white text-sm text-gray-500">
                        {isRTL ? 'جارٍ تحميل معاينة المتجر...' : 'Loading storefront preview...'}
                    </div>
                )}

                {sections.length === 0 && (
                    <div className="mt-4 p-8 border-2 border-dashed border-gray-300 rounded-xl text-center">
                        <p className="text-gray-400">{isRTL ? 'أفلت الأقسام هنا لإضافتها' : 'Drop sections here to add them'}</p>
                    </div>
                )}
            </div>

            {/* Drag Overlay */}
            <DragOverlay>
                {activeSection && (
                    <div className="w-[600px] p-4 bg-white rounded-xl border-2 border-indigo-500 shadow-2xl opacity-90">
                        <div className="flex items-center gap-2 text-gray-900">
                            <span className="font-medium">
                                {isRTL ? sectionRegistry.get(activeSection.type)?.displayName.ar : sectionRegistry.get(activeSection.type)?.displayName.en}
                            </span>
                        </div>
                    </div>
                )}
                {activeDef && (
                    <div className="w-64 p-3 bg-white rounded-lg border-2 border-indigo-500 shadow-2xl opacity-90">
                        <div className="flex items-center gap-2 text-gray-900">
                            <span className="font-medium">{isRTL ? activeDef.displayName.ar : activeDef.displayName.en}</span>
                        </div>
                    </div>
                )}
            </DragOverlay>
        </div>
    )
}

import { useTranslation } from 'react-i18next'
import { DragOverlay } from '@dnd-kit/core'
import { SortableContext, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { Copy, Eye, EyeOff, GripVertical, Lock, Settings, Trash2 } from 'lucide-react'

import { SectionRenderer as StorefrontSectionRenderer } from '@/components/sections/SectionRenderer'
import { StorefrontLayout } from '@/storefront/layout/StorefrontLayout'
import {
    createMandatoryProductHeroSection,
    getProductTemplatePreviewProduct,
} from '@/storefront/products/ProductTemplateRegistry'
import { sectionRegistry } from '../../builder'
import { selectCurrentPage, selectSections, selectSelectedSectionId, useBuilderStore } from '../../store'
import { BuilderThemeProvider } from './BuilderThemeProvider'

function SortableSectionCard({ section, isSelected, onSelect, onToggle, onDuplicate, onDelete, sectionProps }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: section.id })
    const definition = sectionRegistry.get(section.type)
    const Icon = definition?.icon

    return (
        <div
            ref={setNodeRef}
            style={{
                transform: CSS.Transform.toString(transform),
                transition,
                zIndex: isDragging ? 100 : undefined,
            }}
            className={`relative group overflow-hidden rounded-xl border-2 transition-all duration-200 ${
                isSelected ? 'border-indigo-500 ring-2 ring-indigo-200' : 'border-gray-200 hover:border-gray-300'
            } ${isDragging ? 'opacity-50' : ''} ${!section.enabled ? 'opacity-50' : ''}`}
            onClick={onSelect}
        >
            <div className="absolute inset-x-0 top-0 z-10 flex items-center justify-between bg-gradient-to-b from-gray-900/80 to-transparent p-2">
                <div className="flex items-center gap-2">
                    <button
                        {...attributes}
                        {...listeners}
                        className="cursor-grab rounded p-1 hover:bg-white/20 active:cursor-grabbing"
                        aria-label={isRTL ? 'إعادة ترتيب القسم' : 'Reorder section'}
                    >
                        <GripVertical size={16} className="text-white" />
                    </button>
                    {Icon ? <Icon size={14} className="text-indigo-300" /> : null}
                    <span className="text-sm font-medium text-white">
                        {isRTL ? definition?.displayName.ar : definition?.displayName.en}
                    </span>
                </div>

                <div className="flex items-center gap-1 opacity-0 transition-opacity group-hover:opacity-100">
                    <button
                        onClick={(event) => { event.stopPropagation(); onToggle() }}
                        className="rounded p-1.5 transition-colors hover:bg-white/20"
                        title={section.enabled ? (isRTL ? 'إخفاء' : 'Hide') : (isRTL ? 'إظهار' : 'Show')}
                    >
                        {section.enabled ? <Eye size={14} className="text-white" /> : <EyeOff size={14} className="text-white/60" />}
                    </button>
                    <button
                        onClick={(event) => { event.stopPropagation(); onDuplicate() }}
                        className="rounded p-1.5 transition-colors hover:bg-white/20"
                        title={isRTL ? 'تكرار' : 'Duplicate'}
                    >
                        <Copy size={14} className="text-white" />
                    </button>
                    <button
                        onClick={(event) => { event.stopPropagation(); onDelete() }}
                        className="rounded p-1.5 transition-colors hover:bg-red-500/50"
                        title={isRTL ? 'حذف' : 'Delete'}
                    >
                        <Trash2 size={14} className="text-red-300" />
                    </button>
                    <button
                        onClick={(event) => { event.stopPropagation(); onSelect() }}
                        className="rounded p-1.5 transition-colors hover:bg-indigo-500/50"
                        title={isRTL ? 'تعديل' : 'Edit'}
                    >
                        <Settings size={14} className="text-indigo-300" />
                    </button>
                </div>
            </div>

            <div className="min-h-[100px] bg-white">
                <StorefrontSectionRenderer section={section} sectionProps={sectionProps} />
            </div>

            {!section.enabled ? (
                <div className="absolute inset-0 flex items-center justify-center bg-gray-900/40">
                    <div className="flex items-center gap-2 rounded-full bg-gray-800 px-3 py-1.5 text-sm font-medium text-gray-300">
                        <EyeOff size={14} />
                        {isRTL ? 'مخفي' : 'Hidden'}
                    </div>
                </div>
            ) : null}
        </div>
    )
}

function RuntimeOwnedProductHeroCard({ section, sectionProps }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    return (
        <div className="overflow-hidden rounded-xl border-2 border-emerald-400 bg-white">
            <div className="flex items-center justify-between bg-emerald-700 px-3 py-2 text-white">
                <div className="flex items-center gap-2 text-sm font-medium">
                    <Lock size={15} />
                    <span>{isRTL ? 'واجهة المنتج الإلزامية' : 'Mandatory Product Hero'}</span>
                </div>
                <span className="rounded-full bg-white/15 px-2 py-1 text-xs">
                    {isRTL ? 'مملوك لوقت التشغيل' : 'Runtime owned'}
                </span>
            </div>
            <StorefrontSectionRenderer section={section} sectionProps={sectionProps} />
        </div>
    )
}

function EmptyCanvasState({ onLoadDemo, supplemental = false }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    return (
        <div className="flex min-h-64 items-center justify-center rounded-xl border-2 border-dashed border-gray-300 bg-gray-50 p-8">
            <div className="mx-auto max-w-md text-center">
                <Settings size={32} className="mx-auto mb-4 text-gray-400" />
                <h3 className="mb-2 text-xl font-semibold text-gray-900">
                    {supplemental
                        ? (isRTL ? 'أضف أقساماً إضافية' : 'Add supplemental sections')
                        : (isRTL ? 'ابدأ في بناء صفحتك' : 'Start building your page')}
                </h3>
                <p className="mb-6 text-gray-500">
                    {isRTL ? 'اسحب الأقسام من اللوحة الجانبية إلى الصفحة.' : 'Drag sections from the palette onto the page.'}
                </p>
                <button
                    type="button"
                    onClick={onLoadDemo}
                    className="rounded-lg bg-indigo-600 px-4 py-2 font-medium text-white transition-colors hover:bg-indigo-700"
                >
                    {isRTL ? 'تحميل قالب تجريبي' : 'Load Demo Template'}
                </button>
            </div>
        </div>
    )
}

export function BuilderCanvas({ activeId, onLoadDemo, storeConfig, tenantId, templateId = 'default' }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const sections = useBuilderStore(selectSections)
    const selectedSectionId = useBuilderStore(selectSelectedSectionId)
    const currentPage = useBuilderStore(selectCurrentPage)
    const selectSection = useBuilderStore((state) => state.selectSection)
    const toggleSection = useBuilderStore((state) => state.toggleSection)
    const duplicateSection = useBuilderStore((state) => state.duplicateSection)
    const removeSection = useBuilderStore((state) => state.removeSection)

    const sectionIds = sections.map((section) => section.id)
    const activeSection = activeId ? sections.find((section) => section.id === activeId) : null
    const activeDefinition = activeId?.startsWith('palette-')
        ? sectionRegistry.get(activeId.replace('palette-', ''))
        : null
    const isProductTemplate = currentPage === 'product-template'
    const sectionProps = isProductTemplate ? { product: getProductTemplatePreviewProduct() } : undefined
    const runtimeProductHero = isProductTemplate ? createMandatoryProductHeroSection(templateId) : null

    return (
        <div className="h-full flex-1 overflow-auto bg-gray-100">
            <div className="mx-auto min-h-full max-w-4xl p-6">
                {storeConfig ? (
                    <BuilderThemeProvider tenantId={tenantId} storeConfig={storeConfig}>
                        <div className="overflow-hidden rounded-3xl border border-gray-200 bg-white shadow-xl">
                            <StorefrontLayout>
                                <div className="space-y-4 p-4 md:p-6">
                                    {runtimeProductHero ? (
                                        <RuntimeOwnedProductHeroCard section={runtimeProductHero} sectionProps={sectionProps} />
                                    ) : null}

                                    {sections.length === 0 ? (
                                        <EmptyCanvasState onLoadDemo={onLoadDemo} supplemental={isProductTemplate} />
                                    ) : (
                                        <SortableContext items={sectionIds} strategy={verticalListSortingStrategy}>
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
                                        </SortableContext>
                                    )}
                                </div>
                            </StorefrontLayout>
                        </div>
                    </BuilderThemeProvider>
                ) : (
                    <div className="flex min-h-[320px] items-center justify-center rounded-xl border border-dashed border-gray-300 bg-white text-sm text-gray-500">
                        {isRTL ? 'جارٍ تحميل معاينة المتجر...' : 'Loading storefront preview...'}
                    </div>
                )}
            </div>

            <DragOverlay>
                {activeSection ? (
                    <div className="w-[600px] rounded-xl border-2 border-indigo-500 bg-white p-4 shadow-2xl opacity-90">
                        <span className="font-medium text-gray-900">
                            {isRTL
                                ? sectionRegistry.get(activeSection.type)?.displayName.ar
                                : sectionRegistry.get(activeSection.type)?.displayName.en}
                        </span>
                    </div>
                ) : null}
                {activeDefinition ? (
                    <div className="w-64 rounded-lg border-2 border-indigo-500 bg-white p-3 shadow-2xl opacity-90">
                        <span className="font-medium text-gray-900">
                            {isRTL ? activeDefinition.displayName.ar : activeDefinition.displayName.en}
                        </span>
                    </div>
                ) : null}
            </DragOverlay>
        </div>
    )
}

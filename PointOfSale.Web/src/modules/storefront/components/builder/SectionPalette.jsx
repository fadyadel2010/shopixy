/**
 * Section Palette
 * Phase 9.8: POS Integration
 */

import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronDown, ChevronRight, GripVertical } from 'lucide-react'
import { useDraggable } from '@dnd-kit/core'
import { sectionRegistry } from '../../builder'
import { filterSectionDefinitionsForPage } from '../../builder/pageSectionAvailability'
import { categoryDisplayNames } from '../../types'

/**
 * Draggable Section Card
 */
function DraggableSectionCard({ definition }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
        id: `palette-${definition.type}`,
        data: {
            type: 'new-section',
            sectionType: definition.type,
        },
    })

    const style = {
        transform: transform ? `translate3d(${transform.x}px, ${transform.y}px, 0)` : undefined,
        opacity: isDragging ? 0.5 : 1,
    }

    const Icon = definition.icon

    return (
        <div
            ref={setNodeRef}
            style={style}
            {...listeners}
            {...attributes}
            className="flex items-center gap-3 p-3 bg-white border border-gray-200 rounded-lg hover:border-indigo-300 hover:shadow-sm cursor-grab active:cursor-grabbing transition-all group"
        >
            <div className="flex-shrink-0 text-gray-400 group-hover:text-gray-500">
                <GripVertical size={16} />
            </div>
            <div className="w-8 h-8 bg-indigo-100 rounded-lg flex items-center justify-center text-indigo-600">
                <Icon size={16} />
            </div>
            <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-gray-900 truncate">
                    {isRTL ? definition.displayName.ar : definition.displayName.en}
                </p>
                <p className="text-xs text-gray-500 truncate">
                    {isRTL ? definition.description.ar : definition.description.en}
                </p>
            </div>
        </div>
    )
}

/**
 * Section Palette
 */
export function SectionPalette({ pageKey = 'home' }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    const categories = useMemo(() => sectionRegistry.getCategories(), [])
    const sectionsByCategory = useMemo(() => {
        return categories.reduce((acc, category) => {
            acc[category] = filterSectionDefinitionsForPage(sectionRegistry.getByCategory(category), pageKey)
            return acc
        }, {})
    }, [categories, pageKey])
    const visibleCategories = useMemo(
        () => categories.filter((category) => (sectionsByCategory[category] || []).length > 0),
        [categories, sectionsByCategory]
    )
    const visibleCategoryKey = visibleCategories.join('|')
    const [expandedCategories, setExpandedCategories] = useState(() => new Set(visibleCategories))

    useEffect(() => {
        setExpandedCategories(new Set(visibleCategories))
    }, [visibleCategories, visibleCategoryKey])

    const toggleCategory = (category) => {
        const newExpanded = new Set(expandedCategories)
        if (newExpanded.has(category)) {
            newExpanded.delete(category)
        } else {
            newExpanded.add(category)
        }
        setExpandedCategories(newExpanded)
    }

    return (
        <div className="w-64 h-full bg-gray-50 border-r border-gray-200 flex flex-col">
            {/* Header */}
            <div className="p-4 border-b border-gray-200 bg-white">
                <h2 className="text-lg font-semibold text-gray-900">
                    {isRTL ? 'الأقسام' : 'Sections'}
                </h2>
                <p className="text-xs text-gray-500 mt-1">
                    {isRTL ? 'اسحب الأقسام إلى اللوحة' : 'Drag sections onto the canvas'}
                </p>
            </div>

            {/* Section List */}
            <div className="flex-1 overflow-y-auto p-3 space-y-4">
                {visibleCategories.map((category) => {
                    const sections = sectionsByCategory[category] || []
                    const isExpanded = expandedCategories.has(category)
                    const categoryName = categoryDisplayNames[category]

                    return (
                        <div key={category}>
                            {/* Category Header */}
                            <button
                                onClick={() => toggleCategory(category)}
                                className="w-full flex items-center gap-2 px-2 py-1.5 rounded hover:bg-gray-200 transition-colors"
                            >
                                {isExpanded ? (
                                    <ChevronDown size={14} className="text-gray-400" />
                                ) : (
                                    <ChevronRight size={14} className="text-gray-400" />
                                )}
                                <span className="text-xs font-semibold text-gray-600 uppercase tracking-wider">
                                    {isRTL ? categoryName?.ar : categoryName?.en}
                                </span>
                                <span className="ml-auto text-xs text-gray-400">
                                    {sections.length}
                                </span>
                            </button>

                            {/* Section Cards */}
                            {isExpanded && (
                                <div className="mt-2 space-y-2">
                                    {sections.map((section) => (
                                        <DraggableSectionCard
                                            key={section.type}
                                            definition={section}
                                        />
                                    ))}
                                </div>
                            )}
                        </div>
                    )
                })}
            </div>

            {/* Footer Help */}
            <div className="p-3 border-t border-gray-200 bg-white">
                <div className="p-3 bg-blue-50 rounded-lg">
                    <p className="text-xs text-blue-700">
                        <strong>{isRTL ? 'نصيحة:' : 'Tip:'}</strong>{' '}
                        {isRTL
                            ? 'أفلت الأقسام بين العناصر الموجودة للإدراج في ذلك الموضع'
                            : 'Drop sections between existing ones to insert at that position.'
                        }
                    </p>
                </div>
            </div>
        </div>
    )
}

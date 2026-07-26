import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import {
    closestCenter,
    DndContext,
    DragOverlay,
    KeyboardSensor,
    PointerSensor,
    useSensor,
    useSensors,
    type DragEndEvent,
    type DragStartEvent,
} from '@dnd-kit/core'
import {
    SortableContext,
    arrayMove,
    sortableKeyboardCoordinates,
    useSortable,
    verticalListSortingStrategy,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
    AlertTriangle,
    Eye,
    EyeOff,
    GripVertical,
    LayoutTemplate,
    Loader2,
    Plus,
    RefreshCw,
    Save,
    Sparkles,
    Trash2,
} from 'lucide-react'

import { useToast } from '../../components/Toast'
import HomepageBuilderPreview from './HomepageBuilderPreview'
import {
    generateHomepageStarterTemplate,
    getHomepageSchemas,
    getHomepagePreviewStoreConfig,
    getHomepageSections,
    getHomepageTenants,
    publishHomepageSections,
    reorderHomepageSections,
    saveHomepageDraft,
    translateMarketplaceContent,
    type HomepageSectionFieldSchema,
    type HomepageSectionSchema,
    type HomepageSectionType,
} from './homepageService'
import {
    buildSaveHomepageDraftPayload,
    canAutoPersistReorder,
    createBuilderSection,
    createBuilderSectionsFingerprint,
    formatSectionType,
    getFieldDisplayValue,
    getLocalizedFieldDisplayValue,
    getSectionSchema,
    getSectionTitle,
    getSectionTitleForLanguage,
    hasValidationIssues,
    hydrateBuilderSections,
    normalizeBuilderSectionsOrder,
    updateLocalizedFieldValue,
    validateBuilderSections,
    type HomepageBuilderSection,
} from './homepageBuilderUtils'

const homepageSchemasQueryKey = ['super-admin', 'homepage', 'schemas'] as const
const homepageTenantsQueryKey = ['super-admin', 'homepage', 'tenants'] as const
const homepageSectionsQueryKey = (tenantId: string | null, mode: 'preview' | 'published') =>
    ['super-admin', 'homepage', 'sections', tenantId ?? 'global', mode] as const
const homepagePreviewConfigQueryKey = (tenantId: string | null) =>
    ['super-admin', 'homepage', 'preview-config', tenantId ?? 'default'] as const

type ScopeMode = 'global' | 'tenant'

function getErrorMessage(error: unknown, fallback: string): string {
    return error instanceof Error && error.message ? error.message : fallback
}

function formatTimestamp(value?: string | null): string {
    if (!value) {
        return 'Unavailable'
    }

    const parsed = new Date(value)
    if (Number.isNaN(parsed.getTime())) {
        return 'Unavailable'
    }

    return parsed.toLocaleString()
}

function getLatestPublishedTimestamp(sections: Array<{ publishedAt?: string | null }>): string | null {
    const timestamps = sections
        .map((section) => section.publishedAt)
        .filter((value): value is string => Boolean(value))
        .map((value) => new Date(value))
        .filter((value) => !Number.isNaN(value.getTime()))
        .sort((left, right) => right.getTime() - left.getTime())

    return timestamps[0]?.toISOString() ?? null
}

function ScopeButton({
    isActive,
    label,
    description,
    onClick,
}: {
    isActive: boolean
    label: string
    description: string
    onClick: () => void
}) {
    return (
        <button
            type="button"
            onClick={onClick}
            className={[
                'rounded-2xl border px-4 py-3 text-left transition',
                isActive
                    ? 'border-sky-400/40 bg-sky-500/[0.12] text-slate-950 shadow-[0_24px_50px_-36px_rgba(14,165,233,0.75)]'
                    : 'border-slate-200 bg-white text-slate-600 hover:border-slate-300 hover:bg-slate-50',
            ].join(' ')}
        >
            <p className="text-sm font-semibold">{label}</p>
            <p className="mt-1 text-xs leading-5 text-slate-500">{description}</p>
        </button>
    )
}

function SchemaFieldInput({
    field,
    value,
    onChange,
}: {
    field: HomepageSectionFieldSchema
    value: string
    onChange: (value: string) => void
}) {
    const baseClassName =
        'w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition placeholder:text-slate-400 focus:border-sky-300 focus:ring-4 focus:ring-sky-100'

    if (field.kind === 'textarea' || field.kind === 'guid-list') {
        return (
            <textarea
                value={value}
                onChange={(event) => onChange(event.target.value)}
                rows={field.kind === 'guid-list' ? 6 : 4}
                className={`${baseClassName} resize-y`}
                placeholder={
                    field.kind === 'guid-list'
                        ? 'One GUID per line or comma-separated'
                        : `Enter ${field.label.toLowerCase()}`
                }
            />
        )
    }

    if (field.kind === 'select') {
        return (
            <select value={value} onChange={(event) => onChange(event.target.value)} className={baseClassName}>
                {field.options.map((option) => (
                    <option key={option} value={option}>
                        {option}
                    </option>
                ))}
            </select>
        )
    }

    if (field.kind === 'number') {
        return (
            <input
                type="number"
                min="1"
                step="1"
                value={value}
                onChange={(event) => onChange(event.target.value)}
                className={baseClassName}
                placeholder={`Enter ${field.label.toLowerCase()}`}
            />
        )
    }

    return (
        <input
            type="text"
            value={value}
            onChange={(event) => onChange(event.target.value)}
            className={baseClassName}
            placeholder={`Enter ${field.label.toLowerCase()}`}
        />
    )
}

function shouldDisplaySchemaField(
    field: HomepageSectionFieldSchema,
    sectionData: Record<string, unknown>,
): boolean {
    if (field.kind !== 'guid-list') {
        return true
    }

    const source = typeof sectionData.source === 'string' ? sectionData.source.trim().toLowerCase() : 'all'
    return source === 'curated'
}

function SortableSectionItem({
    section,
    schema,
    isSelected,
    hasIssues,
    onSelect,
    onRemove,
}: {
    section: HomepageBuilderSection
    schema: HomepageSectionSchema | null
    isSelected: boolean
    hasIssues: boolean
    onSelect: () => void
    onRemove: () => void
}) {
    const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
        id: section.clientId,
    })

    const style = {
        transform: CSS.Transform.toString(transform),
        transition,
    }

    return (
        <div
            ref={setNodeRef}
            style={style}
            className={[
                'rounded-[24px] border bg-white p-4 shadow-sm transition',
                isSelected
                    ? 'border-sky-300 ring-4 ring-sky-100'
                    : 'border-slate-200 hover:border-slate-300 hover:shadow-md',
                isDragging ? 'opacity-60 shadow-lg' : '',
            ].join(' ')}
        >
            <div className="flex items-start gap-3">
                <button
                    type="button"
                    {...attributes}
                    {...listeners}
                    className="mt-1 flex h-9 w-9 shrink-0 items-center justify-center rounded-2xl border border-slate-200 bg-slate-50 text-slate-500 transition hover:border-slate-300 hover:text-slate-700"
                    aria-label={`Reorder ${formatSectionType(section.type)} section`}
                >
                    <GripVertical className="h-4 w-4" />
                </button>

                <button type="button" onClick={onSelect} className="min-w-0 flex-1 text-left">
                    <div className="flex flex-wrap items-center gap-2">
                        <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.18em] text-slate-500">
                            {schema?.displayName ?? formatSectionType(section.type)}
                        </span>
                        <span
                            className={[
                                'rounded-full border px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.18em]',
                                section.isActive
                                    ? 'border-emerald-200 bg-emerald-50 text-emerald-700'
                                    : 'border-slate-200 bg-slate-100 text-slate-500',
                            ].join(' ')}
                        >
                            {section.isActive ? 'Visible' : 'Hidden'}
                        </span>
                        {section.persistedId ? null : (
                            <span className="rounded-full border border-amber-200 bg-amber-50 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.18em] text-amber-700">
                                New
                            </span>
                        )}
                        {hasIssues ? (
                            <span className="rounded-full border border-rose-200 bg-rose-50 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.18em] text-rose-700">
                                Invalid
                            </span>
                        ) : null}
                    </div>

                    <h3 className="mt-3 truncate text-base font-semibold text-slate-950">
                        {getSectionTitleForLanguage(section, schema ? [schema] : [], 'en')}
                    </h3>
                    <p className="mt-1 text-sm text-slate-500">
                        {schema?.description ?? 'Schema-backed homepage section ready for draft editing.'}
                    </p>
                </button>

                <button
                    type="button"
                    onClick={onRemove}
                    className="flex h-9 w-9 shrink-0 items-center justify-center rounded-2xl border border-rose-200 bg-rose-50 text-rose-600 transition hover:bg-rose-100"
                    aria-label={`Remove ${formatSectionType(section.type)} section`}
                >
                    <Trash2 className="h-4 w-4" />
                </button>
            </div>
        </div>
    )
}

function resolveSelectedSectionIdAfterSync(
    currentSelectedSectionId: string | null,
    previousSections: HomepageBuilderSection[],
    nextSections: HomepageBuilderSection[],
): string | null {
    if (!nextSections.length) {
        return null
    }

    if (!currentSelectedSectionId) {
        return nextSections[0].clientId
    }

    const previousSelectedSection = previousSections.find((section) => section.clientId === currentSelectedSectionId)
    if (!previousSelectedSection) {
        return nextSections[0].clientId
    }

    if (previousSelectedSection.persistedId) {
        const matchingPersistedSection = nextSections.find(
            (section) => section.persistedId === previousSelectedSection.persistedId,
        )

        if (matchingPersistedSection) {
            return matchingPersistedSection.clientId
        }
    }

    const matchingShape = nextSections.find(
        (section) =>
            section.type === previousSelectedSection.type && section.order === previousSelectedSection.order,
    )

    return matchingShape?.clientId ?? nextSections[0].clientId
}

export default function HomepageBuilderPage() {
    const queryClient = useQueryClient()
    const { showError, showInfo, showSuccess } = useToast()
    const [scopeMode, setScopeMode] = useState<ScopeMode>('global')
    const [selectedTenantId, setSelectedTenantId] = useState('')
    const [hydratedScopeKey, setHydratedScopeKey] = useState<string | null>(null)
    const [sections, setSections] = useState<HomepageBuilderSection[]>([])
    const [savedSectionsSnapshot, setSavedSectionsSnapshot] = useState<HomepageBuilderSection[]>([])
    const [removedPersistedIds, setRemovedPersistedIds] = useState<string[]>([])
    const [selectedSectionId, setSelectedSectionId] = useState<string | null>(null)
    const [pendingAddType, setPendingAddType] = useState<HomepageSectionType>('hero')
    const [activeDragId, setActiveDragId] = useState<string | null>(null)
    const [previewTenantId, setPreviewTenantId] = useState('')
    const [activeLanguage, setActiveLanguage] = useState<'en' | 'ar'>('en')
    const [translatingFieldKey, setTranslatingFieldKey] = useState('')

    const sensors = useSensors(
        useSensor(PointerSensor, {
            activationConstraint: { distance: 8 },
        }),
        useSensor(KeyboardSensor, {
            coordinateGetter: sortableKeyboardCoordinates,
        }),
    )

    const activeTenantId = scopeMode === 'tenant' ? selectedTenantId || null : null
    const canLoadScopedSections = scopeMode === 'global' || Boolean(activeTenantId)
    const scopeKey = `${scopeMode}:${activeTenantId ?? 'global'}`

    const schemasQuery = useQuery({
        queryKey: homepageSchemasQueryKey,
        queryFn: getHomepageSchemas,
        staleTime: 5 * 60_000,
    })

    const tenantsQuery = useQuery({
        queryKey: homepageTenantsQueryKey,
        queryFn: getHomepageTenants,
        staleTime: 5 * 60_000,
    })

    const previewSectionsQuery = useQuery({
        queryKey: homepageSectionsQueryKey(activeTenantId, 'preview'),
        queryFn: () => getHomepageSections(activeTenantId, 'preview'),
        enabled: canLoadScopedSections,
    })

    const publishedSectionsQuery = useQuery({
        queryKey: homepageSectionsQueryKey(activeTenantId, 'published'),
        queryFn: () => getHomepageSections(activeTenantId, 'published'),
        enabled: canLoadScopedSections,
    })

    const schemas = schemasQuery.data ?? []
    const tenants = tenantsQuery.data ?? []
    const selectedTenant = tenants.find((tenant) => tenant.id === selectedTenantId) ?? null
    const scopeLabel = selectedTenant ? `${selectedTenant.name} homepage` : 'Global homepage'
    const effectivePreviewTenantId = scopeMode === 'tenant' ? activeTenantId : previewTenantId || null
    const effectivePreviewTenant =
        effectivePreviewTenantId ? tenants.find((tenant) => tenant.id === effectivePreviewTenantId) ?? null : null

    const previewConfigQuery = useQuery({
        queryKey: homepagePreviewConfigQueryKey(effectivePreviewTenantId),
        queryFn: () => getHomepagePreviewStoreConfig(effectivePreviewTenantId!),
        enabled: Boolean(effectivePreviewTenantId),
        staleTime: 5 * 60_000,
    })

    const validationMap = useMemo(() => validateBuilderSections(sections, schemas), [schemas, sections])
    const validationIssueCount = useMemo(
        () => Object.values(validationMap).reduce((count, issues) => count + issues.length, 0),
        [validationMap],
    )

    const currentFingerprint = useMemo(
        () => createBuilderSectionsFingerprint(sections, schemas),
        [schemas, sections],
    )
    const savedFingerprint = useMemo(
        () => createBuilderSectionsFingerprint(savedSectionsSnapshot, schemas),
        [savedSectionsSnapshot, schemas],
    )

    const isDirty = removedPersistedIds.length > 0 || currentFingerprint !== savedFingerprint
    const hasValidationErrors = hasValidationIssues(validationMap)
    const activeDraftCount = sections.filter((section) => section.isActive).length
    const latestPublishedTimestamp = getLatestPublishedTimestamp(publishedSectionsQuery.data?.sections ?? [])
    const selectedSection = sections.find((section) => section.clientId === selectedSectionId) ?? null
    const selectedSchema = selectedSection ? getSectionSchema(selectedSection.type, schemas) : null
    const selectedSectionIssues = selectedSection ? validationMap[selectedSection.clientId] ?? [] : []
    const activeDragSection = sections.find((section) => section.clientId === activeDragId) ?? null

    const saveDraftMutation = useMutation({
        mutationFn: saveHomepageDraft,
    })

    const reorderMutation = useMutation({
        mutationFn: ({
            sectionIds,
            tenantId,
        }: {
            sectionIds: string[]
            tenantId?: string | null
        }) => reorderHomepageSections(sectionIds, tenantId),
    })

    const publishMutation = useMutation({
        mutationFn: (tenantId?: string | null) => publishHomepageSections(tenantId),
    })

    const starterTemplateMutation = useMutation({
        mutationFn: ({
            tenantId,
            overwriteExisting,
        }: {
            tenantId?: string | null
            overwriteExisting: boolean
        }) =>
            generateHomepageStarterTemplate({
                tenantId,
                overwriteExisting,
            }),
    })

    useEffect(() => {
        if (!schemas.length) {
            return
        }

        if (!schemas.some((schema) => schema.type === pendingAddType)) {
            setPendingAddType(schemas[0].type)
        }
    }, [pendingAddType, schemas])

    useEffect(() => {
        if (scopeMode === 'tenant') {
            return
        }

        if (previewTenantId || tenants.length === 0) {
            return
        }

        setPreviewTenantId(tenants[0].id)
    }, [previewTenantId, scopeMode, tenants])

    useEffect(() => {
        if (!canLoadScopedSections || !schemas.length || !previewSectionsQuery.data || hydratedScopeKey === scopeKey) {
            return
        }

        const hydratedSections = hydrateBuilderSections(previewSectionsQuery.data.sections, schemas)
        setSections(hydratedSections)
        setSavedSectionsSnapshot(hydratedSections)
        setRemovedPersistedIds([])
        setSelectedSectionId(hydratedSections[0]?.clientId ?? null)
        setHydratedScopeKey(scopeKey)
    }, [canLoadScopedSections, hydratedScopeKey, previewSectionsQuery.data, schemas, scopeKey])

    useEffect(() => {
        if (selectedSectionId && sections.some((section) => section.clientId === selectedSectionId)) {
            return
        }

        setSelectedSectionId(sections[0]?.clientId ?? null)
    }, [sections, selectedSectionId])

    useEffect(() => {
        const handleBeforeUnload = (event: BeforeUnloadEvent) => {
            if (!isDirty) {
                return
            }

            event.preventDefault()
            event.returnValue = ''
        }

        window.addEventListener('beforeunload', handleBeforeUnload)
        return () => window.removeEventListener('beforeunload', handleBeforeUnload)
    }, [isDirty])

    useEffect(() => {
        const handleKeyDown = (event: KeyboardEvent) => {
            const target = event.target as HTMLElement | null
            const isInputFocused =
                target instanceof HTMLInputElement ||
                target instanceof HTMLTextAreaElement ||
                target instanceof HTMLSelectElement

            if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
                event.preventDefault()
                if (!saveDraftMutation.isPending && !hasValidationErrors && canLoadScopedSections) {
                    void handleSaveDraft()
                }
                return
            }

            if (event.key === 'Escape' && !isInputFocused) {
                setSelectedSectionId(null)
                return
            }

            if ((event.key === 'Delete' || event.key === 'Backspace') && !isInputFocused && selectedSection) {
                event.preventDefault()
                handleRemoveSection(selectedSection.clientId)
            }
        }

        window.addEventListener('keydown', handleKeyDown)
        return () => window.removeEventListener('keydown', handleKeyDown)
    }, [canLoadScopedSections, hasValidationErrors, saveDraftMutation.isPending, selectedSection])

    function resetBuilderStateForScopeChange() {
        setHydratedScopeKey(null)
        setSections([])
        setSavedSectionsSnapshot([])
        setRemovedPersistedIds([])
        setSelectedSectionId(null)
    }

    function confirmScopeChange(): boolean {
        if (!isDirty) {
            return true
        }

        return window.confirm('You have unsaved homepage draft changes. Switch scopes and discard them?')
    }

    function handleScopeModeChange(nextScopeMode: ScopeMode) {
        if (nextScopeMode === scopeMode) {
            return
        }

        if (!confirmScopeChange()) {
            return
        }

        resetBuilderStateForScopeChange()
        setScopeMode(nextScopeMode)
    }

    function handleTenantChange(nextTenantId: string) {
        if (nextTenantId === selectedTenantId) {
            return
        }

        if (!confirmScopeChange()) {
            return
        }

        resetBuilderStateForScopeChange()
        setSelectedTenantId(nextTenantId)
    }

    function applySections(nextSections: HomepageBuilderSection[]) {
        setSections(normalizeBuilderSectionsOrder(nextSections))
    }

    function handleSelectSection(sectionClientId: string) {
        setSelectedSectionId(sectionClientId)
    }

    function handleAddSection() {
        const schema = schemas.find((candidate) => candidate.type === pendingAddType)
        if (!schema) {
            showError('Section schema is unavailable right now.')
            return
        }

        const newSection = createBuilderSection(schema, sections.length, activeTenantId)
        const nextSections = normalizeBuilderSectionsOrder([...sections, newSection])
        applySections(nextSections)
        setSelectedSectionId(newSection.clientId)
    }

    function handleRemoveSection(sectionClientId: string) {
        const section = sections.find((candidate) => candidate.clientId === sectionClientId)
        if (!section) {
            return
        }

        const confirmed = window.confirm(
            `Remove "${getSectionTitle(section, schemas)}" from the current draft workspace?`,
        )

        if (!confirmed) {
            return
        }

        if (section.persistedId) {
            setRemovedPersistedIds((currentIds) =>
                currentIds.includes(section.persistedId!) ? currentIds : [...currentIds, section.persistedId!],
            )
        }

        applySections(sections.filter((candidate) => candidate.clientId !== sectionClientId))
    }

    function handleFieldChange(field: HomepageSectionFieldSchema, rawValue: string) {
        if (!selectedSection) {
            return
        }

        applySections(
            sections.map((section) =>
                section.clientId === selectedSection.clientId
                    ? {
                          ...section,
                          data: updateLocalizedFieldValue(section.data, field, activeLanguage, rawValue),
                      }
                    : section,
            ),
        )
    }

    async function handleTranslateField(field: HomepageSectionFieldSchema) {
        if (!selectedSection || !field.isLocalized || activeLanguage !== 'ar') {
            return
        }

        const englishValue = getLocalizedFieldDisplayValue(selectedSection.data, field, 'en')
        const arabicValue = getLocalizedFieldDisplayValue(selectedSection.data, field, 'ar')
        if (!englishValue.trim()) {
            showInfo('Enter English content first, then translate it into Arabic.')
            return
        }

        if (arabicValue.trim() && !window.confirm('Arabic content already exists. Overwrite it with a fresh AI translation?')) {
            return
        }

        setTranslatingFieldKey(field.key)
        try {
            const translatedValue = await translateMarketplaceContent(englishValue, 'en', 'ar')
            handleFieldChange(field, translatedValue)
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to translate this field right now.'))
        } finally {
            setTranslatingFieldKey('')
        }
    }

    function handleSectionTypeChange(nextType: HomepageSectionType) {
        if (!selectedSection) {
            return
        }

        const nextSchema = getSectionSchema(nextType, schemas)
        if (!nextSchema) {
            showError('Section schema is unavailable right now.')
            return
        }

        const replacementSection = createBuilderSection(nextSchema, selectedSection.order, activeTenantId)
        replacementSection.clientId = selectedSection.clientId
        replacementSection.persistedId = selectedSection.persistedId ?? null
        replacementSection.isActive = selectedSection.isActive
        replacementSection.createdAt = selectedSection.createdAt
        replacementSection.updatedAt = selectedSection.updatedAt
        replacementSection.version = selectedSection.version

        applySections(
            sections.map((section) =>
                section.clientId === selectedSection.clientId ? replacementSection : section,
            ),
        )
    }

    function handleVisibilityToggle() {
        if (!selectedSection) {
            return
        }

        applySections(
            sections.map((section) =>
                section.clientId === selectedSection.clientId
                    ? {
                          ...section,
                          isActive: !section.isActive,
                      }
                    : section,
            ),
        )
    }

    async function handleSaveDraft() {
        if (!canLoadScopedSections) {
            showInfo('Select a tenant scope before saving draft changes.')
            return
        }

        if (hasValidationErrors) {
            showError('Resolve the validation issues in the builder before saving the draft.')
            return
        }

        try {
            const previousSections = sections
            const savedDraftSections = await saveDraftMutation.mutateAsync(
                buildSaveHomepageDraftPayload(sections, schemas, activeTenantId),
            )
            const hydratedSections = hydrateBuilderSections(savedDraftSections, schemas)
            const nextSelectedSectionId = resolveSelectedSectionIdAfterSync(
                selectedSectionId,
                previousSections,
                hydratedSections,
            )

            setSections(hydratedSections)
            setSavedSectionsSnapshot(hydratedSections)
            setRemovedPersistedIds([])
            setSelectedSectionId(nextSelectedSectionId)
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'homepage'] })
            showSuccess('Homepage draft saved.')
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to save the homepage draft.'))
        }
    }

    async function handlePublish() {
        if (!canLoadScopedSections) {
            showInfo('Select a tenant scope before publishing.')
            return
        }

        if (isDirty) {
            showInfo('Save the current draft before publishing it live.')
            return
        }

        if (hasValidationErrors) {
            showError('Resolve validation issues before publishing.')
            return
        }

        const confirmed = window.confirm(
            `Publish the current ${scopeLabel} draft to live customers? This replaces the currently published homepage for this scope.`,
        )

        if (!confirmed) {
            return
        }

        try {
            await publishMutation.mutateAsync(activeTenantId)
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'homepage'] })
            showSuccess('Homepage draft published.')
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to publish the homepage draft.'))
        }
    }

    async function handleGenerateStarterTemplate(overwriteExisting: boolean) {
        if (!canLoadScopedSections) {
            showInfo('Select a tenant scope before generating a starter template.')
            return
        }

        if (overwriteExisting) {
            const confirmed = window.confirm(
                'Reset the current draft to the starter homepage template? This replaces the saved draft for this scope but does not publish it live.',
            )

            if (!confirmed) {
                return
            }
        }

        try {
            const previousSections = sections
            const generatedSections = await starterTemplateMutation.mutateAsync({
                tenantId: activeTenantId,
                overwriteExisting,
            })
            const hydratedSections = hydrateBuilderSections(generatedSections, schemas)
            const nextSelectedSectionId = resolveSelectedSectionIdAfterSync(
                selectedSectionId,
                previousSections,
                hydratedSections,
            )

            setSections(hydratedSections)
            setSavedSectionsSnapshot(hydratedSections)
            setRemovedPersistedIds([])
            setSelectedSectionId(nextSelectedSectionId)
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'homepage'] })
            showSuccess(overwriteExisting ? 'Starter template applied to the draft.' : 'Starter template generated.')
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to generate the starter homepage template.'))
        }
    }

    async function handlePersistReorder(nextSections: HomepageBuilderSection[]) {
        if (!canAutoPersistReorder(nextSections, removedPersistedIds)) {
            showInfo('Draft order updated locally. Save the draft to persist new sections or removals.')
            return
        }

        try {
            await reorderMutation.mutateAsync({
                sectionIds: nextSections.map((section) => section.persistedId!).filter(Boolean),
                tenantId: activeTenantId,
            })

            setSavedSectionsSnapshot((currentSections) => {
                const nextOrderByPersistedId = new Map(
                    nextSections
                        .filter((section) => section.persistedId)
                        .map((section, index) => [section.persistedId!, index] as const),
                )

                return normalizeBuilderSectionsOrder(
                    currentSections.map((section) => {
                        if (!section.persistedId || !nextOrderByPersistedId.has(section.persistedId)) {
                            return section
                        }

                        return {
                            ...section,
                            order: nextOrderByPersistedId.get(section.persistedId)!,
                        }
                    }),
                )
            })
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to persist the new homepage order.'))
        }
    }

    function handleDragStart(event: DragStartEvent) {
        setActiveDragId(String(event.active.id))
    }

    function handleDragEnd(event: DragEndEvent) {
        setActiveDragId(null)
        const { active, over } = event

        if (!over || active.id === over.id) {
            return
        }

        const currentIndex = sections.findIndex((section) => section.clientId === active.id)
        const nextIndex = sections.findIndex((section) => section.clientId === over.id)

        if (currentIndex < 0 || nextIndex < 0) {
            return
        }

        const nextSections = normalizeBuilderSectionsOrder(arrayMove(sections, currentIndex, nextIndex))
        applySections(nextSections)
        void handlePersistReorder(nextSections)
    }

    const isBusy =
        saveDraftMutation.isPending ||
        publishMutation.isPending ||
        reorderMutation.isPending ||
        starterTemplateMutation.isPending

    if (schemasQuery.isError) {
        return (
            <section className="grid min-h-[360px] place-items-center rounded-[32px] border border-slate-200 bg-white p-8 text-center shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                <div className="max-w-xl">
                    <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-rose-50 text-rose-600">
                        <AlertTriangle className="h-6 w-6" />
                    </div>
                    <h2 className="mt-5 text-2xl font-semibold text-slate-950">Builder unavailable</h2>
                    <p className="mt-3 text-sm leading-6 text-slate-500">
                        {getErrorMessage(schemasQuery.error, 'Unable to load homepage section schemas.')}
                    </p>
                    <button
                        type="button"
                        onClick={() => schemasQuery.refetch()}
                        className="mt-6 inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800"
                    >
                        <RefreshCw className="h-4 w-4" />
                        Retry
                    </button>
                </div>
            </section>
        )
    }

    return (
        <div className="space-y-6">
            <section className="rounded-[32px] border border-slate-200 bg-[radial-gradient(circle_at_top_left,rgba(14,165,233,0.12),transparent_32%),linear-gradient(180deg,#ffffff_0%,#f8fafc_100%)] p-6 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)] lg:p-8">
                <div className="flex flex-wrap items-start justify-between gap-6">
                    <div className="max-w-3xl">
                        <div className="inline-flex items-center gap-2 rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-sky-700">
                            <Sparkles className="h-3.5 w-3.5" />
                            Homepage Builder
                        </div>
                        <h1 className="mt-4 text-3xl font-semibold tracking-tight text-slate-950 lg:text-4xl">
                            Build the storefront homepage visually
                        </h1>
                        <p className="mt-4 max-w-3xl text-sm leading-7 text-slate-500">
                            Drag sections, edit schema-backed content inline, and validate the draft preview before anything
                            reaches live customers. Published traffic stays isolated until you explicitly publish.
                        </p>
                    </div>

                    <div className="flex flex-wrap items-center gap-3">
                        <Link
                            to="/super-admin/homepage/manager"
                            className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                        >
                            Legacy Manager
                        </Link>

                        <button
                            type="button"
                            onClick={() => void handleGenerateStarterTemplate(sections.length > 0)}
                            disabled={!canLoadScopedSections || isBusy}
                            className="inline-flex items-center gap-2 rounded-2xl border border-amber-200 bg-amber-50 px-5 py-3 text-sm font-semibold text-amber-700 transition hover:bg-amber-100 disabled:cursor-not-allowed disabled:opacity-60"
                        >
                            {starterTemplateMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
                            {sections.length > 0 ? 'Reset to Template' : 'Generate Starter'}
                        </button>

                        <button
                            type="button"
                            onClick={() => void handleSaveDraft()}
                            disabled={!canLoadScopedSections || !isDirty || hasValidationErrors || isBusy}
                            className="inline-flex items-center gap-2 rounded-2xl border border-slate-950 bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
                        >
                            {saveDraftMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
                            Save Draft
                        </button>

                        <button
                            type="button"
                            onClick={() => void handlePublish()}
                            disabled={!canLoadScopedSections || isBusy || hasValidationErrors || isDirty}
                            className="inline-flex items-center gap-2 rounded-2xl border border-emerald-200 bg-emerald-50 px-5 py-3 text-sm font-semibold text-emerald-700 transition hover:bg-emerald-100 disabled:cursor-not-allowed disabled:opacity-60"
                        >
                            {publishMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Eye className="h-4 w-4" />}
                            Publish
                        </button>
                    </div>
                </div>

                <div className="mt-8 grid gap-4 xl:grid-cols-[1.2fr_0.8fr_0.8fr_0.8fr]">
                    <div className="rounded-[28px] border border-white/70 bg-white/80 px-5 py-4 shadow-sm">
                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Editing scope</p>
                        <p className="mt-2 text-sm font-semibold text-slate-950">{scopeLabel}</p>
                        <p className="mt-2 text-xs leading-5 text-slate-500">
                            Tenant-scoped drafts override the global homepage only for that tenant after publish.
                        </p>
                    </div>

                    <div className="rounded-[28px] border border-white/70 bg-white/80 px-5 py-4 shadow-sm">
                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Draft health</p>
                        <p className="mt-2 text-sm font-semibold text-slate-950">
                            {isDirty ? 'Unsaved changes present' : 'Draft in sync'}
                        </p>
                        <p className="mt-2 text-xs leading-5 text-slate-500">
                            {sections.length} sections total | {activeDraftCount} visible in preview
                        </p>
                    </div>

                    <div className="rounded-[28px] border border-white/70 bg-white/80 px-5 py-4 shadow-sm">
                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Validation</p>
                        <p className="mt-2 text-sm font-semibold text-slate-950">
                            {validationIssueCount > 0 ? `${validationIssueCount} issues to resolve` : 'No blocking issues'}
                        </p>
                        <p className="mt-2 text-xs leading-5 text-slate-500">
                            Publish stays disabled until the builder state is valid and saved.
                        </p>
                    </div>

                    <div className="rounded-[28px] border border-white/70 bg-white/80 px-5 py-4 shadow-sm">
                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Live status</p>
                        <p className="mt-2 text-sm font-semibold text-slate-950">
                            {publishedSectionsQuery.data?.sections.length ?? 0} published sections
                        </p>
                        <p className="mt-2 text-xs leading-5 text-slate-500">
                            Last published {latestPublishedTimestamp ? formatTimestamp(latestPublishedTimestamp) : 'never'}
                        </p>
                    </div>
                </div>
            </section>

            <section className="grid gap-4 md:grid-cols-2">
                <ScopeButton
                    isActive={scopeMode === 'global'}
                    label="Global"
                    description="Fallback homepage shared by stores without an override."
                    onClick={() => handleScopeModeChange('global')}
                />
                <ScopeButton
                    isActive={scopeMode === 'tenant'}
                    label="Tenant Override"
                    description="Edit a draft homepage for a specific marketplace tenant."
                    onClick={() => handleScopeModeChange('tenant')}
                />
            </section>

            {scopeMode === 'tenant' ? (
                <section className="rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm">
                    <label className="space-y-2">
                        <span className="text-sm font-semibold text-slate-900">Tenant</span>
                        <select
                            value={selectedTenantId}
                            onChange={(event) => handleTenantChange(event.target.value)}
                            className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                        >
                            <option value="">Select a tenant override</option>
                            {tenants.map((tenant) => (
                                <option key={tenant.id} value={tenant.id}>
                                    {tenant.name}
                                </option>
                            ))}
                        </select>
                    </label>
                </section>
            ) : null}

            {!canLoadScopedSections ? (
                <section className="grid min-h-[360px] place-items-center rounded-[32px] border border-dashed border-slate-300 bg-white p-8 text-center shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                    <div className="max-w-xl">
                        <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-sky-100 text-sky-700">
                            <LayoutTemplate className="h-6 w-6" />
                        </div>
                        <h2 className="mt-5 text-2xl font-semibold text-slate-950">Choose a tenant override</h2>
                        <p className="mt-3 text-sm leading-6 text-slate-500">
                            Tenant-scoped homepage drafts are ready, but you need to select a tenant before sections can be
                            loaded or saved.
                        </p>
                    </div>
                </section>
            ) : previewSectionsQuery.isError ? (
                <section className="grid min-h-[360px] place-items-center rounded-[32px] border border-slate-200 bg-white p-8 text-center shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                    <div className="max-w-xl">
                        <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-rose-50 text-rose-600">
                            <AlertTriangle className="h-6 w-6" />
                        </div>
                        <h2 className="mt-5 text-2xl font-semibold text-slate-950">Draft homepage unavailable</h2>
                        <p className="mt-3 text-sm leading-6 text-slate-500">
                            {getErrorMessage(previewSectionsQuery.error, 'Unable to load homepage draft sections for this scope.')}
                        </p>
                        <button
                            type="button"
                            onClick={() => previewSectionsQuery.refetch()}
                            className="mt-6 inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800"
                        >
                            <RefreshCw className="h-4 w-4" />
                            Retry
                        </button>
                    </div>
                </section>
            ) : (
                <DndContext
                    sensors={sensors}
                    collisionDetection={closestCenter}
                    onDragStart={handleDragStart}
                    onDragEnd={handleDragEnd}
                >
                    <section className="grid gap-4 xl:grid-cols-[320px_minmax(0,1fr)_360px]">
                        <aside className="rounded-[32px] border border-slate-200 bg-white p-5 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                            <div>
                                <p className="text-xs font-semibold uppercase tracking-[0.2em] text-sky-600">Sections</p>
                                <h2 className="mt-3 text-2xl font-semibold text-slate-950">Draft structure</h2>
                                <p className="mt-3 text-sm leading-6 text-slate-500">
                                    Reorder on the left, click the preview to inspect, and keep published traffic isolated while
                                    you shape the draft.
                                </p>
                            </div>

                            <div className="mt-6 rounded-[28px] border border-slate-200 bg-slate-50/80 p-4">
                                <label className="space-y-2">
                                    <span className="text-sm font-semibold text-slate-900">Add section type</span>
                                    <select
                                        value={pendingAddType}
                                        onChange={(event) => setPendingAddType(event.target.value as HomepageSectionType)}
                                        className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                                    >
                                        {schemas.map((schema) => (
                                            <option key={schema.type} value={schema.type}>
                                                {schema.displayName}
                                            </option>
                                        ))}
                                    </select>
                                </label>

                                <button
                                    type="button"
                                    onClick={handleAddSection}
                                    className="mt-4 inline-flex w-full items-center justify-center gap-2 rounded-2xl bg-slate-950 px-4 py-3 text-sm font-semibold text-white transition hover:bg-slate-800"
                                >
                                    <Plus className="h-4 w-4" />
                                    Add Section
                                </button>
                            </div>

                            <div className="mt-6">
                                {sections.length === 0 ? (
                                    <div className="grid min-h-[240px] place-items-center rounded-[28px] border border-dashed border-slate-300 bg-slate-50 px-6 text-center">
                                        <div>
                                            <LayoutTemplate className="mx-auto h-10 w-10 text-slate-300" />
                                            <p className="mt-4 text-sm font-semibold text-slate-900">No sections in this draft</p>
                                            <p className="mt-2 text-sm leading-6 text-slate-500">
                                                Start with a hero, product shelf, banner, or categories section, or
                                                generate the Shopixy starter template.
                                            </p>
                                            <button
                                                type="button"
                                                onClick={() => void handleGenerateStarterTemplate(false)}
                                                disabled={isBusy}
                                                className="mt-5 inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-4 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
                                            >
                                                {starterTemplateMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
                                                Generate Starter Template
                                            </button>
                                        </div>
                                    </div>
                                ) : (
                                    <SortableContext
                                        items={sections.map((section) => section.clientId)}
                                        strategy={verticalListSortingStrategy}
                                    >
                                        <div className="space-y-3">
                                            {sections.map((section) => (
                                                <SortableSectionItem
                                                    key={section.clientId}
                                                    section={section}
                                                    schema={getSectionSchema(section.type, schemas)}
                                                    isSelected={section.clientId === selectedSectionId}
                                                    hasIssues={Boolean(validationMap[section.clientId]?.length)}
                                                    onSelect={() => handleSelectSection(section.clientId)}
                                                    onRemove={() => handleRemoveSection(section.clientId)}
                                                />
                                            ))}
                                        </div>
                                    </SortableContext>
                                )}
                            </div>
                        </aside>

                        <div className="rounded-[32px] border border-slate-200 bg-[linear-gradient(180deg,#f8fafc_0%,#eef2ff_100%)] p-5 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                            <div className="flex flex-wrap items-center justify-between gap-4 border-b border-slate-200 pb-5">
                                <div>
                                    <p className="text-xs font-semibold uppercase tracking-[0.2em] text-sky-600">Live Preview</p>
                                    <h2 className="mt-3 text-2xl font-semibold text-slate-950">Unified storefront canvas</h2>
                                    <p className="mt-2 text-sm leading-6 text-slate-500">
                                        The builder now uses the same storefront section renderer customers get after publish.
                                        Local draft edits still stay isolated until you save and publish them.
                                    </p>
                                </div>

                                <div className="flex flex-wrap items-center gap-3">
                                    {scopeMode === 'global' ? (
                                        <label className="rounded-[24px] border border-slate-200 bg-white px-4 py-3 shadow-sm">
                                            <span className="text-[11px] font-semibold uppercase tracking-[0.18em] text-slate-500">
                                                Preview tenant
                                            </span>
                                            <select
                                                value={previewTenantId}
                                                onChange={(event) => setPreviewTenantId(event.target.value)}
                                                className="mt-2 block min-w-[15rem] rounded-2xl border border-slate-200 bg-white px-3 py-2 text-sm text-slate-900 shadow-sm outline-none transition focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                                            >
                                                <option value="">Choose storefront theme</option>
                                                {tenants.map((tenant) => (
                                                    <option key={tenant.id} value={tenant.id}>
                                                        {tenant.name}
                                                    </option>
                                                ))}
                                            </select>
                                        </label>
                                    ) : null}

                                    <div className="rounded-[24px] border border-slate-200 bg-white px-4 py-3 shadow-sm">
                                        <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-slate-500">
                                            Published snapshot
                                        </p>
                                        <p className="mt-1 text-sm font-semibold text-slate-950">
                                            {publishedSectionsQuery.data?.sections.length ?? 0} live sections
                                        </p>
                                    </div>
                                </div>
                            </div>

                            <div className="mt-6">
                                {previewSectionsQuery.isLoading && !hydratedScopeKey ? (
                                    <div className="space-y-4">
                                        {Array.from({ length: 3 }).map((_, index) => (
                                            <div
                                                key={`builder-preview-skeleton-${index}`}
                                                className="h-56 animate-pulse rounded-[30px] border border-slate-200 bg-white"
                                            />
                                        ))}
                                    </div>
                                ) : (
                                    <HomepageBuilderPreview
                                        sections={sections}
                                        selectedSectionClientId={selectedSectionId}
                                        onSelectSection={handleSelectSection}
                                        storeConfig={previewConfigQuery.data ?? null}
                                        previewTenantId={effectivePreviewTenantId}
                                        previewTenantName={effectivePreviewTenant?.name ?? null}
                                        isLoadingConfig={previewConfigQuery.isLoading && !previewConfigQuery.data}
                                        language={activeLanguage}
                                        schemas={schemas}
                                    />
                                )}

                                {previewConfigQuery.isError ? (
                                    <div className="mt-4 rounded-[24px] border border-amber-200 bg-amber-50 px-4 py-4 text-sm text-amber-800">
                                        {getErrorMessage(
                                            previewConfigQuery.error,
                                            'Unable to load the storefront theme configuration for preview.',
                                        )}
                                    </div>
                                ) : null}
                            </div>
                        </div>

                        <aside className="rounded-[32px] border border-slate-200 bg-white p-5 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                            {selectedSection && selectedSchema ? (
                                <>
                                    <div className="flex items-start justify-between gap-4">
                                        <div>
                                            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-sky-600">Editor</p>
                                            <h2 className="mt-3 text-2xl font-semibold text-slate-950">
                                                {getSectionTitleForLanguage(selectedSection, schemas, activeLanguage)}
                                            </h2>
                                            <p className="mt-2 text-sm leading-6 text-slate-500">
                                                Inline edits update the draft preview instantly and stay local until you save.
                                            </p>
                                        </div>

                                        <button
                                            type="button"
                                            onClick={handleVisibilityToggle}
                                            className={[
                                                'inline-flex items-center gap-2 rounded-2xl border px-3 py-2 text-sm font-semibold transition',
                                                selectedSection.isActive
                                                    ? 'border-emerald-200 bg-emerald-50 text-emerald-700'
                                                    : 'border-slate-200 bg-slate-100 text-slate-500',
                                            ].join(' ')}
                                        >
                                            {selectedSection.isActive ? <Eye className="h-4 w-4" /> : <EyeOff className="h-4 w-4" />}
                                            {selectedSection.isActive ? 'Visible' : 'Hidden'}
                                        </button>
                                    </div>

                                    <div className="mt-6 grid gap-4 md:grid-cols-2">
                                        <label className="space-y-2">
                                            <span className="text-sm font-semibold text-slate-900">Section type</span>
                                            <select
                                                value={selectedSection.type}
                                                onChange={(event) => handleSectionTypeChange(event.target.value as HomepageSectionType)}
                                                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                                            >
                                                {schemas.map((schema) => (
                                                    <option key={schema.type} value={schema.type}>
                                                        {schema.displayName}
                                                    </option>
                                                ))}
                                            </select>
                                        </label>

                                        <div className="rounded-[24px] border border-slate-200 bg-slate-50 px-4 py-3">
                                            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Draft state</p>
                                            <p className="mt-2 text-sm font-semibold text-slate-950">
                                                {selectedSection.persistedId ? 'Persisted draft section' : 'New local section'}
                                            </p>
                                            <p className="mt-2 text-xs leading-5 text-slate-500">
                                                Schema v{selectedSection.schemaVersion} | order slot {selectedSection.order + 1}
                                            </p>
                                        </div>
                                    </div>

                                    {selectedSectionIssues.length > 0 ? (
                                        <div className="mt-6 rounded-[24px] border border-rose-200 bg-rose-50 px-4 py-4 text-sm text-rose-700">
                                            <p className="font-semibold">Validation needs attention</p>
                                            <ul className="mt-2 space-y-2">
                                                {selectedSectionIssues.map((issue) => (
                                                    <li key={`${issue.fieldKey}-${issue.message}`}>{issue.message}</li>
                                                ))}
                                            </ul>
                                        </div>
                                    ) : null}

                                    <div className="mt-6 space-y-5">
                                        <div className="inline-flex rounded-2xl border border-slate-200 bg-slate-50 p-1">
                                            {(['en', 'ar'] as const).map((language) => (
                                                <button
                                                    key={language}
                                                    type="button"
                                                    onClick={() => setActiveLanguage(language)}
                                                    className={[
                                                        'rounded-xl px-4 py-2 text-sm font-semibold transition',
                                                        activeLanguage === language
                                                            ? 'bg-slate-900 text-white'
                                                            : 'text-slate-600 hover:bg-white',
                                                    ].join(' ')}
                                                >
                                                    {language.toUpperCase()}
                                                </button>
                                            ))}
                                        </div>

                                        {selectedSchema.fields
                                            .filter((field) => shouldDisplaySchemaField(field, selectedSection.data))
                                            .map((field) => {
                                            const fieldIssues = selectedSectionIssues.filter(
                                                (issue) => issue.fieldKey === field.key,
                                            )

                                            return (
                                                <div key={field.key} className="space-y-2">
                                                    <div className="flex items-center justify-between gap-4">
                                                        <div className="flex items-center gap-3">
                                                            <label className="text-sm font-semibold text-slate-900">
                                                                {field.label}
                                                                {field.isLocalized ? ` (${activeLanguage.toUpperCase()})` : ''}
                                                            </label>
                                                            {field.required ? (
                                                                <span className="rounded-full border border-rose-200 bg-rose-50 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.18em] text-rose-600">
                                                                    Required
                                                                </span>
                                                            ) : (
                                                                <span className="rounded-full border border-slate-200 bg-white px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.18em] text-slate-400">
                                                                    Optional
                                                                </span>
                                                            )}
                                                        </div>

                                                        {field.isLocalized ? (
                                                            <button
                                                                type="button"
                                                                onClick={() => void handleTranslateField(field)}
                                                                disabled={activeLanguage !== 'ar' || translatingFieldKey === field.key}
                                                                className="inline-flex items-center gap-2 rounded-2xl border border-slate-200 bg-white px-3 py-2 text-xs font-semibold uppercase tracking-[0.18em] text-slate-600 transition hover:border-slate-300 hover:bg-slate-50 disabled:opacity-50"
                                                            >
                                                                {translatingFieldKey === field.key ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
                                                                Auto Translate
                                                            </button>
                                                        ) : null}
                                                    </div>

                                                    <SchemaFieldInput
                                                        field={field}
                                                        value={field.isLocalized
                                                            ? getLocalizedFieldDisplayValue(selectedSection.data, field, activeLanguage)
                                                            : getFieldDisplayValue(field, selectedSection.data[field.key])}
                                                        onChange={(value) => handleFieldChange(field, value)}
                                                    />

                                                    {field.helpText ? (
                                                        <p className="text-xs leading-5 text-slate-500">{field.helpText}</p>
                                                    ) : null}

                                                    {fieldIssues.map((issue) => (
                                                        <p key={issue.message} className="text-xs font-medium text-rose-600">
                                                            {issue.message}
                                                        </p>
                                                    ))}
                                                </div>
                                            )
                                        })}
                                    </div>

                                    <div className="mt-8 border-t border-slate-200 pt-6">
                                        <button
                                            type="button"
                                            onClick={() => handleRemoveSection(selectedSection.clientId)}
                                            className="inline-flex w-full items-center justify-center gap-2 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-semibold text-rose-700 transition hover:bg-rose-100"
                                        >
                                            <Trash2 className="h-4 w-4" />
                                            Remove Section
                                        </button>
                                    </div>
                                </>
                            ) : (
                                <div className="grid min-h-[520px] place-items-center text-center">
                                    <div className="max-w-sm">
                                        <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-sky-100 text-sky-700">
                                            <Sparkles className="h-6 w-6" />
                                        </div>
                                        <h2 className="mt-5 text-2xl font-semibold text-slate-950">Select a section</h2>
                                        <p className="mt-3 text-sm leading-6 text-slate-500">
                                            Pick a section from the left list or click a block in the live preview to start editing
                                            its schema-backed fields.
                                        </p>
                                    </div>
                                </div>
                            )}
                        </aside>
                    </section>

                    <DragOverlay>
                        {activeDragSection ? (
                            <div className="w-72 rounded-[24px] border border-sky-300 bg-white p-4 shadow-2xl">
                                <div className="flex items-center gap-3">
                                    <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-sky-100 text-sky-700">
                                        <LayoutTemplate className="h-5 w-5" />
                                    </div>
                                    <div>
                                        <p className="text-sm font-semibold text-slate-950">
                                            {getSectionTitleForLanguage(activeDragSection, schemas, activeLanguage)}
                                        </p>
                                        <p className="text-xs text-slate-500">{formatSectionType(activeDragSection.type)}</p>
                                    </div>
                                </div>
                            </div>
                        ) : null}
                    </DragOverlay>
                </DndContext>
            )}
        </div>
    )
}

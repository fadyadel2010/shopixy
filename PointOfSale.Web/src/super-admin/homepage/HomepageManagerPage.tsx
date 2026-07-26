import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
    ArrowDown,
    ArrowUp,
    Eye,
    EyeOff,
    LayoutTemplate,
    Loader2,
    Pencil,
    Plus,
    RefreshCw,
    ShieldAlert,
    Sparkles,
    Trash2,
} from 'lucide-react'
import { useToast } from '../../components/Toast'
import {
    createHomepageSection,
    deleteHomepageSection,
    getHomepageSchemas,
    getHomepageSections,
    getHomepageTenants,
    publishHomepageSections,
    reorderHomepageSections,
    updateHomepageSection,
    type HomepageSectionFieldSchema,
    type HomepageSectionPayload,
    type HomepageSectionRecord,
    type HomepageSectionSchema,
    type HomepageSectionType,
    type HomepageViewMode,
} from './homepageService'

const homepageSchemasQueryKey = ['super-admin', 'homepage', 'schemas'] as const
const homepageTenantsQueryKey = ['super-admin', 'homepage', 'tenants'] as const
const homepageSectionsQueryKey = (tenantId: string | null, mode: HomepageViewMode) =>
    ['super-admin', 'homepage', 'sections', tenantId ?? 'global', mode] as const

type ScopeMode = 'global' | 'tenant'

interface SectionFormState {
    id?: string
    type: HomepageSectionType
    order: number
    isActive: boolean
    data: Record<string, string>
}

function getErrorMessage(error: unknown, fallback: string): string {
    if (error instanceof Error && error.message) {
        return error.message
    }

    return fallback
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

function formatSectionType(type: HomepageSectionType): string {
    switch (type) {
        case 'hero':
            return 'Hero'
        case 'products':
            return 'Products'
        case 'banner':
            return 'Banner'
        case 'categories':
            return 'Categories'
        default:
            return type
    }
}

function getLatestPublishedTimestamp(sections: HomepageSectionRecord[]): string | null {
    const timestamps = sections
        .map((section) => section.publishedAt)
        .filter((value): value is string => Boolean(value))
        .map((value) => new Date(value))
        .filter((value) => !Number.isNaN(value.getTime()))
        .sort((left, right) => right.getTime() - left.getTime())

    return timestamps[0]?.toISOString() ?? null
}

function getDefaultFieldValue(field: HomepageSectionFieldSchema): string {
    if (field.kind === 'select') {
        return field.options[0] ?? ''
    }

    return ''
}

function buildEmptyData(schema: HomepageSectionSchema): Record<string, string> {
    return schema.fields.reduce<Record<string, string>>((accumulator, field) => {
        accumulator[field.key] = getDefaultFieldValue(field)
        return accumulator
    }, {})
}

function remapDataToSchema(
    previousData: Record<string, string>,
    schema: HomepageSectionSchema,
): Record<string, string> {
    return schema.fields.reduce<Record<string, string>>((accumulator, field) => {
        accumulator[field.key] = previousData[field.key] ?? getDefaultFieldValue(field)
        return accumulator
    }, {})
}

function serializeValue(value: unknown): string {
    if (Array.isArray(value)) {
        return value
            .map((entry) => String(entry ?? '').trim())
            .filter(Boolean)
            .join('\n')
    }

    if (typeof value === 'string') {
        return value
    }

    if (typeof value === 'number' || typeof value === 'boolean') {
        return String(value)
    }

    return ''
}

function inferLegacyCollectionSource(
    sectionType: HomepageSectionType,
    data: Record<string, unknown>,
): string {
    const ids =
        sectionType === 'products'
            ? Array.isArray(data.productIds)
                ? data.productIds
                : []
            : sectionType === 'categories'
              ? Array.isArray(data.categoryIds)
                ? data.categoryIds
                : []
              : []

    return ids.length > 0 ? 'curated' : 'all'
}

function buildFormStateFromSection(
    section: HomepageSectionRecord,
    schemas: HomepageSectionSchema[],
): SectionFormState {
    const schema = schemas.find((candidate) => candidate.type === section.type)
    const schemaData = schema
        ? schema.fields.reduce<Record<string, string>>((accumulator, field) => {
              if (field.key === 'source' && section.data[field.key] == null) {
                  accumulator[field.key] = inferLegacyCollectionSource(section.type, section.data)
                  return accumulator
              }

              accumulator[field.key] = serializeValue(section.data[field.key])
              return accumulator
          }, {})
        : Object.entries(section.data).reduce<Record<string, string>>((accumulator, [key, value]) => {
              accumulator[key] = serializeValue(value)
              return accumulator
          }, {})

    return {
        id: section.id,
        type: section.type,
        order: section.order,
        isActive: section.isActive,
        data: schemaData,
    }
}

function normalizePayloadData(
    schema: HomepageSectionSchema,
    formData: Record<string, string>,
): Record<string, unknown> {
    return schema.fields.reduce<Record<string, unknown>>((accumulator, field) => {
        const rawValue = formData[field.key] ?? ''
        const trimmedValue = rawValue.trim()

        if (field.kind === 'guid-list') {
            accumulator[field.key] = trimmedValue
                .split(/[\n,]+/)
                .map((entry) => entry.trim())
                .filter(Boolean)
            return accumulator
        }

        if (field.kind === 'number') {
            accumulator[field.key] = trimmedValue
            return accumulator
        }

        accumulator[field.key] = trimmedValue
        return accumulator
    }, {})
}

function summarizeValue(value: unknown): string {
    if (Array.isArray(value)) {
        return `${value.length} item${value.length === 1 ? '' : 's'}`
    }

    if (typeof value === 'number' || typeof value === 'boolean') {
        return String(value)
    }

    if (typeof value !== 'string') {
        return 'Not configured'
    }

    const trimmed = value.trim()
    if (!trimmed) {
        return 'Not configured'
    }

    return trimmed.length > 72 ? `${trimmed.slice(0, 69)}...` : trimmed
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
                rows={field.kind === 'guid-list' ? 5 : 4}
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
            <select
                value={value}
                onChange={(event) => onChange(event.target.value)}
                className={baseClassName}
            >
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
    formData: Record<string, string>,
): boolean {
    if (field.kind !== 'guid-list') {
        return true
    }

    const source = (formData.source ?? '').trim().toLowerCase()
    return source === 'curated'
}

function SectionEditorModal({
    isOpen,
    scopeLabel,
    schemas,
    formState,
    isSubmitting,
    onClose,
    onSubmit,
    onTypeChange,
    onFieldChange,
    onVisibilityChange,
}: {
    isOpen: boolean
    scopeLabel: string
    schemas: HomepageSectionSchema[]
    formState: SectionFormState | null
    isSubmitting: boolean
    onClose: () => void
    onSubmit: () => void
    onTypeChange: (type: HomepageSectionType) => void
    onFieldChange: (key: string, value: string) => void
    onVisibilityChange: (isActive: boolean) => void
}) {
    const selectedSchema = useMemo(
        () => schemas.find((schema) => schema.type === formState?.type) ?? null,
        [formState?.type, schemas],
    )

    const previewPayload = useMemo(
        () => (selectedSchema && formState ? normalizePayloadData(selectedSchema, formState.data) : {}),
        [formState, selectedSchema],
    )

    if (!isOpen || !formState || !selectedSchema) {
        return null
    }

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center px-4 py-6">
            <button
                type="button"
                aria-label="Close section editor"
                className="absolute inset-0 bg-slate-950/55 backdrop-blur-sm"
                onClick={onClose}
            />

            <div className="relative z-10 max-h-[92vh] w-full max-w-5xl overflow-hidden rounded-[32px] border border-white/20 bg-white shadow-[0_40px_120px_-48px_rgba(15,23,42,0.55)]">
                <div className="grid max-h-[92vh] lg:grid-cols-[1.15fr_0.85fr]">
                    <div className="overflow-y-auto p-6 sm:p-8">
                        <div className="flex items-start justify-between gap-4">
                            <div>
                                <p className="text-xs font-semibold uppercase tracking-[0.24em] text-sky-600">
                                    Section Editor
                                </p>
                                <h2 className="mt-3 text-3xl font-semibold text-slate-950">
                                    {formState.id ? 'Update homepage section' : 'Add homepage section'}
                                </h2>
                                <p className="mt-3 max-w-2xl text-sm leading-6 text-slate-500">
                                    Configure schema-backed content for the {scopeLabel} homepage. Changes are
                                    written to the draft workspace first, then validated again by the API before
                                    they can be published.
                                </p>
                            </div>

                            <span className="rounded-full border border-slate-200 bg-slate-50 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                Slot {formState.order + 1}
                            </span>
                        </div>

                        <div className="mt-8 grid gap-5 md:grid-cols-2">
                            <label className="space-y-2">
                                <span className="text-sm font-semibold text-slate-900">Section type</span>
                                <select
                                    value={formState.type}
                                    onChange={(event) => onTypeChange(event.target.value as HomepageSectionType)}
                                    className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                                >
                                    {schemas.map((schema) => (
                                        <option key={schema.type} value={schema.type}>
                                            {schema.displayName}
                                        </option>
                                    ))}
                                </select>
                            </label>

                            <label className="space-y-2">
                                <span className="text-sm font-semibold text-slate-900">Visibility</span>
                                <button
                                    type="button"
                                    onClick={() => onVisibilityChange(!formState.isActive)}
                                    className={[
                                        'flex w-full items-center justify-between rounded-2xl border px-4 py-3 text-sm font-medium transition',
                                        formState.isActive
                                            ? 'border-emerald-200 bg-emerald-50 text-emerald-700'
                                            : 'border-slate-200 bg-slate-50 text-slate-500',
                                    ].join(' ')}
                                >
                                    <span>{formState.isActive ? 'Visible on storefront' : 'Hidden from storefront'}</span>
                                    {formState.isActive ? <Eye className="h-4 w-4" /> : <EyeOff className="h-4 w-4" />}
                                </button>
                            </label>
                        </div>

                        <div className="mt-8 rounded-[28px] border border-slate-200 bg-slate-50/80 p-5">
                            <h3 className="text-lg font-semibold text-slate-950">{selectedSchema.displayName}</h3>
                            <p className="mt-2 text-sm leading-6 text-slate-500">{selectedSchema.description}</p>
                        </div>

                        <div className="mt-8 space-y-5">
                            {selectedSchema.fields
                                .filter((field) => shouldDisplaySchemaField(field, formState.data))
                                .map((field) => (
                                <div key={field.key} className="space-y-2">
                                    <div className="flex items-center justify-between gap-4">
                                        <label className="text-sm font-semibold text-slate-900">{field.label}</label>
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

                                    <SchemaFieldInput
                                        field={field}
                                        value={formState.data[field.key] ?? ''}
                                        onChange={(value) => onFieldChange(field.key, value)}
                                    />

                                    {field.helpText ? (
                                        <p className="text-xs leading-5 text-slate-500">{field.helpText}</p>
                                    ) : null}
                                </div>
                            ))}
                        </div>

                        <div className="mt-8 flex flex-wrap items-center justify-end gap-3 border-t border-slate-200 pt-6">
                            <button
                                type="button"
                                onClick={onClose}
                                className="rounded-2xl border border-slate-200 bg-white px-5 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                            >
                                Cancel
                            </button>

                            <button
                                type="button"
                                onClick={onSubmit}
                                disabled={isSubmitting}
                                className="inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white shadow-lg shadow-slate-900/15 transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
                            >
                                {isSubmitting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
                                {formState.id ? 'Save draft changes' : 'Create draft section'}
                            </button>
                        </div>
                    </div>

                    <aside className="border-t border-slate-200 bg-slate-950 px-6 py-6 text-slate-100 lg:border-l lg:border-t-0 sm:px-8">
                        <p className="text-xs font-semibold uppercase tracking-[0.24em] text-sky-200">
                            Payload Preview
                        </p>
                        <h3 className="mt-3 text-2xl font-semibold text-white">Schema output</h3>
                        <p className="mt-3 text-sm leading-6 text-slate-300">
                            This is the JSON shape that will be stored in the draft workspace for this section.
                        </p>

                        <div className="mt-6 rounded-[28px] border border-white/10 bg-white/[0.04] p-4">
                            <pre className="overflow-x-auto whitespace-pre-wrap break-words text-xs leading-6 text-sky-100">
                                {JSON.stringify(previewPayload, null, 2)}
                            </pre>
                        </div>

                        <div className="mt-6 rounded-[28px] border border-emerald-400/20 bg-emerald-400/10 p-4">
                            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-emerald-200">
                                Scope
                            </p>
                            <p className="mt-2 text-sm font-medium text-white">{scopeLabel}</p>
                            <p className="mt-3 text-sm leading-6 text-slate-300">
                                Tenant-scoped sections override the global homepage whenever that tenant has active section entries configured.
                            </p>
                        </div>
                    </aside>
                </div>
            </div>
        </div>
    )
}

export default function HomepageManagerPage() {
    const queryClient = useQueryClient()
    const { showError, showSuccess, showInfo } = useToast()
    const [scopeMode, setScopeMode] = useState<ScopeMode>('global')
    const [viewMode, setViewMode] = useState<HomepageViewMode>('preview')
    const [selectedTenantId, setSelectedTenantId] = useState('')
    const [formState, setFormState] = useState<SectionFormState | null>(null)
    const [isEditorOpen, setIsEditorOpen] = useState(false)

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

    const tenantOptions = useMemo(
        () =>
            [...(tenantsQuery.data ?? [])].sort((left, right) => {
                if (left.isActive !== right.isActive) {
                    return left.isActive ? -1 : 1
                }

                return left.name.localeCompare(right.name)
            }),
        [tenantsQuery.data],
    )

    useEffect(() => {
        if (scopeMode !== 'tenant' || selectedTenantId || tenantOptions.length === 0) {
            return
        }

        setSelectedTenantId(tenantOptions[0].id)
    }, [scopeMode, selectedTenantId, tenantOptions])

    const activeTenantId = scopeMode === 'tenant' ? selectedTenantId || null : null
    const canLoadScopedSections = scopeMode === 'global' || Boolean(selectedTenantId)
    const previewSectionsQuery = useQuery({
        queryKey: homepageSectionsQueryKey(activeTenantId, 'preview'),
        queryFn: () => getHomepageSections(activeTenantId, 'preview'),
        enabled: canLoadScopedSections,
        staleTime: 15_000,
        refetchOnWindowFocus: true,
    })

    const sectionsQuery = useQuery({
        queryKey: homepageSectionsQueryKey(activeTenantId, viewMode),
        queryFn: () => getHomepageSections(activeTenantId, viewMode),
        enabled: canLoadScopedSections,
        staleTime: 15_000,
        refetchOnWindowFocus: true,
    })

    const saveMutation = useMutation({
        mutationFn: async ({
            id,
            payload,
        }: {
            id?: string
            payload: HomepageSectionPayload
        }) => (id ? updateHomepageSection(id, payload) : createHomepageSection(payload)),
    })

    const deleteMutation = useMutation({
        mutationFn: (sectionId: string) => deleteHomepageSection(sectionId),
    })

    const reorderMutation = useMutation({
        mutationFn: ({
            tenantId,
            sectionIds,
        }: {
            tenantId?: string | null
            sectionIds: string[]
        }) => reorderHomepageSections(sectionIds, tenantId),
    })

    const publishMutation = useMutation({
        mutationFn: (tenantId?: string | null) => publishHomepageSections(tenantId),
    })

    const sections = sectionsQuery.data?.sections ?? []
    const previewSections = previewSectionsQuery.data?.sections ?? []
    const schemas = schemasQuery.data ?? []
    const activeTenant =
        scopeMode === 'tenant'
            ? tenantOptions.find((tenant) => tenant.id === selectedTenantId) ?? null
            : null
    const scopeLabel = activeTenant ? `${activeTenant.name} tenant` : 'Global storefront default'
    const activeSectionCount = sections.filter((section) => section.isActive).length
    const hiddenSectionCount = sections.length - activeSectionCount
    const typesInUseCount = new Set(sections.map((section) => section.type)).size
    const hasDraftSections = previewSections.some((section) => section.isDraft)
    const displayedStateLabel = viewMode === 'preview' ? 'Draft preview' : 'Live published'
    const lastPublishedAt = getLatestPublishedTimestamp(viewMode === 'published' ? sections : previewSections)

    const selectedSchema =
        formState ? schemas.find((schema) => schema.type === formState.type) ?? null : null

    async function invalidateHomepageQueries() {
        await queryClient.invalidateQueries({ queryKey: ['super-admin', 'homepage'] })
    }

    function openCreateDialog() {
        if (!schemas.length) {
            showError('Section schemas are still loading. Please try again in a moment.')
            return
        }

        const defaultSchema = schemas[0]
        setFormState({
            type: defaultSchema.type,
            order: sections.length,
            isActive: true,
            data: buildEmptyData(defaultSchema),
        })
        setIsEditorOpen(true)
    }

    function openEditDialog(section: HomepageSectionRecord) {
        if (!schemas.length) {
            showError('Section schemas are still loading. Please try again in a moment.')
            return
        }

        setFormState(buildFormStateFromSection(section, schemas))
        setIsEditorOpen(true)
    }

    function closeEditor() {
        setIsEditorOpen(false)
        setFormState(null)
    }

    function handleTypeChange(nextType: HomepageSectionType) {
        const nextSchema = schemas.find((schema) => schema.type === nextType)
        if (!nextSchema) {
            return
        }

        setFormState((current) => {
            if (!current) {
                return current
            }

            return {
                ...current,
                type: nextType,
                data: remapDataToSchema(current.data, nextSchema),
            }
        })
    }

    function handleFieldChange(key: string, value: string) {
        setFormState((current) => {
            if (!current) {
                return current
            }

            return {
                ...current,
                data: {
                    ...current.data,
                    [key]: value,
                },
            }
        })
    }

    function handleVisibilityChange(isActive: boolean) {
        setFormState((current) => {
            if (!current) {
                return current
            }

            return {
                ...current,
                isActive,
            }
        })
    }

    async function handleSubmit() {
        if (!formState || !selectedSchema) {
            return
        }

        try {
            setViewMode('preview')
            await saveMutation.mutateAsync({
                id: formState.id,
                payload: {
                    type: formState.type,
                    order: formState.order,
                    data: normalizePayloadData(selectedSchema, formState.data),
                    schemaVersion: selectedSchema.version,
                    isActive: formState.isActive,
                    tenantId: activeTenantId,
                },
            })

            await invalidateHomepageQueries()
            showSuccess(formState.id ? 'Homepage section updated.' : 'Homepage section created.')
            closeEditor()
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to save the homepage section.'))
        }
    }

    async function handleDelete(section: HomepageSectionRecord) {
        const shouldDelete = window.confirm(
            `Remove the ${formatSectionType(section.type)} section from the ${scopeLabel} draft?`,
        )

        if (!shouldDelete) {
            return
        }

        try {
            setViewMode('preview')
            await deleteMutation.mutateAsync(section.id)
            await invalidateHomepageQueries()
            showSuccess('Homepage section removed from the draft.')

            if (formState?.id === section.id) {
                closeEditor()
            }
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to delete the homepage section.'))
        }
    }

    async function handleToggleVisibility(section: HomepageSectionRecord) {
        try {
            setViewMode('preview')
            await saveMutation.mutateAsync({
                id: section.id,
                payload: {
                    type: section.type,
                    order: section.order,
                    data: section.data,
                    schemaVersion: section.schemaVersion,
                    isActive: !section.isActive,
                    tenantId: section.tenantId ?? null,
                },
            })

            await invalidateHomepageQueries()
            showSuccess(section.isActive ? 'Section hidden in the draft preview.' : 'Section restored in the draft preview.')
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to update section visibility.'))
        }
    }

    async function handleMove(sectionId: string, direction: -1 | 1) {
        const currentIndex = sections.findIndex((section) => section.id === sectionId)
        const targetIndex = currentIndex + direction

        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= sections.length) {
            return
        }

        const reorderedIds = sections.map((section) => section.id)
        const [movedSectionId] = reorderedIds.splice(currentIndex, 1)
        reorderedIds.splice(targetIndex, 0, movedSectionId)

        try {
            setViewMode('preview')
            await reorderMutation.mutateAsync({
                tenantId: activeTenantId,
                sectionIds: reorderedIds,
            })

            await invalidateHomepageQueries()
            showSuccess('Draft homepage order normalized.')
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to reorder homepage sections.'))
        }
    }

    async function handleRefresh() {
        if (!canLoadScopedSections) {
            showInfo('Select a tenant to load tenant-scoped homepage sections.')
            return
        }

        await Promise.all([
            schemasQuery.refetch(),
            tenantsQuery.refetch(),
            previewSectionsQuery.refetch(),
            sectionsQuery.refetch(),
        ])
    }

    async function handlePublish() {
        if (!hasDraftSections) {
            showInfo('There is no unpublished draft to publish yet.')
            return
        }

        const shouldPublish = window.confirm(
            `Publish the current ${scopeLabel} homepage draft to live customers? This will replace the currently published homepage for this scope.`,
        )

        if (!shouldPublish) {
            return
        }

        try {
            await publishMutation.mutateAsync(activeTenantId)
            await invalidateHomepageQueries()
            setViewMode('published')
            showSuccess('Homepage draft published successfully.')
        } catch (error) {
            showError(getErrorMessage(error, 'Unable to publish the homepage draft.'))
        }
    }

    return (
        <div className="space-y-6">
            <section className="overflow-hidden rounded-[36px] border border-slate-200 bg-white shadow-[0_36px_100px_-56px_rgba(15,23,42,0.45)]">
                <div className="bg-[radial-gradient(circle_at_top_left,_rgba(14,165,233,0.18),_transparent_38%),radial-gradient(circle_at_top_right,_rgba(168,85,247,0.16),_transparent_42%),linear-gradient(180deg,#ffffff_0%,#f8fbff_100%)] p-8 sm:p-10">
                    <div className="flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
                        <div className="max-w-3xl">
                            <p className="text-xs font-semibold uppercase tracking-[0.26em] text-sky-600">
                                Shopixy Marketplace
                            </p>
                            <h1 className="mt-4 text-4xl font-semibold tracking-tight text-slate-950">
                                Homepage Manager
                            </h1>
                            <p className="mt-4 text-sm leading-7 text-slate-600 sm:text-base">
                                Manage persisted homepage sections with schema-backed validation, tenant overrides,
                                ordering controls, and publish visibility from one control surface.
                            </p>
                        </div>

                        <div className="flex flex-wrap items-center gap-3">
                            <button
                                type="button"
                                onClick={handleRefresh}
                                disabled={
                                    schemasQuery.isFetching ||
                                    tenantsQuery.isFetching ||
                                    sectionsQuery.isFetching ||
                                    previewSectionsQuery.isFetching
                                }
                                className="inline-flex items-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 shadow-sm transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60"
                            >
                                <RefreshCw
                                    className={`h-4 w-4 ${
                                        schemasQuery.isFetching ||
                                        tenantsQuery.isFetching ||
                                        sectionsQuery.isFetching ||
                                        previewSectionsQuery.isFetching
                                            ? 'animate-spin'
                                            : ''
                                    }`}
                                />
                                Refresh
                            </button>

                            <div className="inline-flex items-center rounded-2xl border border-slate-200 bg-white p-1 shadow-sm">
                                <button
                                    type="button"
                                    onClick={() => setViewMode('preview')}
                                    className={[
                                        'rounded-2xl px-4 py-2 text-sm font-semibold transition',
                                        viewMode === 'preview'
                                            ? 'bg-slate-950 text-white'
                                            : 'text-slate-600 hover:bg-slate-50',
                                    ].join(' ')}
                                >
                                    Preview
                                </button>
                                <button
                                    type="button"
                                    onClick={() => setViewMode('published')}
                                    className={[
                                        'rounded-2xl px-4 py-2 text-sm font-semibold transition',
                                        viewMode === 'published'
                                            ? 'bg-slate-950 text-white'
                                            : 'text-slate-600 hover:bg-slate-50',
                                    ].join(' ')}
                                >
                                    Live
                                </button>
                            </div>

                            <button
                                type="button"
                                onClick={handlePublish}
                                disabled={!hasDraftSections || publishMutation.isPending}
                                className="inline-flex items-center gap-2 rounded-2xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm font-semibold text-emerald-700 shadow-sm transition hover:bg-emerald-100 disabled:cursor-not-allowed disabled:opacity-60"
                            >
                                {publishMutation.isPending ? (
                                    <Loader2 className="h-4 w-4 animate-spin" />
                                ) : (
                                    <Sparkles className="h-4 w-4" />
                                )}
                                Publish Draft
                            </button>

                            <button
                                type="button"
                                onClick={openCreateDialog}
                                disabled={!canLoadScopedSections || !schemas.length}
                                className="inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white shadow-lg shadow-slate-900/15 transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
                            >
                                <Plus className="h-4 w-4" />
                                Add Section
                            </button>
                        </div>
                    </div>

                    <div className="mt-8 grid gap-4 lg:grid-cols-4">
                        <div className="rounded-[28px] border border-white/60 bg-white/80 p-5 shadow-sm">
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Scope</p>
                            <p className="mt-3 text-2xl font-semibold text-slate-950">{activeTenant ? 'Tenant' : 'Global'}</p>
                            <p className="mt-2 text-sm text-slate-500">{scopeLabel}</p>
                        </div>

                        <div className="rounded-[28px] border border-white/60 bg-white/80 p-5 shadow-sm">
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">View</p>
                            <p className="mt-3 text-2xl font-semibold text-slate-950">{displayedStateLabel}</p>
                            <p className="mt-2 text-sm text-slate-500">
                                {hasDraftSections ? 'Draft workspace is active for this scope' : 'No unpublished draft changes'}
                            </p>
                        </div>

                        <div className="rounded-[28px] border border-white/60 bg-white/80 p-5 shadow-sm">
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Visible</p>
                            <p className="mt-3 text-2xl font-semibold text-slate-950">{activeSectionCount}</p>
                            <p className="mt-2 text-sm text-slate-500">{hiddenSectionCount} hidden sections ready for draft work</p>
                        </div>

                        <div className="rounded-[28px] border border-white/60 bg-white/80 p-5 shadow-sm">
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Last Published</p>
                            <p className="mt-3 text-2xl font-semibold text-slate-950">{lastPublishedAt ? 'Tracked' : 'None'}</p>
                            <p className="mt-2 text-sm text-slate-500">
                                {lastPublishedAt ? formatTimestamp(lastPublishedAt) : `${typesInUseCount} layouts in use, ${schemas.length} schemas available`}
                            </p>
                        </div>
                    </div>
                </div>
            </section>

            <section className="rounded-[32px] border border-slate-200 bg-white p-6 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                <div className="flex flex-col gap-5 xl:flex-row xl:items-end xl:justify-between">
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-sky-600">
                            Section Scope
                        </p>
                        <h2 className="mt-3 text-2xl font-semibold text-slate-950">Resolve which homepage you are editing</h2>
                        <p className="mt-3 max-w-2xl text-sm leading-6 text-slate-500">
                            Global sections are the storefront fallback. Tenant-scoped sections override the global homepage when that tenant has active entries configured.
                        </p>
                    </div>

                    <div className="rounded-[28px] border border-slate-200 bg-slate-50 px-4 py-3">
                        <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">Current target</p>
                        <p className="mt-2 text-sm font-semibold text-slate-950">{scopeLabel}</p>
                    </div>
                </div>

                <div className="mt-6 grid gap-4 lg:grid-cols-[auto_auto_1fr]">
                    <ScopeButton
                        isActive={scopeMode === 'global'}
                        label="Global Default"
                        description="Fallback homepage shared when a tenant has no override."
                        onClick={() => setScopeMode('global')}
                    />
                    <ScopeButton
                        isActive={scopeMode === 'tenant'}
                        label="Tenant Override"
                        description="Target a single tenant with a managed homepage section list."
                        onClick={() => setScopeMode('tenant')}
                    />

                    <div className="rounded-[28px] border border-slate-200 bg-white p-4">
                        <label className="space-y-2">
                            <span className="text-sm font-semibold text-slate-900">Tenant selector</span>
                            <select
                                value={selectedTenantId}
                                onChange={(event) => setSelectedTenantId(event.target.value)}
                                disabled={scopeMode !== 'tenant' || tenantsQuery.isLoading || tenantOptions.length === 0}
                                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-sky-300 focus:ring-4 focus:ring-sky-100 disabled:cursor-not-allowed disabled:bg-slate-50 disabled:text-slate-400"
                            >
                                {tenantOptions.length === 0 ? (
                                    <option value="">No tenants available</option>
                                ) : null}
                                {tenantOptions.map((tenant) => (
                                    <option key={tenant.id} value={tenant.id}>
                                        {tenant.name}{tenant.isActive ? '' : ' (inactive)'}
                                    </option>
                                ))}
                            </select>
                        </label>
                        {scopeMode === 'tenant' && activeTenant ? (
                            <p className="mt-3 text-xs leading-5 text-slate-500">
                                Editing override for {activeTenant.name}. Slug: {activeTenant.slug || 'Not set'}.
                            </p>
                        ) : (
                            <p className="mt-3 text-xs leading-5 text-slate-500">
                                Choose tenant mode to manage a per-tenant storefront override.
                            </p>
                        )}
                    </div>
                </div>
            </section>

            <section
                className={[
                    'rounded-[32px] border p-6 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.28)]',
                    viewMode === 'preview'
                        ? 'border-amber-200 bg-amber-50/80'
                        : 'border-emerald-200 bg-emerald-50/70',
                ].join(' ')}
            >
                <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                    <div>
                        <p
                            className={[
                                'text-xs font-semibold uppercase tracking-[0.22em]',
                                viewMode === 'preview' ? 'text-amber-700' : 'text-emerald-700',
                            ].join(' ')}
                        >
                            {viewMode === 'preview' ? 'Safe Draft Mode' : 'Live Published View'}
                        </p>
                        <h2 className="mt-3 text-2xl font-semibold text-slate-950">
                            {viewMode === 'preview' ? 'You are editing draft content' : 'You are reviewing live content'}
                        </h2>
                        <p className="mt-3 max-w-3xl text-sm leading-6 text-slate-600">
                            {viewMode === 'preview'
                                ? hasDraftSections
                                    ? 'Draft changes are isolated from customers until you publish. Use this view to safely reorder, update, or remove sections.'
                                    : 'Preview mode is active, but there are no draft-only changes yet. Your next edit will create or update the draft workspace for this scope.'
                                : hasDraftSections
                                  ? 'Published mode shows what customers currently receive. A separate draft is waiting in preview mode and will not affect live traffic until you publish it.'
                                  : 'Published mode shows the live homepage currently being served to customers.'}
                        </p>
                    </div>

                    <div className="rounded-[28px] border border-white/70 bg-white/80 px-5 py-4 shadow-sm">
                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Publish readiness</p>
                        <p className="mt-2 text-sm font-semibold text-slate-950">
                            {hasDraftSections ? 'Draft ready to publish' : 'No unpublished changes'}
                        </p>
                        <p className="mt-2 text-xs leading-5 text-slate-500">
                            Publishing archives the current live version and promotes the current draft for this scope.
                        </p>
                    </div>
                </div>
            </section>

            {!canLoadScopedSections ? (
                <section className="grid min-h-[340px] place-items-center rounded-[32px] border border-dashed border-slate-300 bg-white p-8 text-center shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                    <div className="max-w-xl">
                        <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-sky-100 text-sky-700">
                            <LayoutTemplate className="h-6 w-6" />
                        </div>
                        <h2 className="mt-5 text-2xl font-semibold text-slate-950">Choose a tenant override</h2>
                        <p className="mt-3 text-sm leading-6 text-slate-500">
                            Tenant-scoped management is ready, but it needs a selected tenant before sections can be loaded or saved.
                        </p>
                    </div>
                </section>
            ) : sectionsQuery.isError ? (
                <section className="grid min-h-[340px] place-items-center rounded-[32px] border border-slate-200 bg-white p-8 text-center shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                    <div className="max-w-xl">
                        <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-rose-50 text-rose-600">
                            <ShieldAlert className="h-6 w-6" />
                        </div>
                        <h2 className="mt-5 text-2xl font-semibold text-slate-950">Homepage sections unavailable</h2>
                        <p className="mt-3 text-sm leading-6 text-slate-500">
                            {getErrorMessage(sectionsQuery.error, 'Unable to load homepage sections for this scope.')}
                        </p>
                        <button
                            type="button"
                            onClick={() => sectionsQuery.refetch()}
                            className="mt-6 inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800"
                        >
                            <RefreshCw className="h-4 w-4" />
                            Retry
                        </button>
                    </div>
                </section>
            ) : (
                <section className="space-y-4">
                    {sectionsQuery.isLoading && !sections.length ? (
                        Array.from({ length: 3 }).map((_, index) => (
                            <div
                                key={`homepage-section-skeleton-${index}`}
                                className="h-48 animate-pulse rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]"
                            />
                        ))
                    ) : sections.length === 0 ? (
                        <div className="grid min-h-[340px] place-items-center rounded-[32px] border border-dashed border-slate-300 bg-white p-8 text-center shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                            <div className="max-w-xl">
                                <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-violet-100 text-violet-700">
                                    <Sparkles className="h-6 w-6" />
                                </div>
                                <h2 className="mt-5 text-2xl font-semibold text-slate-950">No sections configured yet</h2>
                                <p className="mt-3 text-sm leading-6 text-slate-500">
                                    Start with a hero, featured products, or category section. Every record is persisted and validated through the homepage schema registry before it reaches the storefront.
                                </p>
                                <button
                                    type="button"
                                    onClick={openCreateDialog}
                                    className="mt-6 inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800"
                                >
                                    <Plus className="h-4 w-4" />
                                    Add first section
                                </button>
                            </div>
                        </div>
                    ) : (
                        sections.map((section, index) => {
                            const schema = schemas.find((candidate) => candidate.type === section.type) ?? null
                            const isBusy =
                                saveMutation.isPending ||
                                deleteMutation.isPending ||
                                reorderMutation.isPending ||
                                publishMutation.isPending

                            return (
                                <article
                                    key={section.id}
                                    className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]"
                                >
                                    <div className="grid gap-6 p-6 xl:grid-cols-[1.15fr_0.85fr] xl:p-8">
                                        <div>
                                            <div className="flex flex-wrap items-center gap-3">
                                                <span className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-slate-600">
                                                    <LayoutTemplate className="h-3.5 w-3.5" />
                                                    {formatSectionType(section.type)}
                                                </span>

                                                <span
                                                    className={[
                                                        'rounded-full border px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em]',
                                                        section.isActive
                                                            ? 'border-emerald-200 bg-emerald-50 text-emerald-700'
                                                            : 'border-slate-200 bg-slate-100 text-slate-500',
                                                    ].join(' ')}
                                                >
                                                    {section.isActive ? 'Active' : 'Hidden'}
                                                </span>

                                                <span className="rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-sky-700">
                                                    Order {section.order + 1}
                                                </span>

                                                <span
                                                    className={[
                                                        'rounded-full border px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em]',
                                                        section.isDraft
                                                            ? 'border-amber-200 bg-amber-50 text-amber-700'
                                                            : 'border-indigo-200 bg-indigo-50 text-indigo-700',
                                                    ].join(' ')}
                                                >
                                                    {section.isDraft ? 'Draft' : 'Live'}
                                                </span>
                                            </div>

                                            <h2 className="mt-5 text-2xl font-semibold text-slate-950">
                                                {String(section.data.title ?? schema?.displayName ?? formatSectionType(section.type))}
                                            </h2>
                                            <p className="mt-3 text-sm leading-6 text-slate-500">
                                                {schema?.description ?? 'Schema-backed homepage section ready for storefront rendering.'}
                                            </p>

                                            <dl className="mt-6 grid gap-4 sm:grid-cols-2">
                                                {(schema?.fields ?? []).slice(0, 4).map((field) => (
                                                    <div key={field.key} className="rounded-[24px] border border-slate-200 bg-slate-50 p-4">
                                                        <dt className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                                            {field.label}
                                                        </dt>
                                                        <dd className="mt-2 text-sm font-medium text-slate-900">
                                                            {summarizeValue(section.data[field.key])}
                                                        </dd>
                                                    </div>
                                                ))}
                                            </dl>
                                        </div>

                                        <div className="space-y-4 rounded-[28px] border border-slate-200 bg-slate-50/80 p-5">
                                            <div>
                                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                                    Metadata
                                                </p>
                                                <div className="mt-4 space-y-3 text-sm text-slate-600">
                                                    <div className="flex items-center justify-between gap-4">
                                                        <span>Created</span>
                                                        <span className="text-right font-medium text-slate-900">
                                                            {formatTimestamp(section.createdAt)}
                                                        </span>
                                                    </div>
                                                    <div className="flex items-center justify-between gap-4">
                                                        <span>Updated</span>
                                                        <span className="text-right font-medium text-slate-900">
                                                            {formatTimestamp(section.updatedAt)}
                                                        </span>
                                                    </div>
                                                    <div className="flex items-center justify-between gap-4">
                                                        <span>Scope</span>
                                                        <span className="text-right font-medium text-slate-900">
                                                            {section.tenantId ? 'Tenant override' : 'Global default'}
                                                        </span>
                                                    </div>
                                                    <div className="flex items-center justify-between gap-4">
                                                        <span>Version</span>
                                                        <span className="text-right font-medium text-slate-900">
                                                            v{section.version} · schema v{section.schemaVersion}
                                                        </span>
                                                    </div>
                                                    <div className="flex items-center justify-between gap-4">
                                                        <span>Published</span>
                                                        <span className="text-right font-medium text-slate-900">
                                                            {section.publishedAt ? formatTimestamp(section.publishedAt) : 'Not published yet'}
                                                        </span>
                                                    </div>
                                                </div>
                                            </div>

                                            <div className="grid gap-3 sm:grid-cols-2">
                                                <button
                                                    type="button"
                                                    onClick={() => openEditDialog(section)}
                                                    disabled={isBusy}
                                                    className="inline-flex items-center justify-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-60"
                                                >
                                                    <Pencil className="h-4 w-4" />
                                                    Edit
                                                </button>

                                                <button
                                                    type="button"
                                                    onClick={() => handleToggleVisibility(section)}
                                                    disabled={isBusy}
                                                    className="inline-flex items-center justify-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-60"
                                                >
                                                    {section.isActive ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                                                    {section.isActive ? 'Hide' : 'Show'}
                                                </button>

                                                <button
                                                    type="button"
                                                    onClick={() => handleMove(section.id, -1)}
                                                    disabled={isBusy || index === 0}
                                                    className="inline-flex items-center justify-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-60"
                                                >
                                                    <ArrowUp className="h-4 w-4" />
                                                    Move up
                                                </button>

                                                <button
                                                    type="button"
                                                    onClick={() => handleMove(section.id, 1)}
                                                    disabled={isBusy || index === sections.length - 1}
                                                    className="inline-flex items-center justify-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-60"
                                                >
                                                    <ArrowDown className="h-4 w-4" />
                                                    Move down
                                                </button>
                                            </div>

                                            <button
                                                type="button"
                                                onClick={() => handleDelete(section)}
                                                disabled={isBusy}
                                                className="inline-flex w-full items-center justify-center gap-2 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-semibold text-rose-700 transition hover:bg-rose-100 disabled:cursor-not-allowed disabled:opacity-60"
                                            >
                                                {deleteMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Trash2 className="h-4 w-4" />}
                                                Remove from draft
                                            </button>
                                        </div>
                                    </div>
                                </article>
                            )
                        })
                    )}
                </section>
            )}

            <SectionEditorModal
                isOpen={isEditorOpen}
                scopeLabel={scopeLabel}
                schemas={schemas}
                formState={formState}
                isSubmitting={saveMutation.isPending}
                onClose={closeEditor}
                onSubmit={handleSubmit}
                onTypeChange={handleTypeChange}
                onFieldChange={handleFieldChange}
                onVisibilityChange={handleVisibilityChange}
            />
        </div>
    )
}

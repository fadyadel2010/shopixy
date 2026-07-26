import type {
    HomepageSectionFieldSchema,
    HomepageSectionRecord,
    HomepageSectionSchema,
    HomepageSectionType,
    SaveHomepageDraftPayload,
    SaveHomepageDraftSectionPayload,
} from './homepageService'

const GUID_PATTERN =
    /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

export interface HomepageBuilderSection {
    clientId: string
    persistedId?: string | null
    type: HomepageSectionType
    order: number
    isActive: boolean
    schemaVersion: number
    tenantId?: string | null
    createdAt?: string
    updatedAt?: string
    version?: number
    data: Record<string, unknown>
}

export interface HomepageBuilderValidationIssue {
    fieldKey: string
    message: string
}

export type HomepageBuilderValidationMap = Record<string, HomepageBuilderValidationIssue[]>

export function formatSectionType(type: HomepageSectionType): string {
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

export function createHomepageBuilderSectionId(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return `temp-${crypto.randomUUID()}`
    }

    return `temp-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

export function getSectionSchema(
    type: HomepageSectionType,
    schemas: HomepageSectionSchema[],
): HomepageSectionSchema | null {
    return schemas.find((schema) => schema.type === type) ?? null
}

function getLocalizedFieldKey(fieldKey: string): string {
    return `${fieldKey}Localized`
}

function createLocalizedField(fallback = ''): Record<string, string> {
    return {
        en: fallback,
        ar: '',
    }
}

function getDefaultFieldValue(field: HomepageSectionFieldSchema): unknown {
    if (field.kind === 'guid-list') {
        return []
    }

    if (field.kind === 'number') {
        return ''
    }

    if (field.kind === 'select') {
        return field.options[0] ?? ''
    }

    return ''
}

function normalizeGuidListValue(value: unknown): string[] {
    if (!Array.isArray(value)) {
        return []
    }

    return value
        .map((entry) => String(entry ?? '').trim())
        .filter(Boolean)
}

function normalizeFieldValue(field: HomepageSectionFieldSchema, value: unknown): unknown {
    if (field.kind === 'guid-list') {
        return normalizeGuidListValue(value)
    }

    if (field.kind === 'number') {
        if (typeof value === 'number' && Number.isFinite(value)) {
            return String(value)
        }

        if (typeof value === 'string') {
            return value.trim()
        }

        return ''
    }

    if (typeof value === 'string') {
        return value
    }

    if (typeof value === 'number' || typeof value === 'boolean') {
        return String(value)
    }

    return getDefaultFieldValue(field)
}

export function buildSectionDataDefaults(schema: HomepageSectionSchema): Record<string, unknown> {
    return schema.fields.reduce<Record<string, unknown>>((accumulator, field) => {
        accumulator[field.key] = getDefaultFieldValue(field)
        if (field.isLocalized) {
            accumulator[getLocalizedFieldKey(field.key)] = createLocalizedField('')
        }
        return accumulator
    }, {})
}

export function createBuilderSection(
    schema: HomepageSectionSchema,
    order: number,
    tenantId?: string | null,
): HomepageBuilderSection {
    return {
        clientId: createHomepageBuilderSectionId(),
        persistedId: null,
        type: schema.type,
        order,
        isActive: true,
        schemaVersion: schema.version,
        tenantId: tenantId ?? null,
        data: buildSectionDataDefaults(schema),
    }
}

function inferLegacyCollectionSource(
    schema: HomepageSectionSchema,
    recordData: Record<string, unknown>,
): string {
    if (schema.type === 'products') {
        return normalizeGuidListValue(recordData.productIds).length > 0 ? 'curated' : 'all'
    }

    if (schema.type === 'categories') {
        return normalizeGuidListValue(recordData.categoryIds).length > 0 ? 'curated' : 'all'
    }

    return 'all'
}

export function mapRecordToBuilderSection(
    record: HomepageSectionRecord,
    schemas: HomepageSectionSchema[],
): HomepageBuilderSection {
    const schema = getSectionSchema(record.type, schemas)
    const normalizedData = schema
        ? schema.fields.reduce<Record<string, unknown>>((accumulator, field) => {
              if (field.key === 'source' && record.data[field.key] == null) {
                  accumulator[field.key] = inferLegacyCollectionSource(schema, record.data)
                  if (field.isLocalized) {
                      accumulator[getLocalizedFieldKey(field.key)] = normalizeLocalizedFieldValue(
                          record.data[getLocalizedFieldKey(field.key)],
                          accumulator[field.key],
                      )
                  }
                  return accumulator
              }

              accumulator[field.key] = normalizeFieldValue(field, record.data[field.key])
              if (field.isLocalized) {
                  accumulator[getLocalizedFieldKey(field.key)] = normalizeLocalizedFieldValue(
                      record.data[getLocalizedFieldKey(field.key)],
                      accumulator[field.key],
                  )
              }
              return accumulator
          }, {})
        : Object.entries(record.data).reduce<Record<string, unknown>>((accumulator, [key, value]) => {
              accumulator[key] = value
              return accumulator
          }, {})

    return {
        clientId: record.id,
        persistedId: record.id,
        type: record.type,
        order: record.order,
        isActive: record.isActive,
        schemaVersion: record.schemaVersion,
        tenantId: record.tenantId ?? null,
        createdAt: record.createdAt,
        updatedAt: record.updatedAt,
        version: record.version,
        data: normalizedData,
    }
}

export function hydrateBuilderSections(
    sections: HomepageSectionRecord[],
    schemas: HomepageSectionSchema[],
): HomepageBuilderSection[] {
    return normalizeBuilderSectionsOrder(sections.map((section) => mapRecordToBuilderSection(section, schemas)))
}

export function normalizeBuilderSectionsOrder(
    sections: HomepageBuilderSection[],
): HomepageBuilderSection[] {
    return sections.map((section, index) => ({
        ...section,
        order: index,
    }))
}

export function getFieldDisplayValue(
    field: HomepageSectionFieldSchema,
    value: unknown,
): string {
    if (field.kind === 'guid-list') {
        return normalizeGuidListValue(value).join('\n')
    }

    if (typeof value === 'string') {
        return value
    }

    if (typeof value === 'number' || typeof value === 'boolean') {
        return String(value)
    }

    return ''
}

function normalizeLocalizedFieldValue(value: unknown, fallback: unknown): Record<string, string> {
    const fallbackValue =
        typeof fallback === 'string'
            ? fallback
            : typeof fallback === 'number' || typeof fallback === 'boolean'
                ? String(fallback)
                : ''

    if (!value || typeof value !== 'object' || Array.isArray(value)) {
        return createLocalizedField(fallbackValue)
    }

    const localizedValue = value as { en?: unknown; ar?: unknown }

    return {
        en: typeof localizedValue.en === 'string' && localizedValue.en.trim() ? localizedValue.en : fallbackValue,
        ar: typeof localizedValue.ar === 'string' ? localizedValue.ar : '',
    }
}

export function getLocalizedFieldDisplayValue(
    sectionData: Record<string, unknown>,
    field: HomepageSectionFieldSchema,
    language: 'en' | 'ar',
): string {
    if (!field.isLocalized) {
        return getFieldDisplayValue(field, sectionData[field.key])
    }

    const localizedField = normalizeLocalizedFieldValue(
        sectionData[getLocalizedFieldKey(field.key)],
        sectionData[field.key],
    )

    return localizedField[language] || ''
}

export function parseFieldInputValue(
    field: HomepageSectionFieldSchema,
    rawValue: string,
): unknown {
    if (field.kind === 'guid-list') {
        return rawValue
            .split(/[\n,]+/)
            .map((entry) => entry.trim())
            .filter(Boolean)
    }

    return rawValue
}

export function updateLocalizedFieldValue(
    sectionData: Record<string, unknown>,
    field: HomepageSectionFieldSchema,
    language: 'en' | 'ar',
    rawValue: string,
): Record<string, unknown> {
    if (!field.isLocalized) {
        return {
            ...sectionData,
            [field.key]: parseFieldInputValue(field, rawValue),
        }
    }

    const localizedKey = getLocalizedFieldKey(field.key)
    const localizedField = normalizeLocalizedFieldValue(sectionData[localizedKey], sectionData[field.key])
    const nextValue = rawValue

    return {
        ...sectionData,
        [field.key]: language === 'en' ? parseFieldInputValue(field, rawValue) : sectionData[field.key],
        [localizedKey]: {
            ...localizedField,
            [language]: nextValue,
        },
    }
}

function serializeFieldValue(field: HomepageSectionFieldSchema, value: unknown): unknown {
    if (field.kind === 'guid-list') {
        return normalizeGuidListValue(value)
    }

    if (field.kind === 'number') {
        const stringValue = typeof value === 'string' ? value : String(value ?? '')
        return stringValue.trim()
    }

    const stringValue = typeof value === 'string' ? value : String(value ?? '')
    return stringValue.trim()
}

export function serializeBuilderSection(
    section: HomepageBuilderSection,
    schema: HomepageSectionSchema | null,
): SaveHomepageDraftSectionPayload {
    const serializedData = schema
        ? schema.fields.reduce<Record<string, unknown>>((accumulator, field) => {
              accumulator[field.key] = serializeFieldValue(field, section.data[field.key])
              if (field.isLocalized) {
                  const localizedValue = normalizeLocalizedFieldValue(
                      section.data[getLocalizedFieldKey(field.key)],
                      accumulator[field.key],
                  )
                  if (localizedValue.en || localizedValue.ar) {
                      accumulator[getLocalizedFieldKey(field.key)] = localizedValue
                  }
              }
              return accumulator
          }, {})
        : { ...section.data }

    return {
        id: section.persistedId ?? null,
        type: section.type,
        order: section.order,
        data: serializedData,
        schemaVersion: section.schemaVersion,
        isActive: section.isActive,
    }
}

export function buildSaveHomepageDraftPayload(
    sections: HomepageBuilderSection[],
    schemas: HomepageSectionSchema[],
    tenantId?: string | null,
): SaveHomepageDraftPayload {
    const normalizedSections = normalizeBuilderSectionsOrder(sections)
    return {
        tenantId: tenantId ?? null,
        sections: normalizedSections.map((section) =>
            serializeBuilderSection(section, getSectionSchema(section.type, schemas)),
        ),
    }
}

export function createBuilderSectionsFingerprint(
    sections: HomepageBuilderSection[],
    schemas: HomepageSectionSchema[],
): string {
    return JSON.stringify(buildSaveHomepageDraftPayload(sections, schemas).sections)
}

export function canAutoPersistReorder(
    sections: HomepageBuilderSection[],
    removedPersistedIds: string[],
): boolean {
    return (
        removedPersistedIds.length === 0 &&
        sections.length > 0 &&
        sections.every((section) => Boolean(section.persistedId))
    )
}

export function validateBuilderSections(
    sections: HomepageBuilderSection[],
    schemas: HomepageSectionSchema[],
): HomepageBuilderValidationMap {
    return sections.reduce<HomepageBuilderValidationMap>((accumulator, section) => {
        const schema = getSectionSchema(section.type, schemas)
        if (!schema) {
            accumulator[section.clientId] = [
                {
                    fieldKey: '__schema__',
                    message: 'Schema definition is unavailable for this section type.',
                },
            ]
            return accumulator
        }

        const issues = schema.fields.reduce<HomepageBuilderValidationIssue[]>((fieldIssues, field) => {
            const value = section.data[field.key]

            if (field.kind === 'guid-list') {
                const ids = normalizeGuidListValue(value)
                if (field.required && ids.length === 0) {
                    fieldIssues.push({
                        fieldKey: field.key,
                        message: `${field.label} requires at least one identifier.`,
                    })
                    return fieldIssues
                }

                if (ids.some((entry) => !GUID_PATTERN.test(entry))) {
                    fieldIssues.push({
                        fieldKey: field.key,
                        message: `${field.label} must contain valid GUID values.`,
                    })
                }

                return fieldIssues
            }

            if (field.kind === 'number') {
                const stringValue = typeof value === 'string' ? value.trim() : String(value ?? '').trim()
                if (field.required && !stringValue) {
                    fieldIssues.push({
                        fieldKey: field.key,
                        message: `${field.label} is required.`,
                    })
                    return fieldIssues
                }

                if (stringValue) {
                    const numericValue = Number(stringValue)
                    if (!Number.isInteger(numericValue) || numericValue <= 0) {
                        fieldIssues.push({
                            fieldKey: field.key,
                            message: `${field.label} must be a positive whole number.`,
                        })
                    }
                }

                return fieldIssues
            }

            const stringValue = typeof value === 'string' ? value.trim() : String(value ?? '').trim()
            if (field.required && !stringValue) {
                fieldIssues.push({
                    fieldKey: field.key,
                    message: `${field.label} is required.`,
                })
            }

            if (field.kind === 'select' && stringValue && !field.options.includes(stringValue)) {
                fieldIssues.push({
                    fieldKey: field.key,
                    message: `${field.label} must use one of the supported options.`,
                })
            }

            return fieldIssues
        }, [])

        const sourceValue =
            typeof section.data.source === 'string' ? section.data.source.trim().toLowerCase() : 'all'

        if (schema.type === 'products' && sourceValue === 'curated') {
            const productIds = normalizeGuidListValue(section.data.productIds)
            if (productIds.length === 0) {
                issues.push({
                    fieldKey: 'productIds',
                    message: 'Curated products require at least one product identifier.',
                })
            }
        }

        if (schema.type === 'categories' && sourceValue === 'curated') {
            const categoryIds = normalizeGuidListValue(section.data.categoryIds)
            if (categoryIds.length === 0) {
                issues.push({
                    fieldKey: 'categoryIds',
                    message: 'Curated categories require at least one category identifier.',
                })
            }
        }

        if (issues.length > 0) {
            accumulator[section.clientId] = issues
        }

        return accumulator
    }, {})
}

export function hasValidationIssues(validationMap: HomepageBuilderValidationMap): boolean {
    return Object.values(validationMap).some((issues) => issues.length > 0)
}

export function getSectionTitle(
    section: HomepageBuilderSection,
    schemas: HomepageSectionSchema[],
): string {
    const title = typeof section.data.title === 'string' ? section.data.title.trim() : ''
    if (title) {
        return title
    }

    return getSectionSchema(section.type, schemas)?.displayName ?? formatSectionType(section.type)
}

export function getSectionTitleForLanguage(
    section: HomepageBuilderSection,
    schemas: HomepageSectionSchema[],
    language: 'en' | 'ar',
): string {
    const schema = getSectionSchema(section.type, schemas)
    const titleField = schema?.fields.find((field) => field.key === 'title') ?? null
    if (titleField?.isLocalized) {
        const localizedTitle = getLocalizedFieldDisplayValue(section.data, titleField, language).trim()
        if (localizedTitle) {
            return localizedTitle
        }
    }

    return getSectionTitle(section, schemas)
}

export function resolveSectionDataForLanguage(
    section: HomepageBuilderSection,
    schema: HomepageSectionSchema | null,
    language: 'en' | 'ar',
): Record<string, unknown> {
    if (!schema) {
        return section.data
    }

    return schema.fields.reduce<Record<string, unknown>>((accumulator, field) => {
        if (!field.isLocalized) {
            accumulator[field.key] = section.data[field.key]
            return accumulator
        }

        const localizedField = normalizeLocalizedFieldValue(
            section.data[getLocalizedFieldKey(field.key)],
            section.data[field.key],
        )

        accumulator[field.key] = language === 'ar'
            ? localizedField.ar || localizedField.en || ''
            : localizedField.en || localizedField.ar || ''
        accumulator[getLocalizedFieldKey(field.key)] = localizedField
        return accumulator
    }, {})
}

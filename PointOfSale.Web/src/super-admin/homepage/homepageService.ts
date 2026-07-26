import { superAdminApiClient } from '../core/superAdminApiClient'
import type { SuperAdminApiMeta } from '../auth/superAdminAuth.types'
import type { StoreConfig } from '@/types/storeConfig'

export type HomepageSectionType = 'hero' | 'products' | 'banner' | 'categories'
export type HomepageViewMode = 'preview' | 'published'

export interface HomepageSectionRecord {
    id: string
    type: HomepageSectionType
    order: number
    data: Record<string, unknown>
    version: number
    schemaVersion: number
    isActive: boolean
    isDraft: boolean
    isPublished: boolean
    publishedAt?: string | null
    draftGroupId?: string | null
    tenantId?: string | null
    createdAt: string
    updatedAt: string
}

export interface HomepageSectionFieldSchema {
    key: string
    label: string
    kind: 'text' | 'textarea' | 'image' | 'link' | 'guid-list' | 'select' | 'number'
    required: boolean
    isLocalized?: boolean
    helpText?: string | null
    options: string[]
}

export interface HomepageSectionSchema {
    type: HomepageSectionType
    displayName: string
    description: string
    version: number
    fields: HomepageSectionFieldSchema[]
}

export interface HomepageSectionsResult {
    sections: HomepageSectionRecord[]
    meta?: SuperAdminApiMeta
}

export interface HomepageSectionPayload {
    type: HomepageSectionType
    order: number
    data: Record<string, unknown>
    schemaVersion?: number
    isActive: boolean
    tenantId?: string | null
}

export interface SaveHomepageDraftSectionPayload {
    id?: string | null
    type: HomepageSectionType
    order: number
    data: Record<string, unknown>
    schemaVersion?: number
    isActive: boolean
}

export interface SaveHomepageDraftPayload {
    tenantId?: string | null
    sections: SaveHomepageDraftSectionPayload[]
}

export interface TenantOption {
    id: string
    name: string
    slug?: string | null
    isActive: boolean
    contactEmail?: string | null
    createdAtUtc: string
}

export interface GenerateHomepageTemplatePayload {
    tenantId?: string | null
    overwriteExisting: boolean
}

export interface HomepagePreviewProductDetail {
    id: string
    tenantId: string
    storeName: string
    storeShippingFee: number
    productId: string
    initialVariantId: string
    productName: string
    description?: string | null
    image?: string | null
    price: number
    currency: string
    categoryId: string
    categoryName?: string | null
    categoryPath?: string | null
    inStock: boolean
    stockQuantity: number
    isPublished: boolean
    isAvailable: boolean
    hasVariants: boolean
    defaultVariantId?: string | null
    images: string[]
}

export interface HomepagePreviewCategory {
    id: string
    name: string
    nameAr?: string | null
    slug: string
    parentId?: string | null
    level: number
    iconEmoji?: string | null
    hasAttributes: boolean
}

function buildSectionsUrl(tenantId?: string | null, mode: HomepageViewMode = 'preview'): string {
    const params = new URLSearchParams({ mode })
    if (tenantId) {
        params.set('tenantId', tenantId)
    }

    const query = params.toString()
    if (!query) {
        return '/homepage/sections'
    }

    return `/homepage/sections?${query}`
}

export async function getHomepageSections(
    tenantId?: string | null,
    mode: HomepageViewMode = 'preview',
): Promise<HomepageSectionsResult> {
    const response = await superAdminApiClient.get<HomepageSectionRecord[]>(buildSectionsUrl(tenantId, mode))

    return {
        sections: response.data,
        meta: response.meta,
    }
}

export async function getHomepageSchemas(): Promise<HomepageSectionSchema[]> {
    const response = await superAdminApiClient.get<HomepageSectionSchema[]>('/homepage/sections/schemas')
    return response.data
}

export async function createHomepageSection(payload: HomepageSectionPayload): Promise<HomepageSectionRecord> {
    const response = await superAdminApiClient.post<HomepageSectionRecord, HomepageSectionPayload>(
        '/homepage/sections',
        payload,
    )

    return response.data
}

export async function updateHomepageSection(
    id: string,
    payload: HomepageSectionPayload,
): Promise<HomepageSectionRecord> {
    const response = await superAdminApiClient.put<HomepageSectionRecord, HomepageSectionPayload>(
        `/homepage/sections/${id}`,
        payload,
    )

    return response.data
}

export async function deleteHomepageSection(id: string): Promise<HomepageSectionRecord> {
    const response = await superAdminApiClient.delete<HomepageSectionRecord>(`/homepage/sections/${id}`)
    return response.data
}

export async function reorderHomepageSections(
    sectionIds: string[],
    tenantId?: string | null,
): Promise<HomepageSectionRecord[]> {
    const response = await superAdminApiClient.post<HomepageSectionRecord[], { tenantId?: string | null; sectionIds: string[] }>(
        '/homepage/sections/reorder',
        {
            tenantId: tenantId ?? null,
            sectionIds,
        },
    )

    return response.data
}

export async function publishHomepageSections(tenantId?: string | null): Promise<HomepageSectionRecord[]> {
    const response = await superAdminApiClient.post<HomepageSectionRecord[], { tenantId?: string | null }>(
        '/homepage/sections/publish',
        {
            tenantId: tenantId ?? null,
        },
    )

    return response.data
}

export async function saveHomepageDraft(payload: SaveHomepageDraftPayload): Promise<HomepageSectionRecord[]> {
    const response = await superAdminApiClient.put<HomepageSectionRecord[], SaveHomepageDraftPayload>(
        '/homepage/sections/draft',
        {
            tenantId: payload.tenantId ?? null,
            sections: payload.sections.map((section) => ({
                id: section.id ?? null,
                type: section.type,
                order: section.order,
                data: section.data,
                schemaVersion: section.schemaVersion,
                isActive: section.isActive,
            })),
        },
    )

    return response.data
}

export async function generateHomepageStarterTemplate(
    payload: GenerateHomepageTemplatePayload,
): Promise<HomepageSectionRecord[]> {
    const response = await superAdminApiClient.post<HomepageSectionRecord[], GenerateHomepageTemplatePayload>(
        '/homepage/sections/template',
        {
            tenantId: payload.tenantId ?? null,
            overwriteExisting: payload.overwriteExisting,
        },
    )

    return response.data
}

export async function getHomepageTenants(): Promise<TenantOption[]> {
    return superAdminApiClient.getRaw<TenantOption[]>('/api/tenants')
}

export async function getHomepagePreviewStoreConfig(tenantId: string): Promise<StoreConfig> {
    const response = await fetch('/api/storefront/store-config?preview=true', {
        method: 'GET',
        // The preview endpoint is public/read-only and tenant-scoped by header.
        // Sending browser cookies here can accidentally attach an unrelated tenant
        // session and trigger tenant-isolation 403s inside the Super Admin builder.
        credentials: 'omit',
        headers: {
            'Content-Type': 'application/json',
            'X-Tenant-Id': tenantId,
        },
    })

    if (!response.ok) {
        const errorPayload = await response
            .json()
            .catch(() => ({ message: 'Unable to load storefront configuration for preview.' }))

        const message =
            typeof errorPayload?.message === 'string'
                ? errorPayload.message
                : 'Unable to load storefront configuration for preview.'

        throw new Error(message)
    }

    return response.json() as Promise<StoreConfig>
}

export async function getHomepagePreviewProduct(id: string): Promise<HomepagePreviewProductDetail> {
    return superAdminApiClient.getRaw<HomepagePreviewProductDetail>(`/api/commercehub/products/${id}`)
}

export async function getHomepagePreviewCategories(): Promise<HomepagePreviewCategory[]> {
    return superAdminApiClient.getRaw<HomepagePreviewCategory[]>('/api/commercehub/categories/tree')
}

export async function translateMarketplaceContent<T>(content: T, from = 'en', to = 'ar'): Promise<T> {
    const response = await superAdminApiClient.post<{ content: T; from: string; to: string }, { content: T; from: string; to: string }>(
        '/marketplace/translate',
        {
            content,
            from,
            to,
        },
    )

    return response.data.content
}

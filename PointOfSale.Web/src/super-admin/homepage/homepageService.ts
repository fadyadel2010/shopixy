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
    workspaceVersion: string
    meta?: SuperAdminApiMeta & { workspaceVersion?: string }
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

function requireWorkspaceVersion(version: string | null | undefined): string {
    const normalized = version?.trim()
    if (!normalized) {
        throw new Error('The current Homepage workspace version is unavailable. Refresh and try again.')
    }

    return normalized
}

function buildMutationUrl(path: string, workspaceVersion: string | null | undefined): string {
    const params = new URLSearchParams({ expectedVersion: requireWorkspaceVersion(workspaceVersion) })
    return `${path}?${params.toString()}`
}

function admitHomepageResult(
    sections: HomepageSectionRecord[],
    meta?: SuperAdminApiMeta & { workspaceVersion?: string },
): HomepageSectionsResult {
    return {
        sections,
        workspaceVersion: requireWorkspaceVersion(meta?.workspaceVersion),
        meta,
    }
}

export async function getHomepageSections(
    tenantId?: string | null,
    mode: HomepageViewMode = 'preview',
): Promise<HomepageSectionsResult> {
    const response = await superAdminApiClient.get<HomepageSectionRecord[]>(buildSectionsUrl(tenantId, mode))

    return admitHomepageResult(response.data, response.meta)
}

export async function getHomepageSchemas(): Promise<HomepageSectionSchema[]> {
    const response = await superAdminApiClient.get<HomepageSectionSchema[]>('/homepage/sections/schemas')
    return response.data
}

export async function publishHomepageSections(
    tenantId: string | null | undefined,
    workspaceVersion: string | null | undefined,
): Promise<HomepageSectionsResult> {
    const response = await superAdminApiClient.post<HomepageSectionRecord[], { tenantId?: string | null }>(
        buildMutationUrl('/homepage/sections/publish', workspaceVersion),
        {
            tenantId: tenantId ?? null,
        },
    )

    return admitHomepageResult(response.data, response.meta)
}

export async function saveHomepageDraft(
    payload: SaveHomepageDraftPayload,
    workspaceVersion: string | null | undefined,
): Promise<HomepageSectionsResult> {
    const response = await superAdminApiClient.put<HomepageSectionRecord[], SaveHomepageDraftPayload>(
        buildMutationUrl('/homepage/sections/draft', workspaceVersion),
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

    return admitHomepageResult(response.data, response.meta)
}

export async function generateHomepageStarterTemplate(
    payload: GenerateHomepageTemplatePayload,
    workspaceVersion: string | null | undefined,
): Promise<HomepageSectionsResult> {
    const response = await superAdminApiClient.post<HomepageSectionRecord[], GenerateHomepageTemplatePayload>(
        buildMutationUrl('/homepage/sections/template', workspaceVersion),
        {
            tenantId: payload.tenantId ?? null,
            overwriteExisting: payload.overwriteExisting,
        },
    )

    return admitHomepageResult(response.data, response.meta)
}

export function isHomepageWorkspaceConflict(error: unknown): boolean {
    const candidate = error as { status?: number; code?: string } | null
    return candidate?.status === 409 && candidate.code === 'HOMEPAGE_WORKSPACE_CONFLICT'
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

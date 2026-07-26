import { superAdminApiClient } from '../core/superAdminApiClient'
import type { SuperAdminApiMeta } from '../auth/superAdminAuth.types'

export type CommissionScopeType = 'Global' | 'Category' | 'Vendor'

export interface CommissionRule {
    id: string
    scopeType: CommissionScopeType
    scopeId: string | null
    rate: number
    isActive: boolean
    priority: number
    createdAt: string
    updatedAt?: string | null
}

export interface CommissionRuleFormInput {
    scopeType: CommissionScopeType
    scopeId: string | null
    ratePercent: number
    priority: number
}

export interface CommissionRulesResult {
    rules: CommissionRule[]
    meta?: SuperAdminApiMeta
}

export interface CommissionCategoryOption {
    id: string
    name: string
    nameAr?: string | null
    slug: string
    parentId?: string | null
    level: number
    iconEmoji?: string | null
    hasAttributes: boolean
}

export interface CommissionVendorOption {
    id: string
    name: string
    slug?: string | null
    isActive: boolean
    contactEmail?: string | null
    createdAtUtc: string
}

export interface CommissionPreviewResult {
    vendorId?: string | null
    categoryId?: string | null
    rate: number
    source: string
    ruleId?: string | null
    priority?: number | null
    scopeType?: CommissionScopeType | null
    scopeId?: string | null
    usedLegacyFallback: boolean
    cacheStrategy?: string | null
}

interface CommissionRulePayload {
    scopeType: CommissionScopeType
    scopeId?: string | null
    rate: number
    priority: number
}

function toCommissionRulePayload(input: CommissionRuleFormInput): CommissionRulePayload {
    return {
        scopeType: input.scopeType,
        scopeId: input.scopeType === 'Global' ? null : input.scopeId,
        rate: input.ratePercent / 100,
        priority: input.priority,
    }
}

export async function getCommissionRules(): Promise<CommissionRulesResult> {
    const response = await superAdminApiClient.get<CommissionRule[]>('/commission')

    return {
        rules: response.data,
        meta: response.meta,
    }
}

export async function createCommissionRule(input: CommissionRuleFormInput): Promise<CommissionRule> {
    const response = await superAdminApiClient.post<CommissionRule, CommissionRulePayload>(
        '/commission',
        toCommissionRulePayload(input),
    )

    return response.data
}

export async function updateCommissionRule(
    id: string,
    input: CommissionRuleFormInput,
): Promise<CommissionRule> {
    const response = await superAdminApiClient.put<CommissionRule, CommissionRulePayload>(
        `/commission/${id}`,
        toCommissionRulePayload(input),
    )

    return response.data
}

export async function deactivateCommissionRule(id: string): Promise<CommissionRule> {
    const response = await superAdminApiClient.delete<CommissionRule>(`/commission/${id}`)
    return response.data
}

export async function resolveCommissionPreview(
    vendorId?: string | null,
    categoryId?: string | null,
): Promise<CommissionPreviewResult> {
    const params = new URLSearchParams()

    if (vendorId) {
        params.set('vendorId', vendorId)
    }

    if (categoryId) {
        params.set('categoryId', categoryId)
    }

    const query = params.toString()
    const response = await superAdminApiClient.get<CommissionPreviewResult>(
        `/commission/resolve-preview${query ? `?${query}` : ''}`,
    )

    return response.data
}

export async function getCommissionCategories(): Promise<CommissionCategoryOption[]> {
    return superAdminApiClient.getRaw<CommissionCategoryOption[]>('/api/commercehub/categories/tree')
}

export async function getCommissionVendors(): Promise<CommissionVendorOption[]> {
    return superAdminApiClient.getRaw<CommissionVendorOption[]>('/api/tenants')
}

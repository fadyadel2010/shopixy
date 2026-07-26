import { superAdminApiClient } from '../core/superAdminApiClient'
import type { SuperAdminApiMeta } from '../auth/superAdminAuth.types'

export type PromoDiscountType = 'Percentage' | 'Fixed'
export type PromoScopeType = 'Global' | 'Vendor' | 'Category'

export interface SuperAdminPromoCode {
    id: string
    code: string
    discountType: PromoDiscountType
    discountValue: number
    maxDiscountAmount?: number | null
    usageLimit?: number | null
    maxUsagePerCustomer?: number | null
    usedCount: number
    expiryDate?: string | null
    isActive: boolean
    vendorId?: string | null
    categoryId?: string | null
    createdAt: string
    updatedAt?: string | null
}

export interface PromoCodesResult {
    promoCodes: SuperAdminPromoCode[]
    meta?: SuperAdminApiMeta
}

export interface PromoCodeFormInput {
    code: string
    discountType: PromoDiscountType
    discountValue: number
    maxDiscountAmount?: number | null
    usageLimit?: number | null
    maxUsagePerCustomer?: number | null
    expiryDate?: string | null
    isActive: boolean
    scopeType: PromoScopeType
    scopeId?: string | null
}

export interface PromoCodePreviewInput {
    promoId?: string | null
    baseCommissionAmount: number
    previewVendorId?: string | null
    previewCategoryId?: string | null
}

export interface PromoCodePreviewResult {
    baseCommissionAmount: number
    discountAmount: number
    finalCommissionAmount: number
    isApplicable: boolean
    previewVendorId?: string | null
    previewCategoryId?: string | null
}

export interface PromoCategoryOption {
    id: string
    name: string
    nameAr?: string | null
    slug: string
    parentId?: string | null
    level: number
    iconEmoji?: string | null
    hasAttributes: boolean
}

export interface PromoVendorOption {
    id: string
    name: string
    slug?: string | null
    isActive: boolean
    contactEmail?: string | null
    createdAtUtc: string
}

interface PromoCodePayload {
    code: string
    discountType: PromoDiscountType
    discountValue: number
    maxDiscountAmount?: number | null
    usageLimit?: number | null
    maxUsagePerCustomer?: number | null
    expiryDate?: string | null
    isActive: boolean
    vendorId?: string | null
    categoryId?: string | null
}

interface PromoCodePreviewPayload {
    promoId?: string | null
    baseCommissionAmount: number
    previewVendorId?: string | null
    previewCategoryId?: string | null
}

function toPromoCodePayload(input: PromoCodeFormInput): PromoCodePayload {
    return {
        code: input.code.trim().toUpperCase(),
        discountType: input.discountType,
        discountValue: input.discountValue,
        maxDiscountAmount: input.maxDiscountAmount ?? null,
        usageLimit: input.usageLimit ?? null,
        maxUsagePerCustomer: input.maxUsagePerCustomer ?? null,
        expiryDate: input.expiryDate ?? null,
        isActive: input.isActive,
        vendorId: input.scopeType === 'Vendor' ? input.scopeId ?? null : null,
        categoryId: input.scopeType === 'Category' ? input.scopeId ?? null : null,
    }
}

export async function getPromoCodes(): Promise<PromoCodesResult> {
    const response = await superAdminApiClient.get<SuperAdminPromoCode[]>('/promo-codes')

    return {
        promoCodes: response.data,
        meta: response.meta,
    }
}

export async function createPromoCode(input: PromoCodeFormInput): Promise<SuperAdminPromoCode> {
    const response = await superAdminApiClient.post<SuperAdminPromoCode, PromoCodePayload>(
        '/promo-codes',
        toPromoCodePayload(input),
    )

    return response.data
}

export async function updatePromoCode(
    id: string,
    input: PromoCodeFormInput,
): Promise<SuperAdminPromoCode> {
    const response = await superAdminApiClient.put<SuperAdminPromoCode, PromoCodePayload>(
        `/promo-codes/${id}`,
        toPromoCodePayload(input),
    )

    return response.data
}

export async function activatePromoCode(id: string): Promise<SuperAdminPromoCode> {
    const response = await superAdminApiClient.post<SuperAdminPromoCode>(`/promo-codes/${id}/activate`)
    return response.data
}

export async function deactivatePromoCode(id: string): Promise<SuperAdminPromoCode> {
    const response = await superAdminApiClient.delete<SuperAdminPromoCode>(`/promo-codes/${id}`)
    return response.data
}

export async function previewPromoCode(
    input: PromoCodePreviewInput,
): Promise<PromoCodePreviewResult> {
    const payload: PromoCodePreviewPayload = {
        promoId: input.promoId ?? null,
        baseCommissionAmount: input.baseCommissionAmount,
        previewVendorId: input.previewVendorId ?? null,
        previewCategoryId: input.previewCategoryId ?? null,
    }

    const response = await superAdminApiClient.post<PromoCodePreviewResult, PromoCodePreviewPayload>(
        '/promo-codes/preview',
        payload,
    )

    return response.data
}

export async function getPromoCategories(): Promise<PromoCategoryOption[]> {
    return superAdminApiClient.getRaw<PromoCategoryOption[]>('/api/commercehub/categories/tree')
}

export async function getPromoVendors(): Promise<PromoVendorOption[]> {
    return superAdminApiClient.getRaw<PromoVendorOption[]>('/api/tenants')
}

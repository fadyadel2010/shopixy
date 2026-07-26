import { superAdminApiClient } from '../core/superAdminApiClient'
import type { SuperAdminApiMeta } from '../auth/superAdminAuth.types'

export type GovernanceRole = 'Customer' | 'Vendor' | 'Admin'
export type GovernanceStatus = 'Active' | 'Suspended' | 'Banned' | 'FinanciallyLocked'
export type GovernanceRiskLevel = 'Low' | 'Medium' | 'High'
export type GovernanceBlacklistType = 'PhoneNumber' | 'Email' | 'IpAddress' | 'DeviceId'

export interface GovernanceUserListItem {
    id: string
    role: GovernanceRole
    name: string
    email?: string | null
    phoneNumber?: string | null
    vendorSlug?: string | null
    status: GovernanceStatus
    riskLevel: GovernanceRiskLevel
    isKycVerified: boolean
    hasDebt: boolean
    suspiciousFlags: number
    vendorDebt: number
    createdAtUtc: string
    lastLoginAtUtc?: string | null
}

export interface GovernanceUserBasicInfo {
    name: string
    phoneNumber?: string | null
    email?: string | null
    vendorSlug?: string | null
}

export interface GovernanceFinancialSnapshot {
    vendorDebt: number
    vendorPayable: number
    totalRevenue: number
    totalRefunds: number
    currency: string
}

export interface GovernanceActivity {
    ordersCount: number
    refundCount: number
    returnCount: number
}

export interface GovernanceRisk {
    riskLevel: GovernanceRiskLevel
    refundRate: number
    disputeRate: number
    failedPayments: number
    suspiciousFlags: number
    signals: string[]
    alerts: string[]
    evaluatedAtUtc: string
}

export interface GovernanceAccessInfo {
    lastIp?: string | null
    deviceFingerprint?: string | null
}

export interface GovernanceAuditItem {
    id: string
    actionType: string
    reason: string
    adminId?: string | null
    actorType: string
    beforeState: string
    afterState: string
    timestampUtc: string
}

export interface GovernanceUserDetails {
    id: string
    role: GovernanceRole
    status: GovernanceStatus
    riskLevel: GovernanceRiskLevel
    isKycVerified: boolean
    createdAtUtc: string
    lastLoginAtUtc?: string | null
    statusReason?: string | null
    basicInfo: GovernanceUserBasicInfo
    financialSnapshot: GovernanceFinancialSnapshot
    activity: GovernanceActivity
    risk: GovernanceRisk
    accessInfo: GovernanceAccessInfo
    auditTimeline: GovernanceAuditItem[]
}

export interface GovernanceBlacklistEntry {
    id: string
    type: GovernanceBlacklistType
    valueDisplay: string
    reason: string
    isActive: boolean
    createdByAdminId?: string | null
    createdAtUtc: string
    revokedByAdminId?: string | null
    revokedAtUtc?: string | null
    revokedReason?: string | null
}

export interface GovernanceUsersFilters {
    page: number
    pageSize: number
    search?: string
    role?: GovernanceRole | ''
    status?: GovernanceStatus | ''
    riskLevel?: GovernanceRiskLevel | ''
    createdFromUtc?: string
    createdToUtc?: string
    hasDebt?: 'true' | 'false' | ''
}

export interface GovernanceUsersResult {
    users: GovernanceUserListItem[]
    meta?: SuperAdminApiMeta & {
        totalCount?: number
        totalPages?: number
    }
}

function buildUsersParams(filters: GovernanceUsersFilters): URLSearchParams {
    const params = new URLSearchParams()

    params.set('page', String(filters.page))
    params.set('pageSize', String(filters.pageSize))

    if (filters.search?.trim()) {
        params.set('search', filters.search.trim())
    }

    if (filters.role) {
        params.set('role', filters.role)
    }

    if (filters.status) {
        params.set('status', filters.status)
    }

    if (filters.riskLevel) {
        params.set('riskLevel', filters.riskLevel)
    }

    if (filters.createdFromUtc) {
        params.set('createdFromUtc', filters.createdFromUtc)
    }

    if (filters.createdToUtc) {
        params.set('createdToUtc', filters.createdToUtc)
    }

    if (filters.hasDebt) {
        params.set('hasDebt', filters.hasDebt)
    }

    return params
}

export async function getGovernedUsers(filters: GovernanceUsersFilters): Promise<GovernanceUsersResult> {
    const params = buildUsersParams(filters)
    const response = await superAdminApiClient.get<{ users: GovernanceUserListItem[] }>(
        `/users?${params.toString()}`,
    )

    return {
        users: response.data.users ?? [],
        meta: response.meta as GovernanceUsersResult['meta'],
    }
}

export async function getGovernedUserDetails(
    userId: string,
    role?: GovernanceRole,
): Promise<GovernanceUserDetails> {
    const params = new URLSearchParams()
    if (role) {
        params.set('role', role)
    }

    const response = await superAdminApiClient.get<GovernanceUserDetails>(
        `/users/${userId}${params.size ? `?${params.toString()}` : ''}`,
    )
    return response.data
}

export async function getGovernedUserRisk(
    userId: string,
    role?: GovernanceRole,
): Promise<GovernanceRisk> {
    const params = new URLSearchParams()
    if (role) {
        params.set('role', role)
    }

    const response = await superAdminApiClient.get<GovernanceRisk>(
        `/users/${userId}/risk${params.size ? `?${params.toString()}` : ''}`,
    )
    return response.data
}

export async function applyGovernanceAction(
    userId: string,
    role: GovernanceRole,
    action:
        | 'suspend'
        | 'ban'
        | 'activate'
        | 'financial-lock'
        | 'financial-unlock',
    reason: string,
): Promise<GovernanceUserDetails> {
    const response = await superAdminApiClient.post<GovernanceUserDetails, { reason: string }>(
        `/users/${userId}/${action}?role=${role}`,
        { reason },
    )

    return response.data
}

export async function getBlacklistEntries(): Promise<GovernanceBlacklistEntry[]> {
    const response = await superAdminApiClient.get<{ entries: GovernanceBlacklistEntry[] }>(
        '/users/blacklist',
    )

    return response.data.entries ?? []
}

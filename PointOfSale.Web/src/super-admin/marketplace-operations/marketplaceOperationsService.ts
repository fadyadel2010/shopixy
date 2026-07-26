import { superAdminApiClient } from '../core/superAdminApiClient'

export interface MarketplaceOperationsDashboard {
    sqlListings: number
    sqlPublishedListings: number
    sqlSuppressedListings: number
    sqlDeletedListings: number
    mongoProjectedListings: number
    mongoPublishedListings: number
    searchSyncPendingListings: number
    searchSyncFailedListings: number
    elasticsearchActiveDocumentCount?: number | null
    searchIndexStatus: {
        aliasName: string
        desiredIndexName: string
        activeIndexName?: string | null
        writeIndexName?: string | null
        desiredVersion: number
        activeVersion?: number | null
        requiresReindex: boolean
        mappingCompatible: boolean
        aliasExists: boolean
        legacyConcreteIndexExists: boolean
        missingFields: string[]
    }
    searchIndexVerification: {
        desiredIndexReady: boolean
        aliasPointsToDesiredIndex: boolean
        smokeTestPassed: boolean
        smokeTestHitCount?: number | null
        desiredDocumentCount?: number | null
        activeDocumentCount?: number | null
    }
    autoHealing: {
        queueDepth: number
        triggeredCount: number
        processedCount: number
        successCount: number
        failureCount: number
        droppedCount: number
        deduplicatedCount: number
        backgroundScanCount: number
        backgroundEnqueueCount: number
        circuitBreakerState: string
        circuitBreakerOpenUntilUtc?: string | null
        lastBackgroundReconciledAtUtc?: string | null
    }
    authorityOwner: string
    operationsAuthority: string
    generatedAtUtc: string
}

export interface MarketplaceOperationsReindexResult {
    scope: string
    tenantId?: string | null
    result: {
        processed: number
        succeeded: number
        failed: number
        staleDeleted: number
        activeIndexName?: string | null
        targetIndexName?: string | null
        promoted: boolean
        expectedDocumentCount?: number | null
        verifiedDocumentCount?: number | null
    }
    authorityOwner: string
    completedAtUtc: string
}

export interface MarketplaceOperationsVerificationResult {
    scope: string
    tenantId?: string | null
    listingId?: string | null
    enqueued: number
    reason: string
    enqueuedAtUtc: string
}

export interface MarketplaceOperationsCatchUpResult {
    result: {
        processed: number
        succeeded: number
        failed: number
        startedAtUtc: string
        completedAtUtc: string
    }
    authorityOwner: string
    completedAtUtc: string
}

export async function getMarketplaceOperationsDashboard(): Promise<MarketplaceOperationsDashboard> {
    const response = await superAdminApiClient.get<MarketplaceOperationsDashboard>('/marketplace-operations/dashboard')
    return response.data
}

export async function runGlobalMarketplaceReindex(reason: string): Promise<MarketplaceOperationsReindexResult> {
    const response = await superAdminApiClient.post<MarketplaceOperationsReindexResult, { reason: string }>(
        '/marketplace-operations/reindex/global',
        { reason },
    )
    return response.data
}

export async function runTenantMarketplaceReindex(
    tenantId: string,
    reason: string,
): Promise<MarketplaceOperationsReindexResult> {
    const response = await superAdminApiClient.post<MarketplaceOperationsReindexResult, { reason: string }>(
        `/marketplace-operations/reindex/tenants/${tenantId}`,
        { reason },
    )
    return response.data
}

export async function runMarketplaceCatchUpReindex(reason: string): Promise<MarketplaceOperationsCatchUpResult> {
    const response = await superAdminApiClient.post<MarketplaceOperationsCatchUpResult, { reason: string }>(
        '/marketplace-operations/reindex/catch-up',
        { reason },
    )
    return response.data
}

export async function verifyMarketplaceListing(
    listingId: string,
    reason: string,
): Promise<MarketplaceOperationsVerificationResult> {
    const response = await superAdminApiClient.post<MarketplaceOperationsVerificationResult, { reason: string; highPriority: boolean }>(
        `/marketplace-operations/listings/${listingId}/verify`,
        { reason, highPriority: true },
    )
    return response.data
}

export async function verifyMarketplaceTenant(
    tenantId: string,
    reason: string,
): Promise<MarketplaceOperationsVerificationResult> {
    const response = await superAdminApiClient.post<MarketplaceOperationsVerificationResult, { reason: string; highPriority: boolean }>(
        `/marketplace-operations/tenants/${tenantId}/verify`,
        { reason, highPriority: true },
    )
    return response.data
}

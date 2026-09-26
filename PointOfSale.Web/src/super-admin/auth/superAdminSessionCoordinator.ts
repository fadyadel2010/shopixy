import { runWithCrossTabRefreshLock } from '../../auth/crossTabRefreshLock'
import {
    getSuperAdminAuthorityRevision,
    getStoredSuperAdminTokenVersion,
} from './superAdminAuthStore'
import {
    getSuperAdminSession,
    refreshSuperAdminSession,
    SuperAdminAuthError,
} from './superAdminAuthService'
import { canRefreshSuperAdminSession } from './superAdminSession'
import {
    requireSuperAdminSessionAuthority,
    type SuperAdminSessionResponse,
} from './superAdminAuth.types'

let refreshPromise: Promise<SuperAdminSessionResponse> | null = null

export function isDefinitiveSuperAdminAuthFailure(error: unknown): boolean {
    const status = (error as { status?: number })?.status
    return status === 401 || status === 403
}

async function readValidatedSuperAdminSession(): Promise<SuperAdminSessionResponse> {
    const session = await getSuperAdminSession()

    try {
        return requireSuperAdminSessionAuthority(session)
    } catch {
        throw new SuperAdminAuthError('Super Admin session authority payload was invalid.', { status: 401 })
    }
}

export async function restoreSuperAdminSession(): Promise<SuperAdminSessionResponse> {
    try {
        return await readValidatedSuperAdminSession()
    } catch (error) {
        if ((error as { status?: number })?.status !== 401 || !canRefreshSuperAdminSession()) {
            throw error
        }
    }

    if (!refreshPromise) {
        refreshPromise = runWithCrossTabRefreshLock('super-admin-session-refresh', async () => {
            try {
                return await readValidatedSuperAdminSession()
            } catch (error) {
                if ((error as { status?: number })?.status !== 401) {
                    throw error
                }
            }

            const tokenVersionResult = getStoredSuperAdminTokenVersion()
            if (tokenVersionResult !== undefined && typeof tokenVersionResult === 'object') {
                throw new SuperAdminAuthError('Super Admin token version authority is unavailable.', { status: 401 })
            }
            const tokenVersion = tokenVersionResult

            const authorityRevisionAtStart = getSuperAdminAuthorityRevision()
            await refreshSuperAdminSession({ tokenVersion })
            const session = await readValidatedSuperAdminSession()

            if (authorityRevisionAtStart !== getSuperAdminAuthorityRevision()) {
                throw new SuperAdminAuthError('Super Admin actor changed during refresh.', { status: 401 })
            }

            return session
        }).finally(() => {
            refreshPromise = null
        })
    }

    return refreshPromise
}

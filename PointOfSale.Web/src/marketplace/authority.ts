import { useAuthStore } from '../store/authStore'

const normalizeIdentityPart = (value: unknown) => {
  const normalized = String(value ?? '').trim()
  return normalized || null
}

export const isServiceMarketplaceWorkflowActor = (
  user: { tenantId?: unknown; id?: unknown; userId?: unknown; role?: unknown } | null | undefined,
  isAuthenticated = true,
) => {
  const tenantId = normalizeIdentityPart(user?.tenantId)
  const userId = normalizeIdentityPart(user?.id ?? user?.userId)
  const role = String(user?.role ?? '').trim().toLowerCase()

  return isAuthenticated
    && tenantId !== null
    && userId !== null
    && role !== 'superadmin'
}

const getScopeForState = (state: ReturnType<typeof useAuthStore.getState>) => {
  if (!isServiceMarketplaceWorkflowActor(state.user, state.isAuthenticated)) {
    return 'ineligible'
  }

  return [
    normalizeIdentityPart(state.user?.tenantId),
    normalizeIdentityPart(state.user?.id ?? state.user?.userId),
  ].join(':')
}

export const getMarketplaceAuthorityScope = () => getScopeForState(useAuthStore.getState())

export const useMarketplaceAuthorityScope = () => useAuthStore(getScopeForState)

export const useIsServiceMarketplaceWorkflowActor = () => useAuthStore((state) =>
  isServiceMarketplaceWorkflowActor(state.user, state.isAuthenticated))

export const marketplaceAuthorityQueryOptions = {
  staleTime: 0,
  retry: false,
  refetchOnMount: 'always' as const,
  refetchOnReconnect: 'always' as const,
  refetchOnWindowFocus: 'always' as const,
}

export const isMarketplaceAuthorityError = (error: unknown) => {
  const status = Number((error as { status?: number })?.status ?? 0)
  return status === 401 || status === 403 || status === 404
}

export const marketplaceAuthorityChannelName = 'marketplace-authority-invalidation'

export const isMarketplaceAuthorityInvalidation = (
  payload: unknown,
  authorityScope: string,
) => {
  if (!payload || typeof payload !== 'object' || authorityScope === 'ineligible') {
    return false
  }

  const event = payload as Record<string, unknown>
  return event.type === 'invalidate'
    && event.authorityScope === authorityScope
    && (event.serviceId === undefined || typeof event.serviceId === 'string')
}

export const publishMarketplaceAuthorityInvalidation = (
  authorityScope: string,
  serviceId?: string,
) => {
  if (
    typeof BroadcastChannel === 'undefined'
    || authorityScope === 'ineligible'
    || authorityScope !== getMarketplaceAuthorityScope()
  ) {
    return
  }

  try {
    const channel = new BroadcastChannel(marketplaceAuthorityChannelName)
    channel.postMessage({ type: 'invalidate', authorityScope, serviceId })
    channel.close()
  } catch {
    // Cross-tab convergence is best-effort; current backend queries remain authoritative.
  }
}

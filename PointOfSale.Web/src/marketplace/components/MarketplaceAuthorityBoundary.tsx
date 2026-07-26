import { useLayoutEffect, useRef } from 'react'
import type { ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Navigate } from 'react-router-dom'
import { marketplaceKeys } from '../queryKeys'
import {
  isMarketplaceAuthorityInvalidation,
  marketplaceAuthorityChannelName,
  useIsServiceMarketplaceWorkflowActor,
  useMarketplaceAuthorityScope,
} from '../authority'

const AUTHORITY_REVALIDATION_INTERVAL_MS = 30_000

export function MarketplaceAuthorityBoundary({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const scope = useMarketplaceAuthorityScope()
  const isWorkflowActor = useIsServiceMarketplaceWorkflowActor()
  const previousScopeRef = useRef(scope)

  useLayoutEffect(() => {
    queryClient.removeQueries({
      predicate: (query) =>
        query.queryKey[0] === marketplaceKeys.all[0]
        && query.queryKey[1] !== scope,
    })

    if (previousScopeRef.current !== scope) {
      queryClient.removeQueries({ queryKey: marketplaceKeys.scope(previousScopeRef.current) })
      previousScopeRef.current = scope
    }

    const revalidate = () => {
      if (document.visibilityState !== 'hidden') {
        void queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(scope) })
      }
    }

    const intervalId = window.setInterval(revalidate, AUTHORITY_REVALIDATION_INTERVAL_MS)
    let authorityChannel: BroadcastChannel | null = null
    try {
      authorityChannel = typeof BroadcastChannel === 'undefined'
        ? null
        : new BroadcastChannel(marketplaceAuthorityChannelName)
    } catch {
      authorityChannel = null
    }
    if (authorityChannel) {
      authorityChannel.onmessage = (event) => {
        if (isMarketplaceAuthorityInvalidation(event.data, scope)) {
          revalidate()
        }
      }
    }
    window.addEventListener('focus', revalidate)
    document.addEventListener('visibilitychange', revalidate)

    return () => {
      window.clearInterval(intervalId)
      authorityChannel?.close()
      window.removeEventListener('focus', revalidate)
      document.removeEventListener('visibilitychange', revalidate)
      queryClient.removeQueries({ queryKey: marketplaceKeys.scope(scope) })
    }
  }, [queryClient, scope])

  if (!isWorkflowActor) {
    return <Navigate to="/" replace />
  }

  return <div key={scope}>{children}</div>
}

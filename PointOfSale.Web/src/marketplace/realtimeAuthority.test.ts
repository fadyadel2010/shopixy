import { describe, expect, it } from 'vitest'
import { isMarketplaceAuthorityInvalidation } from './authority'
import { isCurrentChatRealtimeEvent } from './chatAuthority'
import type { ChatIdentityAuthority } from './chatAuthority'

const authorityScope = '9dfac6e3-957e-41e9-b75b-486e3f840ccd:13ed2b6d-c48f-4794-a210-0c7ab703e52f'
const requestId = '3be1b510-0f53-4d37-907e-eabdc7fe78cb'

const identity: ChatIdentityAuthority = {
  requestId,
  serviceId: 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
  buyerUserId: '13ed2b6d-c48f-4794-a210-0c7ab703e52f',
  providerUserId: 'f9c54195-8e39-4b95-a1be-3050f5f1175e',
  currentUserId: '13ed2b6d-c48f-4794-a210-0c7ab703e52f',
  currentUserRole: 'Buyer',
  canViewChatHistory: true,
  canSendChat: true,
}

describe('service marketplace frontend realtime authority', () => {
  it('accepts cross-tab invalidation only for the current actor scope', () => {
    expect(isMarketplaceAuthorityInvalidation({
      type: 'invalidate',
      authorityScope,
    }, authorityScope)).toBe(true)

    expect(isMarketplaceAuthorityInvalidation({
      type: 'invalidate',
      authorityScope: 'foreign-tenant:foreign-user',
    }, authorityScope)).toBe(false)
    expect(isMarketplaceAuthorityInvalidation({
      type: 'message',
      authorityScope,
    }, authorityScope)).toBe(false)
    expect(isMarketplaceAuthorityInvalidation(null, authorityScope)).toBe(false)
    expect(isMarketplaceAuthorityInvalidation({
      type: 'invalidate',
      authorityScope: 'ineligible',
    }, 'ineligible')).toBe(false)
  })

  it('accepts SignalR events only as current-request invalidation evidence', () => {
    const event = {
      id: 'c565ad83-7255-4e0a-b04a-d0fed589ddc3',
      serviceRequestId: requestId,
    }

    expect(isCurrentChatRealtimeEvent(event, identity)).toBe(true)
    expect(isCurrentChatRealtimeEvent({
      ...event,
      serviceRequestId: identity.serviceId,
    }, identity)).toBe(false)
    expect(isCurrentChatRealtimeEvent({
      serviceRequestId: requestId,
    }, identity)).toBe(false)
  })
})

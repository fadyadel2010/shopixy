import { describe, expect, it } from 'vitest'
import { deriveServiceRequestCompletionEligibility } from './completionEligibility'
import { deriveServiceRequestLifecyclePresentation } from './status'
import type { ServiceRequest } from './types'

const buyerUserId = '13ed2b6d-c48f-4794-a210-0c7ab703e52f'
const providerUserId = 'f9c54195-8e39-4b95-a1be-3050f5f1175e'

const request = (overrides: Partial<ServiceRequest> = {}): ServiceRequest => ({
  requestId: '3be1b510-0f53-4d37-907e-eabdc7fe78cb',
  serviceId: 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
  buyerUserId,
  providerUserId,
  status: 'Accepted',
  createdAt: '2026-06-14T00:00:00Z',
  canChat: true,
  canViewChatHistory: true,
  canSendChat: true,
  canConfirmCompletion: false,
  canCancel: true,
  canReview: false,
  canAccept: false,
  canReject: false,
  canMarkCompleted: true,
  ...overrides,
})

const eligibility = (
  value: ServiceRequest,
  currentUserId: string,
  expectedActorRole?: 'buyer' | 'provider',
) => deriveServiceRequestCompletionEligibility(
  value,
  deriveServiceRequestLifecyclePresentation(value),
  currentUserId,
  expectedActorRole,
)

describe('completion eligibility authority', () => {
  it('grants provider presentation only for current provider identity in accepted state', () => {
    expect(eligibility(request(), providerUserId, 'provider')).toMatchObject({
      actorRole: 'provider',
      canMarkCompleted: true,
      canConfirmCompletion: false,
    })

    expect(eligibility(request(), buyerUserId, 'provider')).toBeNull()
    expect(eligibility(request({ status: 'Cancelled' }), providerUserId, 'provider')?.canMarkCompleted).toBe(false)
  })

  it('grants buyer confirmation only after provider completion', () => {
    const completedByProvider = request({
      status: 'CompletedByProvider',
      canMarkCompleted: false,
      canConfirmCompletion: true,
      canCancel: false,
    })

    expect(eligibility(completedByProvider, buyerUserId, 'buyer')).toMatchObject({
      actorRole: 'buyer',
      canMarkCompleted: false,
      canConfirmCompletion: true,
    })

    expect(eligibility(completedByProvider, providerUserId, 'buyer')).toBeNull()
    expect(eligibility(request({
      canMarkCompleted: false,
      canConfirmCompletion: true,
    }), buyerUserId, 'buyer')?.canConfirmCompletion).toBe(false)
  })

  it('fails closed for malformed identity, foreign actors, and contradictory capabilities', () => {
    expect(eligibility(request({ buyerUserId: undefined }), buyerUserId)).toBeNull()
    expect(eligibility(request(), 'cfb92572-63f5-4fae-b1ef-b4fdb3204794')).toBeNull()
    expect(eligibility(request({
      canMarkCompleted: true,
      canConfirmCompletion: true,
    }), providerUserId)).toBeNull()
    expect(eligibility(request({
      canMarkCompleted: true,
    }), buyerUserId)).toBeNull()
  })
})

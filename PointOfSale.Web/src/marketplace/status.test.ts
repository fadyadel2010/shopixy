import { describe, expect, it } from 'vitest'
import {
  deriveServiceRequestDetailsLifecyclePresentation,
  deriveServiceRequestLifecyclePresentation,
  deriveServiceRequestLifecycleMutationAuthority,
  normalizeServiceRequestStatusKey,
} from './status'
import type { ServiceRequest, ServiceRequestDetails } from './types'

const request = (overrides: Partial<ServiceRequest> = {}): ServiceRequest => ({
  requestId: '3be1b510-0f53-4d37-907e-eabdc7fe78cb',
  serviceId: 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
  status: 'Pending',
  createdAt: '2026-06-14T00:00:00Z',
  canChat: false,
  canViewChatHistory: false,
  canSendChat: false,
  canConfirmCompletion: false,
  canCancel: false,
  canReview: false,
  canAccept: false,
  canReject: false,
  canMarkCompleted: false,
  ...overrides,
})

const requestDetails = (overrides: Partial<ServiceRequestDetails> = {}): ServiceRequestDetails => ({
  requestId: '3be1b510-0f53-4d37-907e-eabdc7fe78cb',
  serviceId: 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
  buyerUserId: '13ed2b6d-c48f-4794-a210-0c7ab703e52f',
  providerUserId: 'f9c54195-8e39-4b95-a1be-3050f5f1175e',
  status: 'Accepted',
  createdAt: '2026-06-14T00:00:00Z',
  capabilities: {
    canAccept: false,
    canReject: false,
    canMarkCompleted: true,
    canConfirmCompletion: false,
    canCancel: true,
    canChat: true,
    canViewChatHistory: true,
    canSendChat: true,
    canReview: false,
  },
  ...overrides,
})

describe('request lifecycle presentation authority', () => {
  it('allowlists backend lifecycle statuses and rejects invalid status presentation', () => {
    expect(normalizeServiceRequestStatusKey('Pending')).toBe('pending')
    expect(normalizeServiceRequestStatusKey('CompletedByProvider')).toBe('completedByProvider')
    expect(normalizeServiceRequestStatusKey('Completed_By_Buyer')).toBeNull()
    expect(normalizeServiceRequestStatusKey('Rejected')).toBe('rejected')
    expect(normalizeServiceRequestStatusKey('Expired')).toBeNull()
    expect(normalizeServiceRequestStatusKey('completedByBuyer')).toBeNull()
    expect(normalizeServiceRequestStatusKey('Fabricated')).toBeNull()
    expect(deriveServiceRequestLifecyclePresentation(request({ status: 'Fabricated' }))).toBeNull()
  })

  it('suppresses action capabilities that contradict current backend lifecycle status', () => {
    const pending = deriveServiceRequestLifecyclePresentation(request({
      status: 'Pending',
      canAccept: true,
      canReject: true,
      canMarkCompleted: true,
      canConfirmCompletion: true,
      canCancel: true,
      canChat: true,
      canViewChatHistory: true,
      canSendChat: true,
      canReview: true,
    }))

    expect(pending).toMatchObject({
      statusKey: 'pending',
      canAccept: true,
      canReject: true,
      canCancel: true,
      canMarkCompleted: false,
      canConfirmCompletion: false,
      canChat: false,
      canViewChatHistory: false,
      canReview: false,
    })

    const terminal = deriveServiceRequestLifecyclePresentation(request({
      status: 'Rejected',
      canAccept: true,
      canReject: true,
      canCancel: true,
      canMarkCompleted: true,
      canConfirmCompletion: true,
      canChat: true,
      canSendChat: true,
      canReview: true,
      canViewChatHistory: true,
    }))

    expect(terminal).toMatchObject({
      statusKey: 'rejected',
      canAccept: false,
      canReject: false,
      canCancel: false,
      canMarkCompleted: false,
      canConfirmCompletion: false,
      canChat: false,
      canViewChatHistory: true,
      canReview: false,
    })
  })

  it('requires explicit backend chat history and send capabilities', () => {
    const missingExplicitChatCapabilities = deriveServiceRequestLifecyclePresentation(request({
      status: 'Accepted',
      canChat: true,
      canViewChatHistory: undefined,
      canSendChat: undefined,
    }))

    expect(missingExplicitChatCapabilities).toMatchObject({
      canChat: false,
      canViewChatHistory: false,
      canSendChat: false,
    })
  })

  it('derives detail capabilities from status-bound backend capability flags only', () => {
    const accepted = deriveServiceRequestDetailsLifecyclePresentation(requestDetails())
    expect(accepted).toMatchObject({
      statusKey: 'accepted',
      canMarkCompleted: true,
      canCancel: true,
      canChat: true,
      canViewChatHistory: true,
    })

    const completed = deriveServiceRequestDetailsLifecyclePresentation(requestDetails({
      status: 'CompletedByBuyer',
      capabilities: {
        canAccept: true,
        canReject: true,
        canMarkCompleted: true,
        canConfirmCompletion: true,
        canCancel: true,
        canChat: true,
        canViewChatHistory: true,
        canSendChat: true,
        canReview: true,
      },
    }))

    expect(completed).toMatchObject({
      statusKey: 'completedByBuyer',
      canAccept: false,
      canReject: false,
      canMarkCompleted: false,
      canConfirmCompletion: false,
      canCancel: false,
      canChat: false,
      canViewChatHistory: true,
      canReview: true,
    })
  })

  it('accepts lifecycle mutation completion only for the expected backend state-machine target', () => {
    expect(deriveServiceRequestLifecycleMutationAuthority({ status: 'Accepted' }, 'accept')).toEqual({ statusKey: 'accepted' })
    expect(deriveServiceRequestLifecycleMutationAuthority({ status: 'Rejected' }, 'reject')).toEqual({ statusKey: 'rejected' })
    expect(deriveServiceRequestLifecycleMutationAuthority({ status: 'Cancelled' }, 'cancel')).toEqual({ statusKey: 'cancelled' })
    expect(deriveServiceRequestLifecycleMutationAuthority({ status: 'CompletedByProvider' }, 'markCompleted')).toEqual({ statusKey: 'completedByProvider' })
    expect(deriveServiceRequestLifecycleMutationAuthority({ status: 'CompletedByBuyer' }, 'confirmCompletion')).toEqual({ statusKey: 'completedByBuyer' })

    expect(deriveServiceRequestLifecycleMutationAuthority({ status: 'Pending' }, 'accept')).toBeNull()
    expect(deriveServiceRequestLifecycleMutationAuthority({ status: 'Accepted' }, 'reject')).toBeNull()
    expect(deriveServiceRequestLifecycleMutationAuthority({ status: 'Fabricated' }, 'confirmCompletion')).toBeNull()
    expect(deriveServiceRequestLifecycleMutationAuthority({}, 'cancel')).toBeNull()
  })
})

import { describe, expect, it } from 'vitest'
import { deriveServiceRequestCompletionStatePresentation } from './completionStateAuthority'
import { deriveServiceRequestLifecyclePresentation } from './status'
import type { ServiceRequest } from './types'

const requestId = '3be1b510-0f53-4d37-907e-eabdc7fe78cb'
const serviceId = 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0'
const buyerUserId = '13ed2b6d-c48f-4794-a210-0c7ab703e52f'
const providerUserId = 'f9c54195-8e39-4b95-a1be-3050f5f1175e'

const request = (overrides: Partial<ServiceRequest> = {}): ServiceRequest => ({
  requestId,
  serviceId,
  buyerUserId,
  providerUserId,
  status: 'Accepted',
  createdAt: '2026-06-14T00:00:00Z',
  completionEvidence: {
    requestId,
    status: 'Accepted',
    providerCompletion: null,
    buyerConfirmation: null,
    notes: [],
    attachments: [],
    deliverables: [],
  },
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

const derive = (value: ServiceRequest, actorUserId = providerUserId) => {
  const lifecycle = deriveServiceRequestLifecyclePresentation(value)
  return deriveServiceRequestCompletionStatePresentation(value, lifecycle, actorUserId)
}

describe('completion state authority', () => {
  it('accepts non-completed lifecycle only when backend completion evidence is empty and matching', () => {
    expect(derive(request())).toMatchObject({
      lifecycle: { statusKey: 'accepted' },
      completionEvidence: null,
    })

    expect(derive(request({ completionEvidence: undefined }))).toBeNull()
    expect(derive(request({
      completionEvidence: {
        ...request().completionEvidence!,
        status: 'CompletedByProvider',
      },
    }))).toBeNull()
  })

  it('accepts provider completion only with current backend provider evidence', () => {
    const completed = request({
      status: 'CompletedByProvider',
      canMarkCompleted: false,
      completionEvidence: {
        requestId,
        status: 'CompletedByProvider',
        providerCompletion: {
          evidenceId: `${requestId}:ProviderCompletion:1`,
          requestId,
          evidenceType: 'ProviderCompletion',
          occurredAt: '2026-06-14T01:00:00Z',
          actorRole: 'Provider',
          actorUserId: providerUserId,
        },
        buyerConfirmation: null,
        notes: [],
        attachments: [],
        deliverables: [],
      },
    })

    expect(derive(completed)?.completionEvidence).toMatchObject({
      status: 'completedByProvider',
    })
    expect(derive({
      ...completed,
      completionEvidence: {
        ...completed.completionEvidence!,
        providerCompletion: null,
      },
    })).toBeNull()
  })

  it('rejects fabricated aliases, foreign actors, and completion evidence in terminal non-completed states', () => {
    expect(derive(request({ status: 'Completed_By_Provider' }))).toBeNull()
    expect(derive(request(), 'cfb92572-63f5-4fae-b1ef-b4fdb3204794')).toBeNull()
    expect(derive(request({
      status: 'Cancelled',
      canMarkCompleted: false,
      canCancel: false,
      completionEvidence: {
        ...request().completionEvidence!,
        status: 'Cancelled',
        providerCompletion: {
          evidenceId: `${requestId}:ProviderCompletion:1`,
          requestId,
          evidenceType: 'ProviderCompletion',
          occurredAt: '2026-06-14T01:00:00Z',
          actorRole: 'Provider',
          actorUserId: providerUserId,
        },
      },
    }))).toBeNull()
  })
})

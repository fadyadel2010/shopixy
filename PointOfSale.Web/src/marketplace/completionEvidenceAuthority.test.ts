import { describe, expect, it } from 'vitest'
import { deriveCompletionEvidencePresentation } from './completionEvidenceAuthority'
import type { ServiceRequestDetails } from './types'
import type { ServiceRequestLifecyclePresentation } from './status'

const requestId = '3be1b510-0f53-4d37-907e-eabdc7fe78cb'
const buyerUserId = '13ed2b6d-c48f-4794-a210-0c7ab703e52f'
const providerUserId = 'f9c54195-8e39-4b95-a1be-3050f5f1175e'

const lifecycle = (
  statusKey: 'completedByProvider' | 'completedByBuyer',
): ServiceRequestLifecyclePresentation => ({
  statusKey,
  statusLabelFallback: statusKey,
  statusClass: '',
  canAccept: false,
  canReject: false,
  canMarkCompleted: false,
  canConfirmCompletion: statusKey === 'completedByProvider',
  canCancel: false,
  canReview: statusKey === 'completedByBuyer',
  canViewChatHistory: true,
  canSendChat: statusKey === 'completedByProvider',
  canChat: statusKey === 'completedByProvider',
})

const details = (
  status: 'CompletedByProvider' | 'CompletedByBuyer' = 'CompletedByBuyer',
): ServiceRequestDetails => ({
  requestId,
  serviceId: 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
  buyerUserId,
  providerUserId,
  status,
  createdAt: '2026-06-14T00:00:00Z',
  completionEvidence: {
    requestId,
    status,
    providerCompletion: {
      evidenceId: `${requestId}:ProviderCompletion:1`,
      requestId,
      evidenceType: 'ProviderCompletion',
      occurredAt: '2026-06-14T01:00:00Z',
      actorRole: 'Provider',
      actorUserId: providerUserId,
    },
    buyerConfirmation: status === 'CompletedByBuyer'
      ? {
          evidenceId: `${requestId}:BuyerConfirmation:2`,
          requestId,
          evidenceType: 'BuyerConfirmation',
          occurredAt: '2026-06-14T02:00:00Z',
          actorRole: 'Buyer',
          actorUserId: buyerUserId,
        }
      : null,
    notes: [],
    attachments: [],
    deliverables: [],
  },
  capabilities: {
    canAccept: false,
    canReject: false,
    canMarkCompleted: false,
    canConfirmCompletion: status === 'CompletedByProvider',
    canCancel: false,
    canChat: status === 'CompletedByProvider',
    canViewChatHistory: true,
    canSendChat: status === 'CompletedByProvider',
    canReview: status === 'CompletedByBuyer',
  },
})

describe('completion evidence authority', () => {
  it('accepts backend completion phases only for a current request participant', () => {
    expect(deriveCompletionEvidencePresentation(
      details(),
      lifecycle('completedByBuyer'),
      buyerUserId,
    )).toMatchObject({
      requestId,
      status: 'completedByBuyer',
      notes: [],
      attachments: [],
      deliverables: [],
    })

    expect(deriveCompletionEvidencePresentation(
      details('CompletedByProvider'),
      lifecycle('completedByProvider'),
      providerUserId,
    )?.buyerConfirmation).toBeNull()
  })

  it('rejects request, actor, lifecycle, phase ownership, and chronology replay', () => {
    const payload = details()

    expect(deriveCompletionEvidencePresentation(
      { ...payload, requestId: buyerUserId },
      lifecycle('completedByBuyer'),
      buyerUserId,
    )).toBeNull()
    expect(deriveCompletionEvidencePresentation(
      payload,
      lifecycle('completedByBuyer'),
      'a9b35a70-0545-4477-a16a-b91f03a874ff',
    )).toBeNull()
    expect(deriveCompletionEvidencePresentation(
      payload,
      lifecycle('completedByProvider'),
      buyerUserId,
    )).toBeNull()
    expect(deriveCompletionEvidencePresentation(
      {
        ...payload,
        completionEvidence: {
          ...payload.completionEvidence!,
          providerCompletion: {
            ...payload.completionEvidence!.providerCompletion!,
            actorUserId: buyerUserId,
          },
        },
      },
      lifecycle('completedByBuyer'),
      buyerUserId,
    )).toBeNull()
    expect(deriveCompletionEvidencePresentation(
      {
        ...payload,
        completionEvidence: {
          ...payload.completionEvidence!,
          buyerConfirmation: {
            ...payload.completionEvidence!.buyerConfirmation!,
            occurredAt: '2026-06-14T00:30:00Z',
          },
        },
      },
      lifecycle('completedByBuyer'),
      buyerUserId,
    )).toBeNull()
  })

  it('fails closed when unsupported notes, attachments, or deliverables are injected', () => {
    const payload = details()

    for (const field of ['notes', 'attachments', 'deliverables'] as const) {
      expect(deriveCompletionEvidencePresentation(
        {
          ...payload,
          completionEvidence: {
            ...payload.completionEvidence!,
            [field]: [{ fabricated: true }],
          },
        } as ServiceRequestDetails,
        lifecycle('completedByBuyer'),
        buyerUserId,
      )).toBeNull()
    }
  })

  it('rejects malformed evidence metadata and missing required phases', () => {
    const payload = details()

    expect(deriveCompletionEvidencePresentation(
      {
        ...payload,
        completionEvidence: {
          ...payload.completionEvidence!,
          providerCompletion: null,
        },
      },
      lifecycle('completedByBuyer'),
      buyerUserId,
    )).toBeNull()
    expect(deriveCompletionEvidencePresentation(
      {
        ...payload,
        completionEvidence: {
          ...payload.completionEvidence!,
          buyerConfirmation: {
            ...payload.completionEvidence!.buyerConfirmation!,
            occurredAt: 'not-a-date',
          },
        },
      },
      lifecycle('completedByBuyer'),
      buyerUserId,
    )).toBeNull()
  })
})

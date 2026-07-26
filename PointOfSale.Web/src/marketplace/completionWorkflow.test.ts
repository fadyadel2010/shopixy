import { describe, expect, it, vi } from 'vitest'
import {
  deriveServiceRequestCompletionOutcome,
  executeServiceRequestCompletionWorkflow,
} from './completionWorkflow'
import type { ServiceRequestDetails } from './types'

const requestId = '3be1b510-0f53-4d37-907e-eabdc7fe78cb'
const buyerUserId = '13ed2b6d-c48f-4794-a210-0c7ab703e52f'
const providerUserId = 'f9c54195-8e39-4b95-a1be-3050f5f1175e'
const authorityScope = `9dfac6e3-957e-41e9-b75b-486e3f840ccd:${providerUserId}`

const details = (overrides: Partial<ServiceRequestDetails> = {}): ServiceRequestDetails => ({
  requestId,
  serviceId: 'a2b9e228-a6e6-4a5e-8d43-e6d1504942c0',
  buyerUserId,
  providerUserId,
  status: 'CompletedByProvider',
  createdAt: '2026-06-14T00:00:00Z',
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
  capabilities: {
    canAccept: false,
    canReject: false,
    canMarkCompleted: false,
    canConfirmCompletion: false,
    canCancel: false,
    canChat: true,
    canViewChatHistory: true,
    canSendChat: true,
    canReview: false,
  },
  ...overrides,
})

describe('completion workflow authority', () => {
  it('accepts workflow success only after current backend authority proves the target', async () => {
    const mutate = vi.fn().mockResolvedValue({ status: 'CompletedByProvider' })
    const revalidate = vi.fn().mockResolvedValue(details())

    await expect(executeServiceRequestCompletionWorkflow({
      requestId,
      authorityScope,
      actorUserId: providerUserId,
      kind: 'markCompleted',
      mutate,
      revalidate,
      getCurrentAuthorityScope: () => authorityScope,
    })).resolves.toMatchObject({ status: 'CompletedByProvider' })

    expect(mutate).toHaveBeenCalledTimes(1)
    expect(revalidate).toHaveBeenCalledTimes(1)
  })

  it('resolves timeout-after-commit only when backend revalidation proves completion', async () => {
    await expect(executeServiceRequestCompletionWorkflow({
      requestId,
      authorityScope,
      actorUserId: providerUserId,
      kind: 'markCompleted',
      mutate: vi.fn().mockRejectedValue(new Error('timeout')),
      revalidate: vi.fn().mockResolvedValue(details()),
      getCurrentAuthorityScope: () => authorityScope,
    })).resolves.toMatchObject({ status: 'CompletedByProvider' })

    await expect(executeServiceRequestCompletionWorkflow({
      requestId,
      authorityScope,
      actorUserId: providerUserId,
      kind: 'markCompleted',
      mutate: vi.fn().mockRejectedValue(new Error('timeout')),
      revalidate: vi.fn().mockResolvedValue(details({ status: 'Accepted' })),
      getCurrentAuthorityScope: () => authorityScope,
    })).rejects.toThrow('timeout')
  })

  it('rejects stale actor settlement and duplicate same-runtime execution', async () => {
    let releaseMutation: (() => void) | undefined
    const mutation = new Promise<{ status: string }>((resolve) => {
      releaseMutation = () => resolve({ status: 'CompletedByProvider' })
    })

    const first = executeServiceRequestCompletionWorkflow({
      requestId,
      authorityScope,
      actorUserId: providerUserId,
      kind: 'markCompleted',
      mutate: () => mutation,
      revalidate: vi.fn().mockResolvedValue(details()),
      getCurrentAuthorityScope: () => authorityScope,
    })

    await expect(executeServiceRequestCompletionWorkflow({
      requestId,
      authorityScope,
      actorUserId: providerUserId,
      kind: 'markCompleted',
      mutate: vi.fn(),
      revalidate: vi.fn(),
      getCurrentAuthorityScope: () => authorityScope,
    })).rejects.toThrow('already in progress')

    releaseMutation?.()
    await expect(first).resolves.toBeTruthy()

    let scope = authorityScope
    await expect(executeServiceRequestCompletionWorkflow({
      requestId,
      authorityScope,
      actorUserId: providerUserId,
      kind: 'markCompleted',
      mutate: async () => {
        scope = 'replacement-tenant:replacement-user'
        return { status: 'CompletedByProvider' }
      },
      revalidate: vi.fn(),
      getCurrentAuthorityScope: () => scope,
    })).rejects.toThrow('actor authority changed')
  })

  it('rejects wrong request, wrong actor, and wrong lifecycle authority', () => {
    expect(deriveServiceRequestCompletionOutcome(
      details({ requestId: buyerUserId }),
      requestId,
      providerUserId,
      'markCompleted',
    )).toBeNull()
    expect(deriveServiceRequestCompletionOutcome(
      details(),
      requestId,
      buyerUserId,
      'markCompleted',
    )).toBeNull()
    expect(deriveServiceRequestCompletionOutcome(
      details({
        status: 'CompletedByBuyer',
        completionEvidence: {
          ...details().completionEvidence!,
          status: 'CompletedByBuyer',
        },
      }),
      requestId,
      providerUserId,
      'markCompleted',
    )).toBeNull()
  })

  it('rejects matching completion status without coherent backend completion evidence', () => {
    expect(deriveServiceRequestCompletionOutcome(
      details({ completionEvidence: undefined }),
      requestId,
      providerUserId,
      'markCompleted',
    )).toBeNull()

    expect(deriveServiceRequestCompletionOutcome(
      details({
        completionEvidence: {
          ...details().completionEvidence!,
          providerCompletion: null,
        },
      }),
      requestId,
      providerUserId,
      'markCompleted',
    )).toBeNull()
  })
})

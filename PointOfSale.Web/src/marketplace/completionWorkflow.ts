import { getMarketplaceAuthorityScope } from './authority'
import { deriveServiceRequestLifecycleMutationAuthority } from './status'
import { deriveServiceRequestDetailsLifecyclePresentation } from './status'
import { deriveServiceRequestCompletionStatePresentation } from './completionStateAuthority'
import type { ServiceRequestDetails } from './types'

export type CompletionWorkflowKind = 'markCompleted' | 'confirmCompletion'

type CompletionMutationResponse = Readonly<{ status?: unknown }>

type ExecuteCompletionWorkflowOptions = Readonly<{
  requestId: string
  authorityScope: string
  actorUserId: unknown
  kind: CompletionWorkflowKind
  mutate: () => Promise<CompletionMutationResponse>
  revalidate: () => Promise<ServiceRequestDetails>
  getCurrentAuthorityScope?: () => string
}>

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const completionExecutions = new Set<string>()

const normalizedGuid = (value: unknown) => {
  const normalized = String(value ?? '').trim().toLowerCase()
  return guidPattern.test(normalized) ? normalized : null
}

const expectedActorId = (
  request: Pick<ServiceRequestDetails, 'buyerUserId' | 'providerUserId'>,
  kind: CompletionWorkflowKind,
) => kind === 'markCompleted' ? request.providerUserId : request.buyerUserId

export function deriveServiceRequestCompletionOutcome(
  request: ServiceRequestDetails | null | undefined,
  requestId: unknown,
  actorUserId: unknown,
  kind: CompletionWorkflowKind,
): ServiceRequestDetails | null {
  const expectedRequestId = normalizedGuid(requestId)
  const authoritativeRequestId = normalizedGuid(request?.requestId)
  const authoritativeActorId = normalizedGuid(request ? expectedActorId(request, kind) : null)
  const currentActorId = normalizedGuid(actorUserId)
  const lifecycle = request
    ? deriveServiceRequestDetailsLifecyclePresentation(request)
    : null
  const completionState = request
    ? deriveServiceRequestCompletionStatePresentation(request, lifecycle, currentActorId)
    : null

  if (
    !request
    || !expectedRequestId
    || authoritativeRequestId !== expectedRequestId
    || !authoritativeActorId
    || authoritativeActorId !== currentActorId
    || !completionState
    || !deriveServiceRequestLifecycleMutationAuthority(request, kind)
  ) {
    return null
  }

  return request
}

export async function executeServiceRequestCompletionWorkflow({
  requestId,
  authorityScope,
  actorUserId,
  kind,
  mutate,
  revalidate,
  getCurrentAuthorityScope = getMarketplaceAuthorityScope,
}: ExecuteCompletionWorkflowOptions): Promise<ServiceRequestDetails> {
  const normalizedRequestId = normalizedGuid(requestId)
  if (!normalizedRequestId || authorityScope === 'ineligible') {
    throw new Error('Invalid completion workflow identity')
  }

  const executionKey = `${authorityScope}:${kind}:${normalizedRequestId}`
  if (completionExecutions.has(executionKey)) {
    throw new Error('Completion workflow already in progress')
  }

  completionExecutions.add(executionKey)
  try {
    if (authorityScope !== getCurrentAuthorityScope()) {
      throw new Error('Completion actor authority changed')
    }

    let mutationError: unknown = null
    try {
      const response = await mutate()
      if (!deriveServiceRequestLifecycleMutationAuthority(response, kind)) {
        mutationError = new Error('Invalid completion lifecycle response')
      }
    } catch (error) {
      mutationError = error
    }

    if (authorityScope !== getCurrentAuthorityScope()) {
      throw new Error('Completion actor authority changed')
    }

    let authoritativeRequest: ServiceRequestDetails
    try {
      authoritativeRequest = await revalidate()
    } catch (error) {
      throw mutationError ?? error
    }

    if (authorityScope !== getCurrentAuthorityScope()) {
      throw new Error('Completion actor authority changed')
    }

    const outcome = deriveServiceRequestCompletionOutcome(
      authoritativeRequest,
      normalizedRequestId,
      actorUserId,
      kind,
    )
    if (!outcome) {
      throw mutationError ?? new Error('Completion authority revalidation failed')
    }

    return outcome
  } finally {
    completionExecutions.delete(executionKey)
  }
}

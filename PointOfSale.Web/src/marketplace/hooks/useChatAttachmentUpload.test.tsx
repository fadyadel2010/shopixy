import { act, cleanup, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { useChatAttachmentUpload } from './useChatAttachmentUpload'
import type { ChatIdentityAuthority } from '../chatAuthority'

const mocks = vi.hoisted(() => ({ upload: vi.fn(), scope: 'synthetic-authority' }))
vi.mock('../api/marketplaceApi', () => ({ marketplaceApi: { uploadChatAttachment: mocks.upload } }))
vi.mock('../authority', () => ({ useMarketplaceAuthorityScope: () => mocks.scope, getMarketplaceAuthorityScope: () => mocks.scope }))
const identity: ChatIdentityAuthority = { requestId: 'synthetic-request', serviceId: 'synthetic-service', buyerUserId: 'synthetic-buyer',
    providerUserId: 'synthetic-provider', currentUserId: 'synthetic-buyer', currentUserRole: 'Buyer', canViewChatHistory: true, canSendChat: true }
const file = new File(['synthetic'], 'fixture.txt', { type: 'text/plain' })
beforeEach(() => { mocks.upload.mockReset(); mocks.scope = 'synthetic-authority' })
afterEach(cleanup)

it('refuses foreign/ineligible identities before upload and never treats an upload as a message send', async () => {
    const view = renderHook(() => useChatAttachmentUpload(null))
    await expect(view.result.current(identity.requestId, file)).rejects.toThrow('unavailable')
    expect(mocks.upload).not.toHaveBeenCalled()
    const allowed = renderHook(() => useChatAttachmentUpload(identity))
    await expect(allowed.result.current('foreign-request', file)).rejects.toThrow('unavailable')
    mocks.upload.mockResolvedValue({ unadmitted: 'payload' })
    await expect(allowed.result.current(identity.requestId, file)).resolves.toEqual({ unadmitted: 'payload' })
    expect(mocks.upload).toHaveBeenCalledExactlyOnceWith(identity.requestId, file)
})

it('fences repeated uploads and rejects results after identity or authority changes', async () => {
    let finish!: (value: unknown) => void
    mocks.upload.mockImplementation(() => new Promise(resolve => { finish = resolve }))
    const view = renderHook(({ current }) => useChatAttachmentUpload(current), { initialProps: { current: identity } })
    const pending = view.result.current(identity.requestId, file)
    await expect(view.result.current(identity.requestId, file)).rejects.toThrow('unavailable')
    view.rerender({ current: { ...identity, currentUserId: 'another-user' } })
    await act(async () => { finish({ unadmitted: true }); await expect(pending).rejects.toThrow('changed') })
    expect(mocks.upload).toHaveBeenCalledTimes(1)
    view.rerender({ current: identity })
    const scopePending = view.result.current(identity.requestId, file)
    mocks.scope = 'rotated'
    await act(async () => { finish({ unadmitted: true }); await expect(scopePending).rejects.toThrow('changed') })
})

it('rejects late results after unmount', async () => {
    let finish!: (value: unknown) => void
    mocks.upload.mockImplementation(() => new Promise(resolve => { finish = resolve }))
    const view = renderHook(() => useChatAttachmentUpload(identity))
    const pending = view.result.current(identity.requestId, file)
    view.unmount(); finish({ unadmitted: true })
    await expect(pending).rejects.toThrow('changed')
})

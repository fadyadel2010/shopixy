import { beforeEach, describe, expect, it, vi } from 'vitest'

const apiMock = vi.hoisted(() => ({
    get: vi.fn(),
    post: vi.fn(),
    patch: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
}))

vi.mock('./api', () => ({ default: apiMock }))

import {
    acceptPosUserInvitation,
    changePosUserRole,
    changePosUserStatus,
    invitePosUser,
    listPosUsers,
    revokePosUserSession,
    transferPosOwnership,
    updatePosUserProfile,
} from './userManagementApi'

beforeEach(() => {
    vi.clearAllMocks()
})

describe('userManagementApi', () => {
    it('lists users with filter params and request config passthrough', async () => {
        apiMock.get.mockResolvedValue({ data: {} })
        const signal = new AbortController().signal
        await listPosUsers({ page: 2, search: 'cash' }, { signal })
        expect(apiMock.get).toHaveBeenCalledWith('/admin/users', { params: { page: 2, search: 'cash' }, signal })
    })

    it('quotes the If-Match version on mutations', async () => {
        apiMock.patch.mockResolvedValue({ data: {} })
        apiMock.put.mockResolvedValue({ data: {} })
        await updatePosUserProfile('id-1', { firstName: 'A' }, '42')
        expect(apiMock.patch).toHaveBeenCalledWith('/admin/users/id-1/profile', { firstName: 'A' }, { headers: { 'If-Match': '"42"' } })
        await changePosUserRole('id-1', 'Manager', '7')
        expect(apiMock.put).toHaveBeenCalledWith('/admin/users/id-1/role', { role: 'Manager' }, { headers: { 'If-Match': '"7"' } })
        await changePosUserStatus('id-1', false, 'reason', '9')
        expect(apiMock.put).toHaveBeenCalledWith('/admin/users/id-1/status', { isActive: false, reason: 'reason' }, { headers: { 'If-Match': '"9"' } })
    })

    it('posts invitations, resets, revocations, and ownership transfer to the right routes', async () => {
        apiMock.post.mockResolvedValue({ data: {} })
        await invitePosUser({ username: 'u' })
        expect(apiMock.post).toHaveBeenCalledWith('/admin/users/invitations', { username: 'u' })
        await revokePosUserSession('id-1', undefined, '11')
        expect(apiMock.post).toHaveBeenCalledWith('/admin/users/id-1/session/revoke', null, {
            params: undefined,
            headers: { 'If-Match': '"11"' },
        })
        await transferPosOwnership('target-id', '12', '13')
        expect(apiMock.post).toHaveBeenCalledWith(
            '/admin/users/ownership-transfer',
            { targetUserId: 'target-id', targetVersion: '12' },
            { headers: { 'If-Match': '"13"' } },
        )
        await acceptPosUserInvitation('tok', 'LongEnough1!')
        expect(apiMock.post).toHaveBeenCalledWith('/auth/user-invitations/accept', { token: 'tok', password: 'LongEnough1!' })
    })
})

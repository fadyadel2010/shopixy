import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import PosPermissionRouteGuard from '../components/PosPermissionRouteGuard'
import UsersManagementPage from './UsersManagementPage'
import { useAuthStore } from '../store/authStore'
import i18n from '../i18n/config'

const apiMock = vi.hoisted(() => ({
    get: vi.fn(),
}))

vi.mock('../services/api', () => ({
    default: apiMock,
    isRequestCanceled: (error) => error?.code === 'ERR_CANCELED',
}))

const usersMock = vi.hoisted(() => ({
    listPosUsers: vi.fn(),
    getPosUser: vi.fn(),
    getPosUserRoles: vi.fn(),
    getPosUserAudit: vi.fn(),
    invitePosUser: vi.fn(),
    resendPosUserInvitation: vi.fn(),
    revokePosUserInvitation: vi.fn(),
    updatePosUserProfile: vi.fn(),
    changePosUserRole: vi.fn(),
    changePosUserStatus: vi.fn(),
    issuePosUserPasswordReset: vi.fn(),
    revokePosUserSession: vi.fn(),
    transferPosOwnership: vi.fn(),
}))

vi.mock('../services/userManagementApi', () => usersMock)

const ownerUser = {
    id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    tenantId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
    role: 'Owner',
    effectivePermissions: ['users.view', 'users.invite', 'users.profile.manage', 'users.role.manage', 'users.status.manage', 'users.password-reset.initiate', 'users.session.revoke', 'users.ownership.transfer', 'audit.view'],
}

const adminUser = {
    ...ownerUser,
    id: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
    role: 'Admin',
    effectivePermissions: ['users.view', 'users.invite', 'users.profile.manage', 'users.role.manage', 'users.status.manage', 'users.password-reset.initiate', 'users.session.revoke', 'audit.view'],
}

const managerUser = {
    ...ownerUser,
    id: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd',
    role: 'Manager',
    effectivePermissions: ['dashboard.view', 'sales.view'],
}

const rows = [
    { id: ownerUser.id, username: 'owner', email: 'owner@example.com', firstName: 'Own', lastName: 'Er', phone: null, role: 'Owner', status: 'Active', emailVerified: true, lastLoginAt: '2026-09-16T10:00:00Z', invitedAtUtc: null, version: '3' },
    { id: 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', username: 'cashier.one', email: 'cashier@example.com', firstName: 'Cash', lastName: 'Ier', phone: null, role: 'Cashier', status: 'Active', emailVerified: true, lastLoginAt: null, invitedAtUtc: '2026-09-16T10:00:00Z', version: '5' },
    { id: 'ffffffff-ffff-4fff-8fff-ffffffffffff', username: 'pending.one', email: 'pending@example.com', firstName: 'Pend', lastName: 'Ing', phone: null, role: 'Manager', status: 'Pending', emailVerified: false, lastLoginAt: null, invitedAtUtc: '2026-09-16T10:00:00Z', version: '1' },
]

const listResponse = {
    data: { items: rows, totalCount: 3, totalPages: 1, activeCount: 2, pendingCount: 1, disabledCount: 0, ownerAdminCount: 1 },
}

const rolesResponse = {
    data: [
        { name: 'Owner', permissions: ['users.view'], assignableByActor: false },
        { name: 'Admin', permissions: ['users.view', 'users.invite'], assignableByActor: true },
        { name: 'Manager', permissions: [], assignableByActor: true },
        { name: 'Cashier', permissions: [], assignableByActor: true },
        { name: 'Warehouse', permissions: [], assignableByActor: true },
    ],
}

const loginAs = (user) => useAuthStore.setState({ user, isAuthenticated: true, hasHydrated: true })

// Desktop table and mobile cards render the same row (only one is visible
// per breakpoint via CSS), so duplicate action labels exist in the DOM.
const menuButton = (username) => screen.getAllByLabelText(`User actions: ${username}`)[0]

beforeEach(async () => {
    await i18n.changeLanguage('en')
    vi.clearAllMocks()
    apiMock.get.mockResolvedValue({ data: { user: ownerUser } })
    usersMock.listPosUsers.mockResolvedValue(listResponse)
    usersMock.getPosUserRoles.mockResolvedValue(rolesResponse)
    usersMock.getPosUser.mockResolvedValue({ data: rows[1] })
    usersMock.getPosUserAudit.mockResolvedValue({ data: { items: [], totalCount: 0 } })
})

afterEach(() => {
    useAuthStore.setState({ user: null, isAuthenticated: false, hasHydrated: true })
})

const renderPage = async (user) => {
    loginAs(user)
    render(<UsersManagementPage />)
    await screen.findByText('Users Management')
}

describe('UsersManagementPage access', () => {
    it('shows workspace, summary, invite, and transfer controls to the Owner', async () => {
        await renderPage(ownerUser)
        expect(screen.getAllByText('Active').length).toBeGreaterThan(0)
        expect(screen.getAllByText('cashier.one').length).toBeGreaterThan(0)
        expect(screen.getByRole('button', { name: 'Invite user' })).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Transfer ownership' })).toBeInTheDocument()
    })

    it('hides ownership transfer from Admin but keeps invite', async () => {
        await renderPage(adminUser)
        expect(screen.getByRole('button', { name: 'Invite user' })).toBeInTheDocument()
        expect(screen.queryByRole('button', { name: 'Transfer ownership' })).not.toBeInTheDocument()
    })

    it('hides mutation controls from roles without users permissions', async () => {
        await renderPage(managerUser)
        expect(screen.queryByRole('button', { name: 'Invite user' })).not.toBeInTheDocument()
        expect(screen.queryByRole('button', { name: 'Transfer ownership' })).not.toBeInTheDocument()
        expect(screen.queryByLabelText(/User actions:/)).not.toBeInTheDocument()
    })

    it('denies the users route without users.view and admits Owner/Admin', async () => {
        const renderRoute = () => render(
            <MemoryRouter initialEntries={['/users']}>
                <Routes>
                    <Route path="/" element={<div>Dashboard</div>} />
                    <Route element={<PosPermissionRouteGuard permission="users.view" />}>
                        <Route path="/users" element={<div>Users workspace</div>} />
                    </Route>
                </Routes>
            </MemoryRouter>,
        )
        loginAs(managerUser)
        const denied = renderRoute()
        expect(screen.getByText('Dashboard')).toBeInTheDocument()
        denied.unmount()

        loginAs(adminUser)
        renderRoute()
        expect(screen.getByText('Users workspace')).toBeInTheDocument()
    })

    it('renders Arabic RTL without raw keys', async () => {
        await i18n.changeLanguage('ar')
        loginAs(ownerUser)
        const { container } = render(<UsersManagementPage />)
        await screen.findByText('إدارة المستخدمين')
        expect(container.querySelector('[dir="rtl"]')).not.toBeNull()
        expect(screen.queryByText(/userManagement\./)).toBeNull()
        await i18n.changeLanguage('en')
    })
})

describe('UsersManagementPage mutations', () => {
    it('debounces search without issuing requests for intermediate keystrokes', async () => {
        const user = userEvent.setup()
        await renderPage(ownerUser)
        await waitFor(() => expect(usersMock.listPosUsers).toHaveBeenCalledTimes(1))

        await user.type(screen.getByPlaceholderText('Username, email, or name'), 'cash')
        expect(usersMock.listPosUsers).toHaveBeenCalledTimes(1)

        await waitFor(() => expect(usersMock.listPosUsers).toHaveBeenCalledTimes(2), { timeout: 1200 })
        expect(usersMock.listPosUsers.mock.calls[1][0]).toMatchObject({ search: 'cash' })
    })

    it('keeps profile input on version conflict and refreshes the version', async () => {
        const user = userEvent.setup()
        await renderPage(ownerUser)

        await user.click(menuButton('cashier.one'))
        await user.click(screen.getByRole('menuitem', { name: 'Edit profile' }))

        const firstName = screen.getByLabelText(/First name/)
        await user.clear(firstName)
        await user.type(firstName, 'Cashier Two')

        usersMock.updatePosUserProfile.mockRejectedValueOnce({
            response: { status: 412, data: { code: 'USER_VERSION_CONFLICT' } },
        })
        usersMock.getPosUser.mockResolvedValueOnce({ data: { ...rows[1], version: '6' } })

        await user.click(screen.getByRole('button', { name: 'Save changes' }))

        await screen.findByText(/changed since you loaded it/)
        expect(firstName).toHaveValue('Cashier Two')
        expect(usersMock.getPosUser).toHaveBeenCalledWith(rows[1].id)
        expect(screen.getByText('Current server values')).toBeInTheDocument()
        expect(screen.getAllByText('cashier@example.com').length).toBeGreaterThan(0)
        expect(screen.getByRole('dialog')).toBeInTheDocument()
    })

    it('shows the conflict message when an invitation collides', async () => {
        const user = userEvent.setup()
        await renderPage(ownerUser)

        await user.click(screen.getByRole('button', { name: 'Invite user' }))
        const inviteDialog = screen.getByRole('dialog')
        await user.type(within(inviteDialog).getByLabelText(/Username/), 'cashier.one')
        await user.type(within(inviteDialog).getByLabelText(/Email/), 'cashier@example.com')
        await user.type(within(inviteDialog).getByLabelText(/First name/), 'Cash')
        await user.type(within(inviteDialog).getByLabelText(/Last name/), 'Ier')
        await user.selectOptions(within(inviteDialog).getByLabelText(/Role/), 'Cashier')

        usersMock.invitePosUser.mockRejectedValueOnce({
            response: { status: 409, data: { code: 'IDENTITY_ALREADY_EXISTS' } },
        })
        await user.click(within(inviteDialog).getByRole('button', { name: 'Send invitation' }))

        await screen.findByText('This username or email is already in use.')
        expect(screen.getByRole('dialog')).toBeInTheDocument()
    })

    it('shows access denied when the list is forbidden', async () => {
        usersMock.listPosUsers.mockRejectedValueOnce({
            response: { status: 403, data: { code: 'POS_PERMISSION_FORBIDDEN' } },
        })
        await renderPage(ownerUser)
        await screen.findByText('You do not have permission for this action.')
    })

    it('locks submission while a mutation is in flight', async () => {
        const user = userEvent.setup()
        await renderPage(ownerUser)

        await user.click(menuButton('cashier.one'))
        await user.click(screen.getByRole('menuitem', { name: 'Edit profile' }))

        let resolveSave
        usersMock.updatePosUserProfile.mockReturnValueOnce(new Promise((resolve) => { resolveSave = resolve }))
        await user.click(screen.getByRole('button', { name: 'Save changes' }))

        expect(screen.getByRole('button', { name: 'Saving...' })).toBeDisabled()
        resolveSave({ data: rows[1] })
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    })

    it('requires a reason to disable and typed confirmation for Admin targets', async () => {
        const user = userEvent.setup()
        const adminRow = { ...rows[1], id: 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee', username: 'admin.one', role: 'Admin', status: 'Active', version: '2' }
        usersMock.listPosUsers.mockResolvedValue({
            data: { ...listResponse.data, items: [rows[0], adminRow], totalCount: 2 },
        })
        await renderPage(ownerUser)

        await user.click(menuButton('admin.one'))
        await user.click(screen.getByRole('menuitem', { name: 'Disable' }))

        expect(screen.getByRole('button', { name: 'Disable user' })).toBeDisabled()
        await user.type(screen.getByPlaceholderText('Why is this user being disabled?'), 'Restructure')
        expect(screen.getByRole('button', { name: 'Disable user' })).toBeDisabled()
        expect(screen.getByText(/confirmation word below/)).toBeInTheDocument()
        await user.type(screen.getByPlaceholderText('DISABLE'), 'DISABLE')

        usersMock.changePosUserStatus.mockResolvedValueOnce({ data: adminRow })
        await user.click(screen.getByRole('button', { name: 'Disable user' }))
        await waitFor(() => expect(usersMock.changePosUserStatus).toHaveBeenCalledWith(
            adminRow.id, false, 'Restructure', '2',
        ))
    })

    it('shows the session warning and access summary on role change', async () => {
        const user = userEvent.setup()
        await renderPage(ownerUser)

        await user.click(menuButton('cashier.one'))
        await user.click(screen.getByRole('menuitem', { name: 'Change role' }))

        expect(screen.getByText(/current session will be revoked/)).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Change role' })).toBeDisabled()
        await user.selectOptions(screen.getByLabelText(/New role/), 'Manager')
        expect(screen.getByText('Access summary')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Change role' })).toBeEnabled()
    })

    it('gates ownership transfer behind an explicit typed ceremony', async () => {
        const user = userEvent.setup()
        await renderPage(ownerUser)

        await user.click(screen.getByRole('button', { name: 'Transfer ownership' }))
        expect(screen.getByText(/Both your session and the new Owner's session will be revoked/)).toBeInTheDocument()

        const dialog = screen.getByRole('dialog')
        const submit = within(dialog).getByRole('button', { name: 'Transfer ownership' })
        expect(submit).toBeDisabled()
        await user.selectOptions(screen.getByLabelText(/Select the new Owner/), rows[1].id)
        expect(submit).toBeDisabled()
        await user.type(screen.getByPlaceholderText('TRANSFER'), 'TRANSFER')
        expect(submit).toBeEnabled()

        usersMock.transferPosOwnership.mockResolvedValueOnce({ data: {} })
        await user.click(submit)
        await waitFor(() => expect(usersMock.transferPosOwnership).toHaveBeenCalledWith(rows[1].id, '5', '3'))
    })

    it('explains the reset flow and reports safe success', async () => {
        const user = userEvent.setup()
        await renderPage(ownerUser)

        await user.click(menuButton('cashier.one'))
        await user.click(screen.getByRole('menuitem', { name: 'Send password reset' }))
        expect(screen.getByText(/You never see or set their password/)).toBeInTheDocument()

        usersMock.issuePosUserPasswordReset.mockResolvedValueOnce({ data: {} })
        await user.click(screen.getByRole('button', { name: 'Send reset link' }))
        await screen.findByText('If the account is eligible, a password-reset link has been sent.')
    })
})

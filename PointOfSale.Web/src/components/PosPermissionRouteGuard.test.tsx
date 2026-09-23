import { afterEach, describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import PosPermissionRouteGuard from './PosPermissionRouteGuard'
import { useAuthStore } from '../store/authStore'

const user = {
    id: '11111111-1111-4111-8111-111111111111',
    tenantId: '22222222-2222-4222-8222-222222222222',
    role: 'Owner',
    effectivePermissions: ['dashboard.view'],
}

const renderRoute = () => render(
    <MemoryRouter initialEntries={['/audit']}>
        <Routes>
            <Route path="/" element={<div>Dashboard</div>} />
            <Route element={<PosPermissionRouteGuard permission="audit.view" />}>
                <Route path="/audit" element={<div>Audit</div>} />
            </Route>
        </Routes>
    </MemoryRouter>,
)

describe('PosPermissionRouteGuard', () => {
    afterEach(() => useAuthStore.setState({ user: null, isAuthenticated: false, hasHydrated: true }))

    it('admits a server-derived permission', () => {
        useAuthStore.setState({ user: { ...user, effectivePermissions: ['dashboard.view', 'audit.view'] } })
        renderRoute()
        expect(screen.getByText('Audit')).toBeInTheDocument()
    })

    it('denies a role label without the required permission', () => {
        useAuthStore.setState({ user })
        renderRoute()
        expect(screen.getByText('Dashboard')).toBeInTheDocument()
        expect(screen.queryByText('Audit')).not.toBeInTheDocument()
    })
})

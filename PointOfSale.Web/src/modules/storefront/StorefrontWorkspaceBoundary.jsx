import { Fragment } from 'react'
import { Outlet, useLocation } from 'react-router-dom'
import { useAuthStore } from '../../store/authStore'

// Candidates, accepted revisions and confirmations belong to one account/store.
export function StorefrontWorkspaceBoundary() {
    const user = useAuthStore(state => state.user)
    const { pathname } = useLocation()
    return <Fragment key={`${user?.tenantId || ''}:${user?.id || ''}:${pathname}`}><Outlet /></Fragment>
}

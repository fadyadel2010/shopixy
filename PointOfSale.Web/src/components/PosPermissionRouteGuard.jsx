import { Navigate, Outlet } from 'react-router-dom'
import { hasPosPermission } from '../auth/posPermissions'
import { useAuthStore } from '../store/authStore'

export default function PosPermissionRouteGuard({ permission, deniedElement }) {
    const user = useAuthStore((state) => state.user)

    if (!hasPosPermission(user, permission)) {
        return deniedElement ?? <Navigate to="/" replace />
    }

    return <Outlet />
}

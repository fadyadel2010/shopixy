import { Navigate, Outlet } from 'react-router-dom'
import { canAccessCommerceHubVendorNavigationCapability } from '../auth/posVendorSessionAuthority'
import { useAuthStore } from '../store/authStore'

export default function CommerceHubVendorRouteGuard({ capability }) {
    const user = useAuthStore((state) => state.user)

    if (!canAccessCommerceHubVendorNavigationCapability(user, capability)) {
        return <Navigate to="/" replace />
    }

    return <Outlet />
}

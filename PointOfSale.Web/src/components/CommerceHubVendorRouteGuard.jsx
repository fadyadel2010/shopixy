import { Navigate, Outlet } from 'react-router-dom'
import { canAccessCommerceHubVendorNavigationCapability } from '../auth/posVendorSessionAuthority'
import { useAuthStore } from '../store/authStore'

export default function CommerceHubVendorRouteGuard({ capability, deniedElement }) {
    const user = useAuthStore((state) => state.user)

    if (!canAccessCommerceHubVendorNavigationCapability(user, capability)) {
        return deniedElement ?? <Navigate to="/" replace />
    }

    return <Outlet />
}

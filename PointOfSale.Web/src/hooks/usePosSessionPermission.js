import { useAuthStore } from '../store/authStore'
import { canManageStorefrontSettings } from '../auth/storefrontAdminAuthority'
import { hasPosPermission } from '../auth/posPermissions'

export function useHasPermission(permission) {
    const user = useAuthStore((state) => state.user)
    return hasPosPermission(user, permission)
}

export function useCanAccessStorefront() {
    const user = useAuthStore((state) => state.user)
    return canManageStorefrontSettings(user)
}

export function useIsEmailVerified() {
    const user = useAuthStore((state) => state.user)
    return user?.emailVerified ?? false
}

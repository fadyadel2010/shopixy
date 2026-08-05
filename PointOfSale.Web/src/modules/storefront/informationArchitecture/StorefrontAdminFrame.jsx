import { createContext, useContext, useMemo, useState } from 'react'
import { Outlet } from 'react-router-dom'
import { StorefrontBreadcrumbs } from './StorefrontBreadcrumbs'
import {
    AuthoringPlatformProvider,
    AuthoringShell,
    useAuthoringPlatform,
} from '../authoringPlatform'

const StorefrontAdminFrameContext = createContext(null)

export function useStorefrontAdminFrame() {
    return useContext(StorefrontAdminFrameContext) || {}
}

export function StorefrontAdminFrame() {
    return (
        <AuthoringPlatformProvider>
            <StorefrontAdminFrameContent />
        </AuthoringPlatformProvider>
    )
}

function StorefrontAdminFrameContent() {
    const [currentLabel, setCurrentLabel] = useState('')
    const { guardNavigation, registerNavigationGuard } = useAuthoringPlatform()
    const frameContext = useMemo(() => ({
        registerBreadcrumbNavigationGuard: registerNavigationGuard,
        setStorefrontBreadcrumbLabel: setCurrentLabel,
    }), [registerNavigationGuard])

    return (
        <StorefrontAdminFrameContext.Provider value={frameContext}>
            <AuthoringShell className="space-y-4">
                <StorefrontBreadcrumbs
                    currentLabel={currentLabel}
                    onNavigate={guardNavigation}
                />
                <Outlet />
            </AuthoringShell>
        </StorefrontAdminFrameContext.Provider>
    )
}

export default StorefrontAdminFrame

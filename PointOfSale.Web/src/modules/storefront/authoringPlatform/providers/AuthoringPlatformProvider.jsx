import { createContext, useCallback, useContext, useMemo, useRef } from 'react'

const defaultPlatform = {
    registerNavigationGuard: () => () => {},
    guardNavigation: () => true,
}

const AuthoringPlatformContext = createContext(defaultPlatform)

export function AuthoringPlatformProvider({ children }) {
    const navigationGuardRef = useRef(null)

    const registerNavigationGuard = useCallback((guard) => {
        navigationGuardRef.current = guard
        return () => {
            if (navigationGuardRef.current === guard) navigationGuardRef.current = null
        }
    }, [])

    const guardNavigation = useCallback((path) => navigationGuardRef.current?.(path) ?? true, [])
    const value = useMemo(() => ({ registerNavigationGuard, guardNavigation }), [guardNavigation, registerNavigationGuard])

    return <AuthoringPlatformContext.Provider value={value}>{children}</AuthoringPlatformContext.Provider>
}

export function useAuthoringPlatform() {
    return useContext(AuthoringPlatformContext)
}

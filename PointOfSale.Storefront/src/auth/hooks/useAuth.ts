/**
 * useAuth Hook
 * Phase F1: Storefront Auth UI
 * 
 * Primary hook for authentication operations.
 * Provides a clean interface for components to interact with auth.
 */

import { useCallback, useEffect } from 'react';
import {
    useAuthStore,
    selectAuthStatus,
    selectCustomer,
    selectIsAuthenticated,
    selectIsLoading,
    selectAuthError,
    selectAvailableProviders,
    selectEmailVerified,
} from '../store/authStore';
import type { LoginRequest, RegisterRequest, AuthError, AuthResponse } from '../types';

export interface UseAuthReturn {
    // State
    status: 'unknown' | 'authenticated' | 'unauthenticated' | 'degraded';
    customer: ReturnType<typeof selectCustomer>;
    isAuthenticated: boolean;
    isLoading: boolean;
    error: AuthError | null;
    emailVerified: boolean;

    // Social providers (includes Client IDs for OAuth initialization)
    providers: {
        google: boolean;
        facebook: boolean;
        googleClientId?: string;
        facebookAppId?: string;
    } | null;

    // Actions
    login: (request: LoginRequest) => Promise<void>;
    register: (request: RegisterRequest) => Promise<AuthResponse>;
    logout: () => Promise<void>;
    googleLogin: (idToken: string, state?: string, signal?: AbortSignal) => Promise<boolean>;
    facebookLogin: (accessToken: string, state?: string, signal?: AbortSignal) => Promise<boolean>;
    clearError: () => void;

    // Utils
    checkAuth: () => Promise<void>;
}

/**
 * Hook for authentication
 * 
 * @example
 * ```tsx
 * const { isAuthenticated, login, logout, error } = useAuth();
 * 
 * const handleLogin = async () => {
 *   try {
 *     await login({ email, password });
 *     navigate('/');
 *   } catch (err) {
 *     // Error is also available in error state
 *   }
 * };
 * ```
 */
export function useAuth(): UseAuthReturn {
    // Select state with individual selectors for optimized re-renders
    const status = useAuthStore(selectAuthStatus);
    const customer = useAuthStore(selectCustomer);
    const isAuthenticated = useAuthStore(selectIsAuthenticated);
    const isLoading = useAuthStore(selectIsLoading);
    const error = useAuthStore(selectAuthError);
    const providers = useAuthStore(selectAvailableProviders);
    const emailVerified = useAuthStore(selectEmailVerified);

    // Get actions from store
    const storeLogin = useAuthStore((state) => state.login);
    const storeRegister = useAuthStore((state) => state.register);
    const storeLogout = useAuthStore((state) => state.logout);
    const storeGoogleLogin = useAuthStore((state) => state.googleLogin);
    const storeFacebookLogin = useAuthStore((state) => state.facebookLogin);
    const storeClearError = useAuthStore((state) => state.clearError);
    const storeCheckAuth = useAuthStore((state) => state.checkAuth);
    const storeLoadProviders = useAuthStore((state) => state.loadProviders);

    // Load providers on mount
    useEffect(() => {
        void storeLoadProviders();
        const refresh = () => { void storeLoadProviders(); };
        window.addEventListener('focus', refresh);
        return () => window.removeEventListener('focus', refresh);
    }, [storeLoadProviders]);

    // Wrap actions with useCallback for stable references
    const login = useCallback(async (request: LoginRequest) => {
        await storeLogin(request);
    }, [storeLogin]);

    const register = useCallback(async (request: RegisterRequest) => {
        return await storeRegister(request);
    }, [storeRegister]);

    const logout = useCallback(async () => {
        await storeLogout();
    }, [storeLogout]);

    const googleLogin = useCallback(async (idToken: string, state?: string, signal?: AbortSignal) => {
        return await storeGoogleLogin(idToken, state, signal);
    }, [storeGoogleLogin]);

    const facebookLogin = useCallback(async (accessToken: string, state?: string, signal?: AbortSignal) => {
        return await storeFacebookLogin(accessToken, state, signal);
    }, [storeFacebookLogin]);

    const clearError = useCallback(() => {
        storeClearError();
    }, [storeClearError]);

    const checkAuth = useCallback(async () => {
        await storeCheckAuth();
    }, [storeCheckAuth]);

    return {
        status,
        customer,
        isAuthenticated,
        isLoading,
        error,
        emailVerified,
        providers,
        login,
        register,
        logout,
        googleLogin,
        facebookLogin,
        clearError,
        checkAuth,
    };
}

/**
 * Account Hooks
 * Phase F2: Storefront Account Area
 * 
 * React hooks for account management functionality.
 */

import { useState, useCallback, useEffect } from 'react';
import { useAuthStore } from '../store/authStore';
import * as accountApi from '../api/accountApi';
import type {
    ProfileUpdateRequest,
    ChangePasswordRequest,
    SessionDto,
    AccountStatusResponse,
} from '../api/accountApi';

// ============================================================================
// Types
// ============================================================================

interface UseAccountResult {
    // State
    isLoading: boolean;
    error: string | null;
    hasPassword: boolean | null;
    provider: string | null;
    sessions: SessionDto[];
    accountStatus: AccountStatusResponse | null;

    // Actions
    updateProfile: (data: ProfileUpdateRequest) => Promise<boolean>;
    changePassword: (data: ChangePasswordRequest) => Promise<{ success: boolean; message: string }>;
    checkHasPassword: () => Promise<void>;
    loadSessions: () => Promise<void>;
    revokeSession: (sessionId: string) => Promise<boolean>;
    revokeAllSessions: () => Promise<{ success: boolean; revokedCount: number }>;
    loadAccountStatus: () => Promise<void>;
    clearError: () => void;
}

// ============================================================================
// Hook Implementation
// ============================================================================

export function useAccount(): UseAccountResult {
    const { status, checkAuth } = useAuthStore();

    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [hasPassword, setHasPassword] = useState<boolean | null>(null);
    const [provider, setProvider] = useState<string | null>(null);
    const [sessions, setSessions] = useState<SessionDto[]>([]);
    const [accountStatus, setAccountStatus] = useState<AccountStatusResponse | null>(null);

    const clearError = useCallback(() => setError(null), []);
    const isAuthenticated = status === 'authenticated';

    // Update profile
    const updateProfile = useCallback(async (data: ProfileUpdateRequest): Promise<boolean> => {
        if (!isAuthenticated) {
            setError('Not authenticated');
            return false;
        }

        setIsLoading(true);
        setError(null);

        try {
            const response = await accountApi.updateProfile(data);

            if (response.success) {
                // Refresh customer data in auth store
                await checkAuth();
                return true;
            } else {
                setError(response.message);
                return false;
            }
        } catch (err) {
            const message = err instanceof Error ? err.message : 'Failed to update profile';
            setError(message);
            return false;
        } finally {
            setIsLoading(false);
        }
    }, [checkAuth, isAuthenticated]);

    // Change password
    const changePassword = useCallback(async (data: ChangePasswordRequest): Promise<{ success: boolean; message: string }> => {
        if (!isAuthenticated) {
            return { success: false, message: 'Not authenticated' };
        }

        setIsLoading(true);
        setError(null);

        try {
            const response = await accountApi.changePassword(data);

            if (!response.success) {
                setError(response.message);
            }

            return { success: response.success, message: response.message };
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : 'Failed to change password';
            setError(message);
            return { success: false, message };
        } finally {
            setIsLoading(false);
        }
    }, [isAuthenticated]);

    // Check if has password
    const checkHasPassword = useCallback(async (): Promise<void> => {
        if (!isAuthenticated) return;

        try {
            const response = await accountApi.hasPassword();
            setHasPassword(response.hasPassword);
            setProvider(response.provider);
        } catch (err) {
            console.error('Failed to check password status:', err);
        }
    }, [isAuthenticated]);

    // Load sessions
    const loadSessions = useCallback(async (): Promise<void> => {
        if (!isAuthenticated) return;

        setIsLoading(true);
        try {
            const response = await accountApi.getSessions();
            setSessions(response.sessions);
        } catch (err) {
            console.error('Failed to load sessions:', err);
            setError('Failed to load sessions');
        } finally {
            setIsLoading(false);
        }
    }, [isAuthenticated]);

    // Revoke session
    const revokeSession = useCallback(async (sessionId: string): Promise<boolean> => {
        if (!isAuthenticated) return false;

        setIsLoading(true);
        setError(null);

        try {
            const response = await accountApi.revokeSession(sessionId);

            if (response.success) {
                // Remove from local list
                setSessions(prev => prev.filter(s => s.id !== sessionId));
                return true;
            } else {
                setError(response.message);
                return false;
            }
        } catch (err) {
            const message = err instanceof Error ? err.message : 'Failed to revoke session';
            setError(message);
            return false;
        } finally {
            setIsLoading(false);
        }
    }, [isAuthenticated]);

    // Revoke all sessions
    const revokeAllSessions = useCallback(async (): Promise<{ success: boolean; revokedCount: number }> => {
        if (!isAuthenticated) return { success: false, revokedCount: 0 };

        setIsLoading(true);
        setError(null);

        try {
            const response = await accountApi.revokeAllSessions();

            if (response.success) {
                // Reload sessions to show only current
                await loadSessions();
            } else {
                setError(response.message);
            }

            return { success: response.success, revokedCount: response.revokedCount };
        } catch (err) {
            const message = err instanceof Error ? err.message : 'Failed to revoke sessions';
            setError(message);
            return { success: false, revokedCount: 0 };
        } finally {
            setIsLoading(false);
        }
    }, [isAuthenticated, loadSessions]);

    // Load account status
    const loadAccountStatus = useCallback(async (): Promise<void> => {
        if (!isAuthenticated) return;

        try {
            const response = await accountApi.getAccountStatus();
            setAccountStatus(response);
        } catch (err) {
            console.error('Failed to load account status:', err);
        }
    }, [isAuthenticated]);

    return {
        isLoading,
        error,
        hasPassword,
        provider,
        sessions,
        accountStatus,
        updateProfile,
        changePassword,
        checkHasPassword,
        loadSessions,
        revokeSession,
        revokeAllSessions,
        loadAccountStatus,
        clearError,
    };
}

// ============================================================================
// Convenience hook for profile only
// ============================================================================

export function useProfile() {
    const { customer, isLoading: authLoading } = useAuthStore();
    const { updateProfile, isLoading, error, clearError } = useAccount();

    return {
        customer,
        isLoading: isLoading || authLoading,
        error,
        updateProfile,
        clearError,
    };
}

// ============================================================================
// Convenience hook for sessions only
// ============================================================================

export function useSessions() {
    const account = useAccount();

    useEffect(() => {
        account.loadSessions();
    }, [account.loadSessions]);

    return {
        sessions: account.sessions,
        isLoading: account.isLoading,
        error: account.error,
        revokeSession: account.revokeSession,
        revokeAllSessions: account.revokeAllSessions,
        refresh: account.loadSessions,
    };
}

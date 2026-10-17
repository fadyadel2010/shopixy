import type { StoreApi, UseBoundStore } from 'zustand';
export interface PosUser {
    id?: string; userId?: string; tenantId?: string; role?: string; email?: string; username?: string;
    firstName?: string; lastName?: string; vendorId?: string | null; permissions?: string[];
    [key: string]: unknown;
}
export interface PosAuthState {
    user: PosUser | null; isAuthenticated: boolean; hasHydrated: boolean;
    login(user: unknown): void; logout(): void; hydrateSession(user: unknown): void; updateUser(user: unknown): void;
    setHasHydrated(value: boolean): void;
}
export const useAuthStore: UseBoundStore<StoreApi<PosAuthState>>;
export const STORAGE_KEYS: { TENANT_ID: string; AUTH_STORAGE: string };
export const LEGACY_AUTH_TOKEN_KEYS: string[];
export function clearLegacyPosTokenStorage(): void;

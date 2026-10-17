import type { AxiosInstance } from 'axios';
export const affiliateAxios: AxiosInstance;
export const AFFILIATE_AUTH_REQUEST_TIMEOUT_MS: 30000;
export const AFFILIATE_AUTH_REQUEST_TIMEOUT_MESSAGE: string;
export const AFFILIATE_AUTH_STORAGE_KEYS: { ACCOUNT_ID: string; EMAIL: string; DISPLAY_NAME: string; REFERRAL_CODE: string };
export const AFFILIATE_AUTH_SESSION_EVENT_KEY: string;
export const AFFILIATE_AUTH_FAILURE_POLICY: { TERMINAL_LOGOUT: 'terminal-logout'; CALLER_OWNED_PASSIVE: 'caller-owned-passive' };
export function resolveAffiliatePortalReturnUrl(candidate: unknown): string;
export function setAffiliateLoginNavigator(navigator: (path: string) => void): void;
export interface AffiliateProfile {
    affiliateAccountId: string; email: string; displayName: string; referralCode: string;
    isEmailVerified?: boolean; walletNumber?: string | null; walletType?: string | null;
}
export const affiliateAuthApi: {
    register(data: { email: string; password: string; displayName: string }): Promise<{ message: string }>;
    login(data: { email: string; password: string }): Promise<AffiliateProfile>;
    verifyEmail(token: string): Promise<{ message: string }>;
    resendVerification(email: string): Promise<{ message: string }>;
    forgotPassword(email: string): Promise<{ message: string }>;
    resetPassword(token: string, newPassword: string): Promise<{ message: string }>;
    restoreSession(): Promise<AffiliateProfile>; logout(): Promise<void>; clearSessionPresentation(): void;
    getStoredProfile(): { email: string | null; displayName: string | null; referralCode: string | null; accountId: string | null };
    STORAGE_KEYS: typeof AFFILIATE_AUTH_STORAGE_KEYS;
};
export default affiliateAuthApi;

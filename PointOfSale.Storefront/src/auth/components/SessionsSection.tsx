/**
 * Sessions Section Component
 * Phase F2: Storefront Account Area
 * 
 * Session management with safety enforcement:
 * - Lists all active sessions (refresh tokens)
 * - Marks current session clearly
 * - Prevents accidental current session revocation
 * - Forces logout if current session is revoked
 */

import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from '@/i18n';
import { useAuthStore } from '@/auth/store/authStore';
import { useSessions } from '@/auth/hooks/useAccount';
import type { SessionDto } from '@/auth/api/accountApi';

export const SessionsSection: React.FC = () => {
    const { t, direction } = useTranslation();
    const navigate = useNavigate();
    const { logout } = useAuthStore();
    const {
        sessions,
        isLoading,
        error,
        revokeSession,
        revokeAllSessions
    } = useSessions();

    const [confirmRevokeAll, setConfirmRevokeAll] = useState(false);
    const [confirmRevokeId, setConfirmRevokeId] = useState<string | null>(null);

    // Helper to format date
    const formatDate = (dateStr: string) => {
        try {
            return new Date(dateStr).toLocaleDateString(undefined, {
                year: 'numeric',
                month: 'short',
                day: 'numeric',
                hour: '2-digit',
                minute: '2-digit'
            });
        } catch {
            return dateStr;
        }
    };

    // Handle revoking a specific session
    const handleRevokeSession = async (session: SessionDto) => {
        if (session.isCurrent) {
            // CRITICAL: If revoking current session, force logout
            const confirmed = window.confirm(
                t('account.sessions.revokeCurrentWarning', {
                    defaultValue: 'Revoking your current session will log you out immediately. Continue?'
                })
            );
            if (!confirmed) return;

            await revokeSession(session.id);
            // Force logout since current session was revoked
            await logout();
            navigate('/login');
            return;
        }

        // Non-current session - just revoke
        setConfirmRevokeId(null);
        await revokeSession(session.id);
    };

    // Handle revoking all sessions except current
    const handleRevokeAllSessions = async () => {
        setConfirmRevokeAll(false);
        const result = await revokeAllSessions();
        if (result.success && result.revokedCount > 0) {
            // Show success feedback (handled in parent via toast)
        }
    };

    // Count non-current sessions
    const otherSessionsCount = sessions.filter(s => !s.isCurrent).length;

    return (
        <div dir={direction} className="bg-background border border-muted/20 rounded-xl p-6">
            <div className="flex items-center justify-between mb-6">
                <div className="flex items-center gap-4">
                    <div className="w-12 h-12 bg-purple-100 rounded-full flex items-center justify-center">
                        <svg className="w-6 h-6 text-purple-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
                        </svg>
                    </div>
                    <div>
                        <h2 className="text-lg font-semibold text-text">
                            {t('account.sessions.title', { defaultValue: 'Sessions' })}
                        </h2>
                        <p className="text-sm text-muted">
                            {t('account.sessions.description', { defaultValue: 'Manage your active sessions' })}
                        </p>
                    </div>
                </div>

                {/* Revoke All Button */}
                {otherSessionsCount > 0 && (
                    <button
                        onClick={() => setConfirmRevokeAll(true)}
                        disabled={isLoading}
                        className="px-4 py-2 text-sm text-red-600 hover:text-red-700 hover:bg-red-50 rounded-lg transition-colors disabled:opacity-50"
                    >
                        {t('account.sessions.revokeAll', { defaultValue: 'End All Other Sessions' })}
                    </button>
                )}
            </div>

            {/* Error Display */}
            {error && (
                <div className="bg-red-50 border border-red-200 rounded-lg p-3 mb-4">
                    <p className="text-sm text-red-600">{error}</p>
                </div>
            )}

            {/* Loading State */}
            {isLoading && sessions.length === 0 && (
                <div className="text-center py-8">
                    <div className="animate-pulse text-muted">
                        {t('common.loading', { defaultValue: 'Loading...' })}
                    </div>
                </div>
            )}

            {/* Session List */}
            <div className="space-y-3">
                {sessions.map((session) => (
                    <div
                        key={session.id}
                        className={`flex items-center justify-between p-4 rounded-lg border ${session.isCurrent
                            ? 'border-primary/30 bg-primary/5'
                            : 'border-muted/20 bg-muted/5'
                            }`}
                    >
                        <div className="flex items-center gap-3">
                            {/* Device Icon */}
                            <div className={`w-10 h-10 rounded-full flex items-center justify-center ${session.isCurrent ? 'bg-primary/10' : 'bg-muted/10'
                                }`}>
                                <svg className={`w-5 h-5 ${session.isCurrent ? 'text-primary' : 'text-muted'}`} fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                        d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
                                </svg>
                            </div>

                            <div>
                                <div className="flex items-center gap-2">
                                    <span className="font-medium text-text">
                                        {session.device || t('account.sessions.unknownDevice', { defaultValue: 'Unknown Device' })}
                                    </span>
                                    {session.isCurrent && (
                                        <span className="px-2 py-0.5 text-xs font-medium text-primary bg-primary/10 rounded-full">
                                            {t('account.sessions.currentSession', { defaultValue: 'Current' })}
                                        </span>
                                    )}
                                </div>
                                <div className="text-sm text-muted">
                                    {session.ipAddress} • {formatDate(session.createdAt)}
                                </div>
                            </div>
                        </div>

                        {/* Actions */}
                        {!session.isCurrent && (
                            confirmRevokeId === session.id ? (
                                <div className="flex items-center gap-2">
                                    <button
                                        onClick={() => handleRevokeSession(session)}
                                        disabled={isLoading}
                                        className="px-3 py-1.5 text-sm text-white bg-red-600 hover:bg-red-700 rounded transition-colors disabled:opacity-50"
                                    >
                                        {t('common.confirm', { defaultValue: 'Confirm' })}
                                    </button>
                                    <button
                                        onClick={() => setConfirmRevokeId(null)}
                                        className="px-3 py-1.5 text-sm text-muted hover:text-text transition-colors"
                                    >
                                        {t('common.cancel', { defaultValue: 'Cancel' })}
                                    </button>
                                </div>
                            ) : (
                                <button
                                    onClick={() => setConfirmRevokeId(session.id)}
                                    disabled={isLoading}
                                    className="px-3 py-1.5 text-sm text-red-600 hover:text-red-700 hover:bg-red-50 rounded transition-colors disabled:opacity-50"
                                >
                                    {t('account.sessions.revokeSession', { defaultValue: 'End Session' })}
                                </button>
                            )
                        )}
                    </div>
                ))}

                {/* No Other Sessions */}
                {sessions.length === 1 && sessions[0]?.isCurrent && (
                    <div className="text-center py-4 text-muted">
                        {t('account.sessions.noOtherSessions', { defaultValue: 'No other active sessions' })}
                    </div>
                )}
            </div>

            {/* Revoke All Confirmation Modal */}
            {confirmRevokeAll && (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
                    <div className="bg-background rounded-xl shadow-2xl max-w-md w-full mx-4 p-6">
                        <h3 className="text-lg font-semibold text-text mb-4">
                            {t('account.sessions.revokeAllConfirm', { defaultValue: 'End All Other Sessions?' })}
                        </h3>
                        <p className="text-muted mb-6">
                            {t('account.sessions.revokeAllWarning', {
                                defaultValue: 'This will log you out from all other devices. Your current session will remain active.'
                            })}
                        </p>
                        <div className="flex gap-3 justify-end">
                            <button
                                onClick={() => setConfirmRevokeAll(false)}
                                className="px-4 py-2 text-muted hover:text-text transition-colors"
                            >
                                {t('common.cancel', { defaultValue: 'Cancel' })}
                            </button>
                            <button
                                onClick={handleRevokeAllSessions}
                                disabled={isLoading}
                                className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 transition-colors disabled:opacity-50"
                            >
                                {isLoading
                                    ? t('common.loading', { defaultValue: 'Processing...' })
                                    : t('account.sessions.revokeAll', { defaultValue: 'End All Other Sessions' })
                                }
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};

export default SessionsSection;

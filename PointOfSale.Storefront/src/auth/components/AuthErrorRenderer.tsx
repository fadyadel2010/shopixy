/**
 * AuthErrorRenderer Component
 * Phase F1: Storefront Auth UI
 * 
 * Renders auth errors with appropriate styling and actions.
 * Maps backend error codes to user-friendly messages.
 */

import React from 'react';
import { useTranslation } from '@/i18n';
import type { AuthError, AuthErrorCode } from '../types';

interface AuthErrorRendererProps {
    error: AuthError | null;
    onDismiss?: () => void;
    className?: string;
}

/**
 * Get icon for error type
 */
function getErrorIcon(code: AuthErrorCode): React.ReactNode {
    switch (code) {
        case 'TOKEN_REUSE_DETECTED':
        case 'ACCOUNT_DEACTIVATED':
            return (
                <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                        d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                </svg>
            );
        case 'TOO_MANY_ATTEMPTS':
            return (
                <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                        d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
            );
        default:
            return (
                <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                        d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
            );
    }
}

/**
 * Get severity level for styling
 */
function getErrorSeverity(code: AuthErrorCode): 'warning' | 'error' | 'info' {
    switch (code) {
        case 'TOKEN_REUSE_DETECTED':
        case 'ACCOUNT_DEACTIVATED':
            return 'error';
        case 'TOO_MANY_ATTEMPTS':
        case 'EMAIL_NOT_VERIFIED':
            return 'warning';
        default:
            return 'info';
    }
}

/**
 * Render authentication errors with proper styling
 */
export const AuthErrorRenderer: React.FC<AuthErrorRendererProps> = ({
    error,
    onDismiss,
    className = '',
}) => {
    const { t } = useTranslation();

    if (!error) return null;

    const severity = getErrorSeverity(error.code);
    const icon = getErrorIcon(error.code);

    // Get translated message or fall back to error message
    const message = t(`auth.errors.${error.code}`, { defaultValue: error.message });

    // Severity-based styles
    const severityStyles = {
        error: 'bg-red-50 border-red-200 text-red-800',
        warning: 'bg-amber-50 border-amber-200 text-amber-800',
        info: 'bg-blue-50 border-blue-200 text-blue-800',
    };

    const iconStyles = {
        error: 'text-red-500',
        warning: 'text-amber-500',
        info: 'text-blue-500',
    };

    return (
        <div
            className={`
                relative flex items-start gap-3 p-4 rounded-lg border
                animate-fade-in
                ${severityStyles[severity]}
                ${className}
            `}
            role="alert"
            aria-live="polite"
        >
            <div className={`flex-shrink-0 mt-0.5 ${iconStyles[severity]}`}>
                {icon}
            </div>

            <div className="flex-1 min-w-0">
                <p className="text-sm font-medium">{message}</p>

                {/* Show retry-after for rate limiting */}
                {error.retryAfter && (
                    <p className="text-xs mt-1 opacity-75">
                        {t('auth.errors.retryAfter', {
                            seconds: error.retryAfter,
                            defaultValue: `Please try again in ${error.retryAfter} seconds.`
                        })}
                    </p>
                )}

                {/* Security message for token reuse */}
                {error.code === 'TOKEN_REUSE_DETECTED' && (
                    <p className="text-xs mt-2 font-medium">
                        {t('auth.errors.securityAlert', {
                            defaultValue: 'For your security, please log in again.'
                        })}
                    </p>
                )}
            </div>

            {onDismiss && (
                <button
                    onClick={onDismiss}
                    className={`
                        flex-shrink-0 p-1 rounded-full
                        hover:bg-black/5 transition-colors
                        focus:outline-none focus:ring-2 focus:ring-offset-2
                        ${severity === 'error' ? 'focus:ring-red-500' : ''}
                        ${severity === 'warning' ? 'focus:ring-amber-500' : ''}
                        ${severity === 'info' ? 'focus:ring-blue-500' : ''}
                    `}
                    aria-label={t('common.close')}
                >
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                    </svg>
                </button>
            )}
        </div>
    );
};

export default AuthErrorRenderer;

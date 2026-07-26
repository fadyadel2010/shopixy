/**
 * Order Error Component
 * 
 * Error states for order pages with friendly messages
 * and appropriate actions based on error type.
 * 
 * Phase 7.8: Order Confirmation & Tracking UI
 */

import React from 'react';
import type { OrderError, OrderErrorCode } from '@/types/order';

// ============================================================================
// Types
// ============================================================================

interface OrderErrorDisplayProps {
    error: OrderError;
    onRetry?: () => void;
    onGoHome?: () => void;
    className?: string;
}

// ============================================================================
// Error Icons
// ============================================================================

const ErrorIcons: Record<OrderErrorCode, React.ReactNode> = {
    ORDER_NOT_FOUND: (
        <svg className="w-16 h-16" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={1.5}
                d="M9.172 16.172a4 4 0 015.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
            />
        </svg>
    ),
    INVALID_ORDER_ID: (
        <svg className="w-16 h-16" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={1.5}
                d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"
            />
        </svg>
    ),
    SESSION_MISMATCH: (
        <svg className="w-16 h-16" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={1.5}
                d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z"
            />
        </svg>
    ),
    NETWORK_ERROR: (
        <svg className="w-16 h-16" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={1.5}
                d="M8.111 16.404a5.5 5.5 0 017.778 0M12 20h.01m-7.08-7.071c3.904-3.905 10.236-3.905 14.141 0M1.394 9.393c5.857-5.857 15.355-5.857 21.213 0"
            />
        </svg>
    ),
    UNKNOWN_ERROR: (
        <svg className="w-16 h-16" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={1.5}
                d="M9.75 9.75l4.5 4.5m0-4.5l-4.5 4.5M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
            />
        </svg>
    ),
};

// ============================================================================
// Error Titles
// ============================================================================

const ErrorTitles: Record<OrderErrorCode, string> = {
    ORDER_NOT_FOUND: 'Order Not Found',
    INVALID_ORDER_ID: 'Invalid Order ID',
    SESSION_MISMATCH: 'Access Denied',
    NETWORK_ERROR: 'Connection Problem',
    UNKNOWN_ERROR: 'Something Went Wrong',
};

// ============================================================================
// Main Component
// ============================================================================

export const OrderErrorDisplay: React.FC<OrderErrorDisplayProps> = ({
    error,
    onRetry,
    onGoHome,
    className = '',
}) => {
    const icon = ErrorIcons[error.code] || ErrorIcons.UNKNOWN_ERROR;
    const title = ErrorTitles[error.code] || ErrorTitles.UNKNOWN_ERROR;

    // Navigate home
    const handleGoHome = () => {
        if (onGoHome) {
            onGoHome();
        } else {
            window.location.href = '/';
        }
    };

    return (
        <div
            className={`
                min-h-[400px] flex items-center justify-center
                ${className}
            `}
        >
            <div className="text-center px-4 py-8 max-w-md">
                {/* Icon */}
                <div className="mb-6 text-muted/40">
                    {icon}
                </div>

                {/* Title */}
                <h1
                    className="text-2xl font-bold text-text mb-3"
                    id="error-heading"
                    tabIndex={-1}
                >
                    {title}
                </h1>

                {/* Message */}
                <p className="text-muted mb-8 leading-relaxed">
                    {error.message}
                </p>

                {/* Actions */}
                <div className="flex flex-col sm:flex-row gap-3 justify-center">
                    {error.retryable && onRetry && (
                        <button
                            onClick={onRetry}
                            className="
                                py-3 px-6 rounded-xl font-medium
                                bg-primary text-white
                                hover:bg-primary/90
                                transition-colors
                                focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                            "
                        >
                            Try Again
                        </button>
                    )}

                    <button
                        onClick={handleGoHome}
                        className={`
                            py-3 px-6 rounded-xl font-medium
                            transition-colors
                            focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                            ${error.retryable && onRetry
                                ? 'border border-muted/20 text-text hover:bg-muted/10'
                                : 'bg-primary text-white hover:bg-primary/90'
                            }
                        `}
                    >
                        Back to Home
                    </button>
                </div>

                {/* Help Text */}
                <p className="mt-8 text-sm text-muted">
                    Need help?{' '}
                    <a
                        href="/contact"
                        className="text-primary hover:underline"
                    >
                        Contact our support team
                    </a>
                </p>
            </div>
        </div>
    );
};

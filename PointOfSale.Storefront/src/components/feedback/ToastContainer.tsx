/**
 * Toast Container Component
 * 
 * Renders toast notifications in a fixed position.
 * Accessible with ARIA live regions.
 * Respects reduced motion preferences.
 */

import React from 'react';
import { useToastStore, selectToasts, type Toast, type ToastType } from '@/store/toastStore';

/**
 * Toast icon by type
 */
const ToastIcon: React.FC<{ type: ToastType }> = ({ type }) => {
    switch (type) {
        case 'success':
            return (
                <svg className="w-5 h-5 text-green-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                </svg>
            );
        case 'error':
            return (
                <svg className="w-5 h-5 text-red-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                </svg>
            );
        case 'warning':
            return (
                <svg className="w-5 h-5 text-amber-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                </svg>
            );
        case 'info':
        default:
            return (
                <svg className="w-5 h-5 text-blue-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
            );
    }
};

/**
 * Toast background styles by type
 */
const getToastStyles = (type: ToastType): string => {
    switch (type) {
        case 'success':
            return 'bg-green-50 border-green-200 text-green-800';
        case 'error':
            return 'bg-red-50 border-red-200 text-red-800';
        case 'warning':
            return 'bg-amber-50 border-amber-200 text-amber-800';
        case 'info':
        default:
            return 'bg-blue-50 border-blue-200 text-blue-800';
    }
};

/**
 * Single Toast Item
 */
const ToastItem: React.FC<{ toast: Toast; onDismiss: () => void }> = ({ toast, onDismiss }) => {
    return (
        <div
            role="alert"
            aria-live="polite"
            className={`
                flex items-center gap-3 px-4 py-3 rounded-lg border shadow-lg
                animate-slide-in-right
                motion-reduce:animate-none
                ${getToastStyles(toast.type)}
            `}
        >
            <ToastIcon type={toast.type} />
            <p className="flex-1 text-sm font-medium">{toast.message}</p>
            <button
                onClick={onDismiss}
                className="flex-shrink-0 p-1 rounded-full hover:bg-black/5 transition-colors"
                aria-label="Dismiss notification"
            >
                <svg className="w-4 h-4 opacity-60" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                </svg>
            </button>
        </div>
    );
};

/**
 * Toast Container
 * 
 * Renders all active toasts in a fixed position at bottom-right.
 */
export const ToastContainer: React.FC = () => {
    const toasts = useToastStore(selectToasts);
    const removeToast = useToastStore((state) => state.removeToast);

    if (toasts.length === 0) {
        return null;
    }

    return (
        <div
            className="fixed bottom-4 right-4 z-50 flex flex-col gap-2 max-w-sm w-full pointer-events-none"
            aria-label="Notifications"
        >
            {toasts.map((toast) => (
                <div key={toast.id} className="pointer-events-auto">
                    <ToastItem
                        toast={toast}
                        onDismiss={() => removeToast(toast.id)}
                    />
                </div>
            ))}
        </div>
    );
};

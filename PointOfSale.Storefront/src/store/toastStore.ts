/**
 * Toast Store (Zustand)
 * 
 * Centralized toast/snackbar notification system.
 * Non-blocking, auto-dismiss, accessible.
 * 
 * Used by cart store actions for user feedback.
 */

import { create } from 'zustand';

/**
 * Toast types
 */
export type ToastType = 'success' | 'error' | 'info' | 'warning';

/**
 * Toast item
 */
export interface Toast {
    id: string;
    type: ToastType;
    message: string;
    duration?: number;
}

/**
 * Toast store state
 */
interface ToastState {
    toasts: Toast[];
}

/**
 * Toast store actions
 */
interface ToastActions {
    /** Add a toast */
    addToast: (toast: Omit<Toast, 'id'>) => string;
    /** Remove a toast by ID */
    removeToast: (id: string) => void;
    /** Clear all toasts */
    clearToasts: () => void;
    /** Success toast shorthand */
    success: (message: string, duration?: number) => string;
    /** Error toast shorthand */
    error: (message: string, duration?: number) => string;
    /** Info toast shorthand */
    info: (message: string, duration?: number) => string;
    /** Warning toast shorthand */
    warning: (message: string, duration?: number) => string;
}

/**
 * Generate unique ID
 */
let toastIdCounter = 0;
const generateId = (): string => {
    toastIdCounter += 1;
    return `toast-${toastIdCounter}-${Date.now()}`;
};

/**
 * Default durations by type (ms)
 */
const DEFAULT_DURATIONS: Record<ToastType, number> = {
    success: 3000,
    error: 5000,
    info: 4000,
    warning: 4000,
};

/**
 * Maximum concurrent toasts
 */
const MAX_TOASTS = 5;

/**
 * Toast store
 */
export const useToastStore = create<ToastState & ToastActions>((set, get) => ({
    toasts: [],

    addToast: (toast) => {
        const id = generateId();
        const duration = toast.duration ?? DEFAULT_DURATIONS[toast.type];

        set((state) => {
            // Limit concurrent toasts
            const newToasts = [...state.toasts, { ...toast, id, duration }];
            if (newToasts.length > MAX_TOASTS) {
                newToasts.shift();
            }
            return { toasts: newToasts };
        });

        // Auto-dismiss
        if (duration > 0) {
            setTimeout(() => {
                get().removeToast(id);
            }, duration);
        }

        return id;
    },

    removeToast: (id) => {
        set((state) => ({
            toasts: state.toasts.filter((t) => t.id !== id),
        }));
    },

    clearToasts: () => {
        set({ toasts: [] });
    },

    success: (message, duration) => {
        return get().addToast({ type: 'success', message, duration });
    },

    error: (message, duration) => {
        return get().addToast({ type: 'error', message, duration });
    },

    info: (message, duration) => {
        return get().addToast({ type: 'info', message, duration });
    },

    warning: (message, duration) => {
        return get().addToast({ type: 'warning', message, duration });
    },
}));

// ============================================================
// Hook for components
// ============================================================

/**
 * Hook to access toast functions
 */
export function useToast() {
    const addToast = useToastStore((state) => state.addToast);
    const removeToast = useToastStore((state) => state.removeToast);
    const clearToasts = useToastStore((state) => state.clearToasts);
    const success = useToastStore((state) => state.success);
    const error = useToastStore((state) => state.error);
    const info = useToastStore((state) => state.info);
    const warning = useToastStore((state) => state.warning);

    return {
        addToast,
        removeToast,
        clearToasts,
        success,
        error,
        info,
        warning,
    };
}

// ============================================================
// Selectors
// ============================================================

export const selectToasts = (state: ToastState) => state.toasts;

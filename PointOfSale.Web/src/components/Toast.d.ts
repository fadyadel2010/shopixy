import type { ReactElement, ReactNode } from 'react';
export function ToastProvider(props: { children: ReactNode }): ReactElement;
export function useToast(): {
    showSuccess(message: ReactNode, duration?: number): void;
    showError(message: ReactNode, duration?: number): void;
    showInfo(message: ReactNode, duration?: number): void;
    showWarning(message: ReactNode, duration?: number): void;
};

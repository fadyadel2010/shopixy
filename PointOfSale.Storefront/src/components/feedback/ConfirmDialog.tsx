/**
 * Confirm Dialog Component
 * 
 * Lightweight confirmation dialog for destructive actions.
 * Inline/inline-modal approach - not a full modal system.
 * 
 * Accessibility:
 * - Focus trap while open
 * - ESC key closes dialog
 * - Focus restored on close
 */

import React, { useEffect, useRef, useCallback } from 'react';
import { Button } from '@/components/common';

interface ConfirmDialogProps {
    /** Whether the dialog is open */
    isOpen: boolean;
    /** Title text */
    title: string;
    /** Message text */
    message: string;
    /** Confirm button text */
    confirmText?: string;
    /** Cancel button text */
    cancelText?: string;
    /** Confirm variant */
    confirmVariant?: 'primary' | 'secondary';
    /** Whether confirming is in progress */
    isLoading?: boolean;
    /** Confirm callback */
    onConfirm: () => void;
    /** Cancel callback */
    onCancel: () => void;
}

/**
 * Confirm Dialog Component
 */
export const ConfirmDialog: React.FC<ConfirmDialogProps> = ({
    isOpen,
    title,
    message,
    confirmText = 'Confirm',
    cancelText = 'Cancel',
    confirmVariant = 'primary',
    isLoading = false,
    onConfirm,
    onCancel,
}) => {
    const dialogRef = useRef<HTMLDivElement>(null);
    const previouslyFocusedRef = useRef<HTMLElement | null>(null);

    // Store previously focused element when opening
    useEffect(() => {
        if (isOpen) {
            previouslyFocusedRef.current = document.activeElement as HTMLElement;
        }
    }, [isOpen]);

    // Focus trap and keyboard handling
    useEffect(() => {
        if (!isOpen || !dialogRef.current) return;

        const dialog = dialogRef.current;
        const focusableElements = dialog.querySelectorAll<HTMLElement>(
            'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
        );
        const firstElement = focusableElements[0];
        const lastElement = focusableElements[focusableElements.length - 1];

        // Focus first focusable element
        firstElement?.focus();

        const handleKeyDown = (e: KeyboardEvent) => {
            // ESC key closes dialog
            if (e.key === 'Escape' && !isLoading) {
                e.preventDefault();
                onCancel();
                return;
            }

            // Tab key trap
            if (e.key === 'Tab') {
                if (e.shiftKey) {
                    if (document.activeElement === firstElement) {
                        e.preventDefault();
                        lastElement?.focus();
                    }
                } else {
                    if (document.activeElement === lastElement) {
                        e.preventDefault();
                        firstElement?.focus();
                    }
                }
            }
        };

        document.addEventListener('keydown', handleKeyDown);

        return () => {
            document.removeEventListener('keydown', handleKeyDown);
            // Restore focus to previously focused element
            previouslyFocusedRef.current?.focus();
        };
    }, [isOpen, isLoading, onCancel]);

    // Stable cancel handler for backdrop click
    const handleBackdropClick = useCallback(() => {
        if (!isLoading) {
            onCancel();
        }
    }, [isLoading, onCancel]);

    if (!isOpen) {
        return null;
    }

    return (
        <>
            {/* Backdrop */}
            <div
                className="fixed inset-0 bg-black/40 z-40 animate-fade-in motion-reduce:animate-none"
                onClick={handleBackdropClick}
                aria-hidden="true"
            />

            {/* Dialog */}
            <div
                className="fixed inset-0 z-50 flex items-end sm:items-center justify-center p-4"
                role="dialog"
                aria-modal="true"
                aria-labelledby="confirm-dialog-title"
                aria-describedby="confirm-dialog-message"
            >
                <div
                    ref={dialogRef}
                    className="bg-background rounded-t-xl sm:rounded-xl shadow-xl w-full max-w-sm animate-slide-in-up sm:animate-scale-in motion-reduce:animate-none"
                    onClick={(e) => e.stopPropagation()}
                >
                    <div className="p-6">
                        {/* Title */}
                        <h3
                            id="confirm-dialog-title"
                            className="text-lg font-semibold text-text mb-2"
                        >
                            {title}
                        </h3>

                        {/* Message */}
                        <p id="confirm-dialog-message" className="text-muted text-sm mb-6">
                            {message}
                        </p>

                        {/* Actions */}
                        <div className="flex gap-3">
                            <Button
                                variant="secondary"
                                size="md"
                                fullWidth
                                onClick={onCancel}
                                disabled={isLoading}
                            >
                                {cancelText}
                            </Button>
                            <Button
                                variant={confirmVariant}
                                size="md"
                                fullWidth
                                onClick={onConfirm}
                                loading={isLoading}
                            >
                                {confirmText}
                            </Button>
                        </div>
                    </div>

                    {/* Mobile bottom sheet handle */}
                    <div className="sm:hidden w-12 h-1 bg-muted/30 rounded-full mx-auto mb-2" />
                </div>
            </div>
        </>
    );
};


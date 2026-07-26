/**
 * useKeyboardShortcut Hook
 * 
 * Provides keyboard shortcut functionality for:
 * - Cmd/Ctrl + K → Focus search input
 * - Escape → Clear/blur search
 * - Enter → Force immediate search
 * 
 * Phase 7.6.1: Search UX Enhancements
 */

import { useEffect, useCallback, type RefObject } from 'react';

/**
 * Keyboard shortcut configuration
 */
export interface KeyboardShortcut {
    /** Key to listen for */
    key: string;
    /** Require Ctrl/Cmd modifier */
    ctrlOrCmd?: boolean;
    /** Require Shift modifier */
    shift?: boolean;
    /** Callback when shortcut is triggered */
    callback: (event: KeyboardEvent) => void;
    /** Only trigger when input is focused */
    whenFocused?: boolean;
    /** Only trigger when input is NOT focused */
    whenNotFocused?: boolean;
}

/**
 * Check if event matches the shortcut
 */
function matchesShortcut(event: KeyboardEvent, shortcut: KeyboardShortcut): boolean {
    // Check key match (case insensitive)
    if (event.key.toLowerCase() !== shortcut.key.toLowerCase()) {
        return false;
    }

    // Check Ctrl/Cmd modifier
    if (shortcut.ctrlOrCmd) {
        // Mac: metaKey (Cmd), Windows/Linux: ctrlKey
        const hasModifier = event.metaKey || event.ctrlKey;
        if (!hasModifier) return false;
    } else {
        // If ctrlOrCmd not required, make sure it's not pressed
        if (event.metaKey || event.ctrlKey) return false;
    }

    // Check Shift modifier
    if (shortcut.shift && !event.shiftKey) return false;
    if (!shortcut.shift && event.shiftKey) return false;

    return true;
}

/**
 * Hook for global keyboard shortcuts
 * 
 * @param shortcuts - Array of keyboard shortcut configurations
 * @param enabled - Whether shortcuts are enabled (default: true)
 */
export function useKeyboardShortcuts(
    shortcuts: KeyboardShortcut[],
    enabled: boolean = true
): void {
    useEffect(() => {
        if (!enabled || shortcuts.length === 0) return;

        const handleKeyDown = (event: KeyboardEvent) => {
            // Don't trigger in contenteditable elements
            const target = event.target as HTMLElement;
            if (target.isContentEditable) return;

            // Check if we're in an input/textarea
            const isInInput = target.tagName === 'INPUT' || target.tagName === 'TEXTAREA';

            for (const shortcut of shortcuts) {
                // Skip if focus requirements don't match
                if (shortcut.whenFocused && !isInInput) continue;
                if (shortcut.whenNotFocused && isInInput) continue;

                if (matchesShortcut(event, shortcut)) {
                    event.preventDefault();
                    event.stopPropagation();
                    shortcut.callback(event);
                    break;
                }
            }
        };

        document.addEventListener('keydown', handleKeyDown);
        return () => document.removeEventListener('keydown', handleKeyDown);
    }, [shortcuts, enabled]);
}

/**
 * Hook for search-specific keyboard shortcuts
 * 
 * Provides:
 * - Cmd/Ctrl + K → Focus search
 * - Escape → Clear search when focused
 * - Enter → Force search (bypasses debounce)
 * 
 * @param inputRef - Ref to the search input element
 * @param onClear - Callback to clear search
 * @param onForceSearch - Callback to force immediate search
 * @param enabled - Whether shortcuts are enabled
 */
export function useSearchKeyboardShortcuts(
    inputRef: RefObject<HTMLInputElement | null>,
    onClear: () => void,
    onForceSearch: () => void,
    enabled: boolean = true
): void {
    // Cmd/Ctrl + K → Focus search
    const focusSearch = useCallback(() => {
        inputRef.current?.focus();
        inputRef.current?.select();
    }, [inputRef]);

    // Escape → Clear and blur
    const handleEscape = useCallback(() => {
        if (document.activeElement === inputRef.current) {
            onClear();
            inputRef.current?.blur();
        }
    }, [inputRef, onClear]);

    // Enter → Force search
    const handleEnter = useCallback(() => {
        if (document.activeElement === inputRef.current) {
            onForceSearch();
        }
    }, [inputRef, onForceSearch]);

    useKeyboardShortcuts([
        {
            key: 'k',
            ctrlOrCmd: true,
            callback: focusSearch,
        },
        {
            key: 'Escape',
            callback: handleEscape,
            whenFocused: true,
        },
        {
            key: 'Enter',
            callback: handleEnter,
            whenFocused: true,
        },
    ], enabled);
}

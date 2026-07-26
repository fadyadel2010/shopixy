/**
 * useScrollPosition Hook
 * 
 * Tracks scroll position with debouncing for scroll-aware UI.
 * Used for header elevation changes and scroll direction detection.
 * 
 * Respects prefers-reduced-motion.
 */

import { useState, useEffect, useCallback, useRef } from 'react';

export interface ScrollPosition {
    /** Current scroll Y position */
    y: number;
    /** Previous scroll Y position */
    prevY: number;
    /** Scroll direction: 'up' | 'down' | 'none' */
    direction: 'up' | 'down' | 'none';
    /** Whether page is scrolled beyond threshold */
    isScrolled: boolean;
    /** Whether user is actively scrolling */
    isScrolling: boolean;
}

interface UseScrollPositionOptions {
    /** Scroll threshold in pixels before isScrolled is true (default: 10) */
    threshold?: number;
    /** Debounce delay for performance (default: 10ms) */
    debounceMs?: number;
    /** Whether to track scroll direction (adds overhead) */
    trackDirection?: boolean;
}

/**
 * Check if user prefers reduced motion
 */
function prefersReducedMotion(): boolean {
    if (typeof window === 'undefined') return false;
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/**
 * Hook for tracking scroll position
 */
export function useScrollPosition(options: UseScrollPositionOptions = {}): ScrollPosition {
    const {
        threshold = 10,
        debounceMs = 10,
        trackDirection = true,
    } = options;

    const [scrollPosition, setScrollPosition] = useState<ScrollPosition>({
        y: 0,
        prevY: 0,
        direction: 'none',
        isScrolled: false,
        isScrolling: false,
    });

    const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const scrollingTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const lastYRef = useRef(0);

    const handleScroll = useCallback(() => {
        const currentY = window.scrollY;
        const prevY = lastYRef.current;

        // Determine direction
        let direction: 'up' | 'down' | 'none' = 'none';
        if (trackDirection) {
            if (currentY > prevY + 5) {
                direction = 'down';
            } else if (currentY < prevY - 5) {
                direction = 'up';
            }
        }

        lastYRef.current = currentY;

        setScrollPosition({
            y: currentY,
            prevY,
            direction,
            isScrolled: currentY > threshold,
            isScrolling: true,
        });

        // Clear scrolling state after user stops scrolling
        if (scrollingTimeoutRef.current) {
            clearTimeout(scrollingTimeoutRef.current);
        }
        scrollingTimeoutRef.current = setTimeout(() => {
            setScrollPosition(prev => ({ ...prev, isScrolling: false }));
        }, 150);
    }, [threshold, trackDirection]);

    useEffect(() => {
        // Skip heavy scroll tracking if user prefers reduced motion
        const reducedMotion = prefersReducedMotion();

        const debouncedHandler = () => {
            if (timeoutRef.current) {
                clearTimeout(timeoutRef.current);
            }
            timeoutRef.current = setTimeout(handleScroll, reducedMotion ? 50 : debounceMs);
        };

        // Initial position
        handleScroll();

        window.addEventListener('scroll', debouncedHandler, { passive: true });

        return () => {
            window.removeEventListener('scroll', debouncedHandler);
            if (timeoutRef.current) {
                clearTimeout(timeoutRef.current);
            }
            if (scrollingTimeoutRef.current) {
                clearTimeout(scrollingTimeoutRef.current);
            }
        };
    }, [handleScroll, debounceMs]);

    return scrollPosition;
}

/**
 * Hook for body scroll lock (used with mobile menu)
 */
export function useBodyScrollLock(isLocked: boolean): void {
    useEffect(() => {
        if (!isLocked) return;

        // Get current scroll position
        const scrollY = window.scrollY;

        // Lock body
        document.body.style.position = 'fixed';
        document.body.style.top = `-${scrollY}px`;
        document.body.style.left = '0';
        document.body.style.right = '0';
        document.body.style.overflow = 'hidden';

        return () => {
            // Unlock body
            document.body.style.position = '';
            document.body.style.top = '';
            document.body.style.left = '';
            document.body.style.right = '';
            document.body.style.overflow = '';
            // Restore scroll position
            window.scrollTo(0, scrollY);
        };
    }, [isLocked]);
}

/**
 * Hook for focus trap (used with modal dialogs and mobile menu)
 */
export function useFocusTrap(
    containerRef: React.RefObject<HTMLElement | null>,
    isActive: boolean
): void {
    useEffect(() => {
        if (!isActive || !containerRef.current) return;

        const container = containerRef.current;
        const focusableElements = container.querySelectorAll<HTMLElement>(
            'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
        );
        const firstElement = focusableElements[0];
        const lastElement = focusableElements[focusableElements.length - 1];

        // Focus first element
        const previouslyFocused = document.activeElement as HTMLElement;
        firstElement?.focus();

        const handleKeyDown = (e: KeyboardEvent) => {
            if (e.key !== 'Tab') return;

            if (e.shiftKey) {
                // Shift + Tab
                if (document.activeElement === firstElement) {
                    e.preventDefault();
                    lastElement?.focus();
                }
            } else {
                // Tab
                if (document.activeElement === lastElement) {
                    e.preventDefault();
                    firstElement?.focus();
                }
            }
        };

        container.addEventListener('keydown', handleKeyDown);

        return () => {
            container.removeEventListener('keydown', handleKeyDown);
            // Restore focus
            previouslyFocused?.focus();
        };
    }, [containerRef, isActive]);
}

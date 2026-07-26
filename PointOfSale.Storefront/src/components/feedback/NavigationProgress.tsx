/**
 * Navigation Progress Bar Component
 * 
 * Lightweight top loading bar for route transitions (like YouTube/Shopify).
 * Never blocks UI, automatically hides on completion or error.
 * 
 * Uses CSS animations only, no heavy JS animation libraries.
 */

import React, { useEffect, useState, useRef } from 'react';

interface NavigationProgressProps {
    /** Whether loading is in progress */
    isLoading: boolean;
    /** Primary color (uses CSS variable by default) */
    color?: string;
    /** Height in pixels */
    height?: number;
}

/**
 * Navigation Progress Bar
 */
export const NavigationProgress: React.FC<NavigationProgressProps> = ({
    isLoading,
    color,
    height = 3,
}) => {
    const [progress, setProgress] = useState(0);
    const [visible, setVisible] = useState(false);
    const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
    const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

    useEffect(() => {
        if (isLoading) {
            // Start progress
            setVisible(true);
            setProgress(0);

            // Simulate progress (fast start, slow middle)
            let current = 0;
            intervalRef.current = setInterval(() => {
                current += Math.random() * 10;
                if (current > 90) {
                    current = 90; // Cap at 90% until complete
                    if (intervalRef.current) {
                        clearInterval(intervalRef.current);
                    }
                }
                setProgress(current);
            }, 200);
        } else if (visible) {
            // Complete progress
            if (intervalRef.current) {
                clearInterval(intervalRef.current);
            }
            setProgress(100);

            // Hide after animation completes
            timeoutRef.current = setTimeout(() => {
                setVisible(false);
                setProgress(0);
            }, 300);
        }

        return () => {
            if (intervalRef.current) {
                clearInterval(intervalRef.current);
            }
            if (timeoutRef.current) {
                clearTimeout(timeoutRef.current);
            }
        };
    }, [isLoading, visible]);

    if (!visible) return null;

    return (
        <div
            className="fixed top-0 left-0 right-0 z-50 pointer-events-none"
            style={{ height }}
            role="progressbar"
            aria-valuenow={Math.round(progress)}
            aria-valuemin={0}
            aria-valuemax={100}
            aria-label="Page loading"
        >
            <div
                className="h-full transition-all duration-200 ease-out"
                style={{
                    width: `${progress}%`,
                    backgroundColor: color || 'var(--color-primary)',
                    boxShadow: `0 0 10px ${color || 'var(--color-primary)'}`,
                }}
            />
        </div>
    );
};

/**
 * Hook for tracking navigation loading state
 */
export function useNavigationProgress(): {
    isNavigating: boolean;
    startNavigation: () => void;
    endNavigation: () => void;
} {
    const [isNavigating, setIsNavigating] = useState(false);

    return {
        isNavigating,
        startNavigation: () => setIsNavigating(true),
        endNavigation: () => setIsNavigating(false),
    };
}

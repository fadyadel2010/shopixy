/**
 * Promotion Countdown Hook
 * 
 * PHASE: Promotions Engine - Countdown Timers
 * 
 * Provides countdown functionality for promotions with end dates.
 * Uses server-provided end date to avoid client-side clock drift issues.
 * 
 * Features:
 * - Automatically updates every second
 * - Returns formatted time remaining
 * - Returns isExpired when countdown reaches zero
 * - Cleans up interval on unmount
 */

import { useState, useEffect, useCallback, useMemo } from 'react';

interface CountdownResult {
    /** Formatted time remaining (e.g., "2d 5h 30m") */
    timeRemaining: string;
    /** Whether the promotion has expired */
    isExpired: boolean;
    /** Total seconds remaining */
    secondsRemaining: number;
    /** Individual time components */
    days: number;
    hours: number;
    minutes: number;
    seconds: number;
}

/**
 * Hook for countdown timer functionality
 * @param endDate - ISO date string or Date object for when the countdown ends
 * @returns CountdownResult with formatted time and state
 */
export function usePromotionCountdown(endDate: string | Date | undefined | null): CountdownResult {
    const endTimeMs = useMemo(() => {
        if (!endDate) return 0;
        return new Date(endDate).getTime();
    }, [endDate]);

    const calculateTimeRemaining = useCallback((): CountdownResult => {
        if (!endTimeMs) {
            return {
                timeRemaining: '',
                isExpired: true,
                secondsRemaining: 0,
                days: 0,
                hours: 0,
                minutes: 0,
                seconds: 0,
            };
        }

        const now = Date.now();
        const diff = endTimeMs - now;

        if (diff <= 0) {
            return {
                timeRemaining: 'Expired',
                isExpired: true,
                secondsRemaining: 0,
                days: 0,
                hours: 0,
                minutes: 0,
                seconds: 0,
            };
        }

        const totalSeconds = Math.floor(diff / 1000);
        const days = Math.floor(totalSeconds / (60 * 60 * 24));
        const hours = Math.floor((totalSeconds % (60 * 60 * 24)) / (60 * 60));
        const minutes = Math.floor((totalSeconds % (60 * 60)) / 60);
        const seconds = totalSeconds % 60;

        // Format the time string based on remaining time
        let timeRemaining = '';
        if (days > 0) {
            timeRemaining = `${days}d ${hours}h ${minutes}m`;
        } else if (hours > 0) {
            timeRemaining = `${hours}h ${minutes}m ${seconds}s`;
        } else if (minutes > 0) {
            timeRemaining = `${minutes}m ${seconds}s`;
        } else {
            timeRemaining = `${seconds}s`;
        }

        return {
            timeRemaining,
            isExpired: false,
            secondsRemaining: totalSeconds,
            days,
            hours,
            minutes,
            seconds,
        };
    }, [endTimeMs]);

    const [result, setResult] = useState<CountdownResult>(calculateTimeRemaining);

    useEffect(() => {
        if (!endTimeMs) return;

        // Update immediately
        setResult(calculateTimeRemaining());

        // Update every second
        const interval = setInterval(() => {
            const newResult = calculateTimeRemaining();
            setResult(newResult);

            // Stop interval if expired
            if (newResult.isExpired) {
                clearInterval(interval);
            }
        }, 1000);

        return () => clearInterval(interval);
    }, [endTimeMs, calculateTimeRemaining]);

    return result;
}

export default usePromotionCountdown;

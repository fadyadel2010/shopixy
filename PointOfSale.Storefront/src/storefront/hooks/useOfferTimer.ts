import { useEffect } from 'react';

interface UseOfferTimerOptions {
    enabled: boolean;
    delaySeconds: number | null;
    onTrigger: () => void;
}

export function useOfferTimer({ enabled, delaySeconds, onTrigger }: UseOfferTimerOptions) {
    useEffect(() => {
        if (!enabled || delaySeconds === null) {
            return;
        }

        if (delaySeconds <= 0) {
            onTrigger();
            return;
        }

        const timeoutId = window.setTimeout(() => {
            onTrigger();
        }, delaySeconds * 1000);

        return () => {
            window.clearTimeout(timeoutId);
        };
    }, [delaySeconds, enabled, onTrigger]);
}

import { useEffect, useRef } from 'react';

interface UseScrollTriggerOptions {
    enabled: boolean;
    onTrigger: (scrollPercent: number) => void;
}

export function useScrollTrigger({ enabled, onTrigger }: UseScrollTriggerOptions) {
    const maxTriggeredPercent = useRef(0);

    useEffect(() => {
        if (!enabled) {
            maxTriggeredPercent.current = 0;
            return;
        }

        const measureScrollPercent = () => {
            const scrollTop = window.scrollY;
            const pageHeight = document.documentElement.scrollHeight - window.innerHeight;

            if (pageHeight <= 0) {
                return 0;
            }

            return Math.round((scrollTop / pageHeight) * 100);
        };

        const handleScroll = () => {
            const currentPercent = measureScrollPercent();
            if (currentPercent <= maxTriggeredPercent.current) {
                return;
            }

            maxTriggeredPercent.current = currentPercent;
            onTrigger(currentPercent);
        };

        window.addEventListener('scroll', handleScroll, { passive: true });
        handleScroll();

        return () => {
            window.removeEventListener('scroll', handleScroll);
        };
    }, [enabled, onTrigger]);
}

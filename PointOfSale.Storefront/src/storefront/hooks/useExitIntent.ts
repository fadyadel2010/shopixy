import { useEffect, useRef } from 'react';

interface UseExitIntentOptions {
    enabled: boolean;
    onTrigger: () => void;
}

export function useExitIntent({ enabled, onTrigger }: UseExitIntentOptions) {
    const triggeredRef = useRef(false);

    useEffect(() => {
        if (!enabled) {
            triggeredRef.current = false;
            return;
        }

        const trigger = () => {
            if (triggeredRef.current) {
                return;
            }

            triggeredRef.current = true;
            onTrigger();
        };

        const handleMouseLeave = (event: MouseEvent) => {
            if (event.clientY < 10) {
                trigger();
            }
        };

        const handleVisibilityChange = () => {
            if (document.visibilityState === 'hidden') {
                trigger();
            }
        };

        document.addEventListener('mouseleave', handleMouseLeave);
        document.addEventListener('visibilitychange', handleVisibilityChange);

        return () => {
            document.removeEventListener('mouseleave', handleMouseLeave);
            document.removeEventListener('visibilitychange', handleVisibilityChange);
        };
    }, [enabled, onTrigger]);
}

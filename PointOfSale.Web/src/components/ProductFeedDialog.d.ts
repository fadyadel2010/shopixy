import type { ReactElement, RefObject } from 'react';
export default function ProductFeedDialog(props: {
    open: boolean; onOpenChange(open: boolean): void; sessionFeedUrls?: Record<string, string>;
    onSessionFeedUrlChange?(channel: string, url: string | null): void; restoreFocusRef?: RefObject<HTMLElement | null> | null;
}): ReactElement;

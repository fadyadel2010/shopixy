import type { ReactElement } from 'react';
export default function ProductMediaManager(props: {
    productId?: string | null; onMediaChanged?(headers?: Record<string, string>): void;
}): ReactElement | null;

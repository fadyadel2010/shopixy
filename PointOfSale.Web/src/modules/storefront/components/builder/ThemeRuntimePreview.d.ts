import type { ReactElement } from 'react';
export function ThemeRuntimePreview(props: {
    config: { tenantId?: string; previewChrome?: { runtimeUrl: string } | null };
    tenantId: string; language: string; viewportWidth?: number;
}): ReactElement;

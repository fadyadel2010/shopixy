import type { ReactElement } from 'react';
export default function ProductExportMenu(props: {
    onExport(format: 'excel' | 'pdf', scope: 'current-view' | 'selected'): void;
    selectedCount: number; available: boolean; exporting?: boolean | string | null;
}): ReactElement;

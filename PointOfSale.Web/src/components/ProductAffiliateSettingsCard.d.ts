import type { ReactElement } from 'react';
export default function ProductAffiliateSettingsCard(props: {
    productId?: string | null; onSettingsChanged?(): void | Promise<void>; onBusyChange?(busy: boolean): void;
    onDirtyChange?(dirty: boolean): void; onCancel?(): void;
}): ReactElement | null;

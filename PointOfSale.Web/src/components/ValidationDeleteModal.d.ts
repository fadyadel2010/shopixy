import type { ReactElement } from 'react';
export default function ValidationDeleteModal(props: {
    isOpen: boolean; onClose(): void; entityType: 'product' | 'customer' | 'supplier' | 'purchase' | 'sale';
    entityId: string; entityName: string; expectedVersion?: string | number;
    onDeleteSuccess?(): void; onDeactivateSuccess?(): void;
}): ReactElement | null;

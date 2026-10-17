import type { ReactElement } from 'react';
export interface InvoiceSearchResult { id: string; invoiceNumber: string; status?: string | number }
export default function InvoiceSearchModal(props: {
    type: 'sales' | 'purchases'; onClose(): void;
    onSelect(invoice: InvoiceSearchResult): void; onPrint?(invoice: InvoiceSearchResult): void;
}): ReactElement;

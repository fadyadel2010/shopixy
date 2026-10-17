export const canRewriteInvoice = (type: string, invoice: unknown): boolean => (
    type !== 'sales' || typeof invoice === 'object' && invoice !== null && 'status' in invoice &&
        (invoice.status === 'Draft' || invoice.status === 0)
)

export interface CheckoutAttachmentPresentation { attachmentId: string; fileName: string; contentType: string; sizeBytes: number; mediaType: string }
export interface CheckoutFieldSnapshot {
    fieldId: string; fieldKind?: string; fieldType?: string; isVisible?: boolean; displayOrder?: number; label?: string;
    valueJson?: string; configurationJson?: string; configuration?: { options?: { value: string; label: string }[] };
    submittedValue?: { kind?: string; displayValue?: string; attachments?: (Partial<CheckoutAttachmentPresentation> & { objectKey?: string; url?: string })[] } | null;
}
export function presentOrderCheckoutFields(snapshots?: CheckoutFieldSnapshot[]): { fieldId: string; label: string; displayValue: string; attachments: CheckoutAttachmentPresentation[] }[];

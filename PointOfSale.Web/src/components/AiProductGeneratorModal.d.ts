import type { ReactElement } from 'react';
export interface GeneratedProduct {
    name?: string; shortDescription?: string; slug?: string; description?: string; brand?: string; manufacturer?: string;
    tags?: string[]; metaTitle?: string; metaDescription?: string; metaKeywords?: string[]; features?: string[];
    faq?: { question: string; answer: string }[];
}
export default function AiProductGeneratorModal(props: { isOpen: boolean; onClose(): void; onApply(product: GeneratedProduct): void }): ReactElement | null;

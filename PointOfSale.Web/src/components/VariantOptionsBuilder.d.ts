import type { ReactElement } from 'react';
export interface VariantOptionGroup { id: string; name: string; values: string[] }
export default function VariantOptionsBuilder(props: {
    optionGroups: VariantOptionGroup[]; onChange(groups: VariantOptionGroup[]): void; maxOptions?: number;
    combinationCount?: number; risk?: { level: string; label: string; message: string };
    duplicateValueMap?: Record<string, string[]>; validationMessages?: string[];
    safeLimit?: number; infoLimit?: number; warningLimit?: number;
}): ReactElement;

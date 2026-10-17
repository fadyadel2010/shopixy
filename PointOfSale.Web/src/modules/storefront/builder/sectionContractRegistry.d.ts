import sectionAuthority from '@theme-authority/shopixy-theme-authority.json';
export type SectionContract = typeof sectionAuthority.sectionContracts[number];
export interface BuilderSettingsField {
    key: string; type: string; label: string; defaultValue: unknown;
    min?: number; max?: number; maxItems?: number; rows?: number;
    options?: { value: unknown; label: string }[];
    itemLabel?: string; defaultItem?: Record<string, unknown>; fields?: BuilderSettingsField[];
}
export const sectionContracts: typeof sectionAuthority.sectionContracts;
export function getSectionContract(sectionType: string): SectionContract | undefined;
export function getContractDefaultSettings(sectionType: string): Record<string, unknown>;
export function getContractSettingsSchema(sectionType: string): BuilderSettingsField[];
export function materializeContractSettings(sectionType: string, settings?: Record<string, unknown>): Record<string, unknown>;
export function validateContractSettingsStrict(sectionType: string, settings?: Record<string, unknown>): void;
export function validateSectionsAgainstContract(sections?: readonly unknown[]): void;

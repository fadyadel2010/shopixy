import type { LucideIcon } from 'lucide-react';
import type { BuilderSettingsField } from './sectionContractRegistry';
interface SectionDefinitionInput {
    type: string; displayName: { en: string; ar: string }; description: { en: string; ar: string };
    icon: LucideIcon; category: string; runtimeOwned?: boolean;
}
export interface SectionDefinition extends SectionDefinitionInput {
    defaultSettings: Record<string, unknown>; settingsSchema: BuilderSettingsField[];
}
export const sectionRegistry: {
    register(definition: SectionDefinitionInput): void;
    get(type: string): SectionDefinition | undefined;
    getAll(): SectionDefinition[];
    getByCategory(category: string): SectionDefinition[];
    getCategories(): string[];
    has(type: string): boolean;
};
export default sectionRegistry;

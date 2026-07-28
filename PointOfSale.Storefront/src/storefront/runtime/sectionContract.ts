import sectionAuthority from '@theme-authority/shopixy-theme-authority.json';

export type SectionContractFieldType =
    | 'text'
    | 'textarea'
    | 'url'
    | 'image'
    | 'image-list'
    | 'video'
    | 'number'
    | 'toggle'
    | 'select'
    | 'guid-list'
    | 'list'
    | 'color';

export interface SectionContractField {
    key: string;
    type: SectionContractFieldType;
    defaultValue: unknown;
    min?: number;
    max?: number;
    options?: string[];
    rendererConsumer: string;
    visualEffect: string;
}

export interface SectionContract {
    type: string;
    defaultSettings: Record<string, unknown>;
    fields: SectionContractField[];
}

export const sectionContracts = sectionAuthority.sectionContracts as SectionContract[];

export function getSectionContract(sectionType: string): SectionContract {
    const contract = sectionContracts.find((candidate) => candidate.type === normalizeSectionType(sectionType));
    if (!contract) {
        throw new Error(`[SectionContract] Unknown section type "${sectionType}".`);
    }

    return contract;
}

export function getSectionDefaultSettings(sectionType: string): Record<string, unknown> {
    return { ...getSectionContract(sectionType).defaultSettings };
}

export function normalizeSectionType(sectionType: string): string {
    return sectionType.trim().toLowerCase();
}

export function validateSectionSettingsStrict(sectionType: string, settings: Record<string, unknown>): void {
    const contract = getSectionContract(sectionType);
    const fieldsByKey = new Map(contract.fields.map((field) => [field.key, field]));

    for (const key of Object.keys(settings)) {
        if (!fieldsByKey.has(key)) {
            throw new Error(`[SectionContract] Unknown setting "${key}" for section "${sectionType}".`);
        }
    }

    for (const field of contract.fields) {
        if (!Object.prototype.hasOwnProperty.call(settings, field.key)) {
            throw new Error(`[SectionContract] Missing setting "${field.key}" for section "${sectionType}".`);
        }

        validateFieldValue(sectionType, field, settings[field.key]);
    }
}

export function materializeSectionSettings(sectionType: string, settings: Record<string, unknown>): Record<string, unknown> {
    validateSectionSettingsStrict(sectionType, settings);
    return { ...getSectionDefaultSettings(sectionType), ...settings };
}

function validateFieldValue(sectionType: string, field: SectionContractField, value: unknown): void {
    if (field.type === 'toggle' && typeof value !== 'boolean') {
        throwTypeError(sectionType, field, 'boolean');
    }

    if (field.type === 'number') {
        if (typeof value !== 'number' || !Number.isFinite(value)) {
            throwTypeError(sectionType, field, 'number');
        }

        if (typeof field.min === 'number' && value < field.min) {
            throw new Error(`[SectionContract] Setting "${field.key}" for section "${sectionType}" is below minimum ${field.min}.`);
        }

        if (typeof field.max === 'number' && value > field.max) {
            throw new Error(`[SectionContract] Setting "${field.key}" for section "${sectionType}" is above maximum ${field.max}.`);
        }
    }

    if ((field.type === 'select' || field.type === 'color' || field.type === 'text' || field.type === 'textarea' || field.type === 'url' || field.type === 'image' || field.type === 'video') && typeof value !== 'string') {
        throwTypeError(sectionType, field, 'string');
    }

    if (field.type === 'guid-list' && (!Array.isArray(value) || value.some((entry) => typeof entry !== 'string'))) {
        throwTypeError(sectionType, field, 'string array');
    }

    if ((field.type === 'image-list' || field.type === 'list') && !Array.isArray(value)) {
        throwTypeError(sectionType, field, 'array');
    }

    if (field.options?.length && typeof value === 'string' && !field.options.includes(value)) {
        throw new Error(`[SectionContract] Setting "${field.key}" for section "${sectionType}" has invalid enum value "${value}".`);
    }
}

function throwTypeError(sectionType: string, field: SectionContractField, expectedType: string): never {
    throw new Error(`[SectionContract] Setting "${field.key}" for section "${sectionType}" must be ${expectedType}.`);
}

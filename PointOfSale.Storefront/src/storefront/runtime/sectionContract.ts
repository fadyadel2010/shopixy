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
    maxItems?: number;
    options?: string[];
    itemLabel?: string;
    itemSchema?: SectionContractItemSchema;
    rendererConsumer: string;
    visualEffect: string;
}

export interface SectionContractItemSchema {
    defaultValue: Record<string, unknown>;
    fields: SectionContractItemField[];
}

export interface SectionContractItemField {
    key: string;
    type: SectionContractFieldType;
    defaultValue: unknown;
    maxLength?: number;
    allowEmpty?: boolean;
}

export interface SectionContract {
    type: string;
    defaultSettings: Record<string, unknown>;
    fields: SectionContractField[];
}

export const sectionContracts = sectionAuthority.sectionContracts as SectionContract[];

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const HEX_COLOR_PATTERN = /^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i;
const DEFAULT_TEXT_MAX_LENGTH = 500;
const DEFAULT_LONG_TEXT_MAX_LENGTH = 5000;
const DEFAULT_URL_MAX_LENGTH = 2048;

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

    if (field.type === 'select' || field.type === 'color' || field.type === 'text' || field.type === 'textarea' || field.type === 'url' || field.type === 'image' || field.type === 'video') {
        if (typeof value !== 'string') {
            throwTypeError(sectionType, field, 'string');
        }

        const maxLength = field.type === 'textarea'
            ? DEFAULT_LONG_TEXT_MAX_LENGTH
            : field.type === 'url' || field.type === 'image' || field.type === 'video'
                ? DEFAULT_URL_MAX_LENGTH
                : DEFAULT_TEXT_MAX_LENGTH;
        if (value.length > maxLength) {
            throw new Error(`[SectionContract] Setting "${field.key}" for section "${sectionType}" exceeds maximum length ${maxLength}.`);
        }

        if (field.type === 'color' && !HEX_COLOR_PATTERN.test(value)) {
            throw new Error(`[SectionContract] Setting "${field.key}" for section "${sectionType}" must be a hex color.`);
        }

        if ((field.type === 'url' || field.type === 'image' || field.type === 'video') && !isSafeAuthoredUrl(value, field.type)) {
            throw new Error(`[SectionContract] Setting "${field.key}" for section "${sectionType}" must be a safe URL or relative path.`);
        }
    }

    if (field.type === 'guid-list') {
        if (!Array.isArray(value) || value.some((entry) => typeof entry !== 'string')) {
            throwTypeError(sectionType, field, 'string array');
        }

        validateListSize(sectionType, field, value);
        if (value.some((entry) => !GUID_PATTERN.test(entry as string))) {
            throw new Error(`[SectionContract] Setting "${field.key}" for section "${sectionType}" must contain valid GUID values.`);
        }

        const normalizedValues = value.map((entry) => (entry as string).toLowerCase());
        if (new Set(normalizedValues).size !== normalizedValues.length) {
            throw new Error(`[SectionContract] Setting "${field.key}" for section "${sectionType}" must not contain duplicate GUID values.`);
        }
    }

    if (field.type === 'image-list' || field.type === 'list') {
        if (!Array.isArray(value)) {
            throwTypeError(sectionType, field, 'array');
        }

        validateListSize(sectionType, field, value);
        if (field.type === 'image-list' && value.some((entry) => typeof entry !== 'string' || !isSafeAuthoredUrl(entry, 'image'))) {
            throw new Error(`[SectionContract] Setting "${field.key}" for section "${sectionType}" must contain safe image URLs.`);
        }

        if (field.type === 'list') {
            validateNestedList(sectionType, field, value);
        }
    }

    if (field.options?.length && typeof value === 'string' && !field.options.includes(value)) {
        throw new Error(`[SectionContract] Setting "${field.key}" for section "${sectionType}" has invalid enum value "${value}".`);
    }
}

function validateListSize(sectionType: string, field: SectionContractField, value: unknown[]): void {
    if (typeof field.maxItems === 'number' && value.length > field.maxItems) {
        throw new Error(`[SectionContract] Setting "${field.key}" for section "${sectionType}" exceeds maximum item count ${field.maxItems}.`);
    }
}

function validateNestedList(sectionType: string, field: SectionContractField, value: unknown[]): void {
    const itemFields = field.itemSchema?.fields;
    if (!itemFields) {
        throw new Error(`[SectionContract] Setting "${field.key}" for section "${sectionType}" has no semantic item schema.`);
    }

    const fieldsByKey = new Map(itemFields.map((itemField) => [itemField.key, itemField]));
    value.forEach((item, index) => {
        if (!item || typeof item !== 'object' || Array.isArray(item)) {
            throw new Error(`[SectionContract] Setting "${field.key}" item ${index} for section "${sectionType}" must be an object.`);
        }

        const itemRecord = item as Record<string, unknown>;
        for (const key of Object.keys(itemRecord)) {
            if (!fieldsByKey.has(key)) {
                throw new Error(`[SectionContract] Setting "${field.key}" item ${index} for section "${sectionType}" contains unsupported property "${key}".`);
            }
        }

        for (const itemField of itemFields) {
            const itemValue = itemRecord[itemField.key];
            if (typeof itemValue !== 'string') {
                throw new Error(`[SectionContract] Setting "${field.key}" item ${index} property "${itemField.key}" for section "${sectionType}" must be string.`);
            }

            if (itemField.allowEmpty === false && !itemValue.trim()) {
                throw new Error(`[SectionContract] Setting "${field.key}" item ${index} property "${itemField.key}" for section "${sectionType}" must not be empty.`);
            }

            if (typeof itemField.maxLength === 'number' && itemValue.length > itemField.maxLength) {
                throw new Error(`[SectionContract] Setting "${field.key}" item ${index} property "${itemField.key}" for section "${sectionType}" exceeds maximum length ${itemField.maxLength}.`);
            }

            if ((itemField.type === 'url' || itemField.type === 'image' || itemField.type === 'video') && !isSafeAuthoredUrl(itemValue, itemField.type)) {
                throw new Error(`[SectionContract] Setting "${field.key}" item ${index} property "${itemField.key}" for section "${sectionType}" must be a safe URL or relative path.`);
            }
        }
    });
}

function isSafeAuthoredUrl(value: string, fieldType: SectionContractFieldType): boolean {
    if (!value.trim()) {
        return true;
    }

    if (value.length > DEFAULT_URL_MAX_LENGTH || /[\s\u0000-\u001f\u007f\\]/.test(value) || value.startsWith('//')) {
        return false;
    }

    if (value.startsWith('/') || value.startsWith('#') || value.startsWith('?')) {
        return true;
    }

    try {
        const parsed = new URL(value);
        if (parsed.protocol === 'http:' || parsed.protocol === 'https:') {
            return !parsed.username && !parsed.password;
        }

        return fieldType === 'url' && (parsed.protocol === 'mailto:' || parsed.protocol === 'tel:');
    } catch {
        return !value.includes(':');
    }
}

function throwTypeError(sectionType: string, field: SectionContractField, expectedType: string): never {
    throw new Error(`[SectionContract] Setting "${field.key}" for section "${sectionType}" must be ${expectedType}.`);
}

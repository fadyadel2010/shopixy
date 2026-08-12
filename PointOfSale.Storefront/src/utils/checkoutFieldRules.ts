import type { CheckoutFieldDefinition, CheckoutFieldRule, ShippingAddress } from '@/types/checkout';

const SYSTEM_FIELD_ADDRESS_KEYS: Record<string, keyof ShippingAddress> = {
    full_name: 'fullName',
    phone: 'phone',
    email: 'email',
    city: 'city',
    area: 'area',
    street_address: 'addressLine',
    building: 'building',
    apartment: 'apartment',
    postal_code: 'postalCode',
    order_notes: 'notes',
};

const isUploadField = (field: CheckoutFieldDefinition) =>
    field.fieldType === 'IMAGE' || field.fieldType === 'FILE' || field.fieldType === 'MULTI_IMAGE';

const parseAttachmentCount = (value?: string): number => {
    if (!value?.trim()) return 0;
    try {
        const parsed = JSON.parse(value);
        return Array.isArray(parsed) ? parsed.filter((item) => item?.attachmentId).length : 0;
    } catch {
        return 0;
    }
};

const isTruthy = (value?: string | null) => value?.toLowerCase() === 'true' || value === '1';

function compareCanonicalDecimals(left: string, right: string): number | null {
    const normalize = (value: string) => {
        let normalized = value;
        if (normalized.startsWith('.')) normalized = `0${normalized}`;
        if (normalized.startsWith('+.')) normalized = `+0${normalized.slice(1)}`;
        if (normalized.startsWith('-.')) normalized = `-0${normalized.slice(1)}`;
        if (normalized.endsWith('.')) normalized = `${normalized}0`;
        return normalized;
    };
    const pattern = /^([+-]?)(\d+)(?:\.(\d+))?$/;
    const leftMatch = pattern.exec(normalize(left));
    const rightMatch = pattern.exec(normalize(right));
    if (!leftMatch || !rightMatch) return null;
    const leftScale = leftMatch[3]?.length ?? 0;
    const rightScale = rightMatch[3]?.length ?? 0;
    const scale = Math.max(leftScale, rightScale);
    const toScaledInteger = (match: RegExpExecArray, currentScale: number) => {
        const digits = `${match[2]}${match[3] ?? ''}${'0'.repeat(scale - currentScale)}`;
        return BigInt(`${match[1] === '-' ? '-' : ''}${digits}`);
    };
    const leftValue = toScaledInteger(leftMatch, leftScale);
    const rightValue = toScaledInteger(rightMatch, rightScale);
    return leftValue === rightValue ? 0 : leftValue > rightValue ? 1 : -1;
}

function isCanonicalDate(value: string): boolean {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
    if (!match) return false;
    const year = Number(match[1]);
    const month = Number(match[2]);
    const day = Number(match[3]);
    if (year < 1 || year > 9999 || month < 1 || month > 12) return false;
    const leapYear = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
    const daysInMonth = [31, leapYear ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    return day >= 1 && day <= daysInMonth[month - 1];
}

function escapeRegExp(value: string): string {
    return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function compareOrdinalIgnoreCase(left: string, right: string, contains: boolean): boolean {
    const pattern = contains ? escapeRegExp(right) : `^${escapeRegExp(right)}$`;
    return new RegExp(pattern, 'iu').test(left);
}

function getFieldValue(field: CheckoutFieldDefinition, address: ShippingAddress, customValues: Record<string, string>): string {
    if (field.fieldKind === 'SYSTEM' && field.systemFieldKey) {
        const key = SYSTEM_FIELD_ADDRESS_KEYS[field.systemFieldKey];
        return key ? String(address[key] ?? '') : '';
    }

    return customValues[field.id] ?? '';
}

function isEmptyValue(field: CheckoutFieldDefinition, value: string): boolean {
    if (field.fieldType === 'CHECKBOX') return !isTruthy(value);
    if (isUploadField(field)) return parseAttachmentCount(value) === 0;
    return !value.trim();
}

function compare(field: CheckoutFieldDefinition, value: string, comparisonValue: string, operator: string): boolean {
    if (operator === 'IS_EMPTY') return isEmptyValue(field, value);
    if (operator === 'IS_NOT_EMPTY') return !isEmptyValue(field, value);

    const left = value.trim();
    const right = comparisonValue.trim();
    if (!left) return operator === 'NOT_EQUALS' || operator === 'NOT_CONTAINS';

    if (field.fieldType === 'CHECKBOX') {
        const same = isTruthy(left) === isTruthy(right);
        return operator === 'EQUALS' ? same : operator === 'NOT_EQUALS' && !same;
    }

    if (field.fieldType === 'NUMBER') {
        const comparison = compareCanonicalDecimals(left, right);
        if (comparison == null) return false;
        if (operator === 'EQUALS') return comparison === 0;
        if (operator === 'NOT_EQUALS') return comparison !== 0;
        if (operator === 'GREATER_THAN') return comparison > 0;
        if (operator === 'GREATER_THAN_OR_EQUAL') return comparison >= 0;
        if (operator === 'LESS_THAN') return comparison < 0;
        if (operator === 'LESS_THAN_OR_EQUAL') return comparison <= 0;
        return false;
    }

    if (field.fieldType === 'DATE') {
        if (!isCanonicalDate(left) || !isCanonicalDate(right)) return false;
        if (operator === 'EQUALS') return left === right;
        if (operator === 'NOT_EQUALS') return left !== right;
        if (operator === 'GREATER_THAN') return left > right;
        if (operator === 'GREATER_THAN_OR_EQUAL') return left >= right;
        if (operator === 'LESS_THAN') return left < right;
        if (operator === 'LESS_THAN_OR_EQUAL') return left <= right;
        return false;
    }

    const caseSensitive = field.fieldType === 'SELECT' || field.fieldType === 'RADIO';
    const equals = caseSensitive ? left === right : compareOrdinalIgnoreCase(left, right, false);
    if (operator === 'EQUALS') return equals;
    if (operator === 'NOT_EQUALS') return !equals;
    const contains = caseSensitive ? left.includes(right) : compareOrdinalIgnoreCase(left, right, true);
    if (operator === 'CONTAINS') return contains;
    if (operator === 'NOT_CONTAINS') return !contains;
    return false;
}

function ruleMatches(
    rule: CheckoutFieldRule,
    fieldsById: Map<string, CheckoutFieldDefinition>,
    address: ShippingAddress,
    customValues: Record<string, string>,
    states: Map<string, { visible: boolean; required: boolean }>
): boolean {
    const results = (rule.conditions ?? []).map((condition) => {
        const source = fieldsById.get(condition.sourceFieldId);
        if (!source) return false;
        const value = states.get(source.id)?.visible === false
            ? ''
            : getFieldValue(source, address, customValues);
        return compare(source, value, condition.comparisonValue ?? '', condition.operator);
    });

    return rule.conditionMode === 'ANY' ? results.some(Boolean) : results.every(Boolean);
}

export function getEffectiveCheckoutFields(
    fields: CheckoutFieldDefinition[],
    address: ShippingAddress,
    customValues: Record<string, string>
): CheckoutFieldDefinition[] {
    const fieldsById = new Map(fields.map((field) => [field.id, field]));
    const states = new Map(fields.map((field) => [field.id, {
        visible: field.isEnabled,
        required: field.isEnabled && field.isRequired,
    }]));
    const evaluated = new Set<string>();
    const evaluating = new Set<string>();

    const evaluateField = (field: CheckoutFieldDefinition) => {
            if (evaluated.has(field.id)) return;
            evaluated.add(field.id);
            const rules = field.configuration.conditionalRules ?? [];
            if (!field.isEnabled || rules.length === 0) return;
            if (evaluating.has(field.id)) return;
            evaluating.add(field.id);
            for (const sourceId of new Set(rules.flatMap((rule) => rule.conditions.map((condition) => condition.sourceFieldId)))) {
                const source = fieldsById.get(sourceId);
                if (source) evaluateField(source);
            }
            evaluating.delete(field.id);

            const hasShowRule = rules.some((rule) => rule.actions?.includes('SHOW'));
            let showMatched = false;
            let hideMatched = false;
            let requireMatched = false;
            let optionalMatched = false;

            for (const rule of rules) {
                if (!ruleMatches(rule, fieldsById, address, customValues, states)) continue;
                showMatched ||= rule.actions?.includes('SHOW') ?? false;
                hideMatched ||= rule.actions?.includes('HIDE') ?? false;
                requireMatched ||= rule.actions?.includes('REQUIRE') ?? false;
                optionalMatched ||= rule.actions?.includes('OPTIONAL') ?? false;
            }

            let visible = hasShowRule ? showMatched : field.isEnabled;
            if (hideMatched) visible = false;
            let required = field.isRequired;
            if (requireMatched) required = true;
            if (optionalMatched) required = false;
            if (!visible) required = false;
            states.set(field.id, { visible, required });
    };

    fields.forEach(evaluateField);
    return fields
        .map((field) => {
            const state = states.get(field.id)!;
            return { ...field, isRequired: state.required, isEnabled: field.isEnabled && state.visible };
        })
        .filter((field) => field.isEnabled);
}

export interface CheckoutFieldReviewItem {
    id: string;
    label: string;
    value: string;
}

export function getCustomCheckoutFieldReviewItems(
    fields: CheckoutFieldDefinition[],
    address: ShippingAddress,
    customValues: Record<string, string>
): CheckoutFieldReviewItem[] {
    return getEffectiveCheckoutFields(fields, address, customValues)
        .filter((field) => field.fieldKind === 'CUSTOM')
        .flatMap((field) => {
            const rawValue = customValues[field.id] ?? '';
            let displayValue = rawValue.trim();

            if (isUploadField(field)) {
                try {
                    const attachments = JSON.parse(rawValue);
                    displayValue = Array.isArray(attachments)
                        ? attachments
                            .filter((item) => item?.attachmentId && item?.fileName)
                            .map((item) => String(item.fileName))
                            .join(', ')
                        : '';
                } catch {
                    displayValue = '';
                }
            } else if (field.fieldType === 'CHECKBOX') {
                displayValue = isTruthy(rawValue) ? 'Yes' : 'No';
            } else if (field.fieldType === 'SELECT' || field.fieldType === 'RADIO') {
                displayValue = field.configuration.options?.find((option) => option.value === rawValue)?.label ?? displayValue;
            }

            return displayValue
                ? [{ id: field.id, label: field.label, value: displayValue }]
                : [];
        });
}

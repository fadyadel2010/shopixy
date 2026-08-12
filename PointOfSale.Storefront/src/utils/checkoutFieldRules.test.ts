import { describe, expect, it } from 'vitest';
import { getCustomCheckoutFieldReviewItems, getEffectiveCheckoutFields } from './checkoutFieldRules';
import type { CheckoutFieldDefinition, ShippingAddress } from '@/types/checkout';

const address: ShippingAddress = {
    fullName: 'Customer',
    phone: '01000000000',
    city: 'Cairo',
    area: 'Nasr City',
    addressLine: 'Street address',
};

describe('checkout field rules', () => {
    it('shows and requires a target upload field when the source option matches', () => {
        const source = field('source', 'RADIO', 'Need printing', {
            options: [{ value: 'yes', label: 'Yes' }, { value: 'no', label: 'No' }],
        });
        const target = field('target', 'IMAGE', 'Upload design', {
            conditionalRules: [{
                ruleId: 'rule1',
                conditionMode: 'ALL',
                conditions: [{ sourceFieldId: source.id, operator: 'EQUALS', comparisonValue: 'yes' }],
                actions: ['SHOW', 'REQUIRE'],
            }],
        });

        const effective = getEffectiveCheckoutFields([source, target], address, { source: 'yes' });

        expect(effective.find((item) => item.id === target.id)?.isRequired).toBe(true);
    });

    it('hides and clears required state when a show rule does not match', () => {
        const source = field('source', 'RADIO', 'Need printing', {
            options: [{ value: 'yes', label: 'Yes' }, { value: 'no', label: 'No' }],
        });
        const target = field('target', 'IMAGE', 'Upload design', {
            conditionalRules: [{
                ruleId: 'rule1',
                conditionMode: 'ALL',
                conditions: [{ sourceFieldId: source.id, operator: 'EQUALS', comparisonValue: 'yes' }],
                actions: ['SHOW', 'REQUIRE'],
            }],
        });

        const effective = getEffectiveCheckoutFields([source, target], address, { source: 'no' });

        expect(effective.some((item) => item.id === target.id)).toBe(false);
    });

    it('keeps disabled fields hidden even when a show rule matches', () => {
        const source = field('source', 'RADIO', 'Need printing', {
            options: [{ value: 'yes', label: 'Yes' }, { value: 'no', label: 'No' }],
        });
        const target = {
            ...field('target', 'TEXT', 'Internal note', {
                conditionalRules: [{
                    ruleId: 'rule1',
                    conditionMode: 'ALL' as const,
                    conditions: [{ sourceFieldId: source.id, operator: 'EQUALS' as const, comparisonValue: 'yes' }],
                    actions: ['SHOW' as const],
                }],
            }),
            isEnabled: false,
        };

        const effective = getEffectiveCheckoutFields([source, target], address, { source: 'yes' });

        expect(effective.some((item) => item.id === target.id)).toBe(false);
    });

    it('does not let a retained hidden source value drive a dependent rule', () => {
        const control = field('control', 'TEXT', 'Control', {});
        const source = field('source', 'TEXT', 'Source', {
            conditionalRules: [{
                ruleId: 'source-rule',
                conditionMode: 'ALL',
                conditions: [{ sourceFieldId: control.id, operator: 'EQUALS', comparisonValue: 'show' }],
                actions: ['SHOW'],
            }],
        });
        const target = field('target', 'TEXT', 'Target', {
            conditionalRules: [{
                ruleId: 'target-rule',
                conditionMode: 'ALL',
                conditions: [{ sourceFieldId: source.id, operator: 'EQUALS', comparisonValue: 'retained' }],
                actions: ['SHOW'],
            }],
        });

        const effective = getEffectiveCheckoutFields(
            [control, source, target],
            address,
            { control: 'hide', source: 'retained' },
        );

        expect(effective.some((item) => item.id === source.id)).toBe(false);
        expect(effective.some((item) => item.id === target.id)).toBe(false);
    });

    it('preserves decimal precision beyond the JavaScript safe integer range', () => {
        const source = field('source-number', 'NUMBER', 'Number', {});
        const target = field('target-number', 'TEXT', 'Target', {
            conditionalRules: [{
                ruleId: 'number-rule',
                conditionMode: 'ALL',
                conditions: [{ sourceFieldId: source.id, operator: 'GREATER_THAN', comparisonValue: '9007199254740992' }],
                actions: ['SHOW'],
            }],
        });

        const effective = getEffectiveCheckoutFields([source, target], address, { [source.id]: '9007199254740993' });

        expect(effective.some((item) => item.id === target.id)).toBe(true);
    });

    it.each(['.5', '5.'])('matches backend decimal grammar for %s', (value) => {
        const source = field('source-number', 'NUMBER', 'Number', {});
        const target = field('target-number', 'TEXT', 'Target', {
            conditionalRules: [{
                ruleId: 'number-rule',
                conditionMode: 'ALL',
                conditions: [{ sourceFieldId: source.id, operator: 'EQUALS', comparisonValue: value }],
                actions: ['SHOW'],
            }],
        });

        const effective = getEffectiveCheckoutFields([source, target], address, { [source.id]: value });

        expect(effective.some((item) => item.id === target.id)).toBe(true);
    });

    it('rejects non-canonical offset dates instead of applying timezone coercion', () => {
        const source = field('source-date', 'DATE', 'Date', {});
        const target = field('target-date', 'TEXT', 'Target', {
            conditionalRules: [{
                ruleId: 'date-rule',
                conditionMode: 'ALL',
                conditions: [{ sourceFieldId: source.id, operator: 'EQUALS', comparisonValue: '2026-08-09' }],
                actions: ['SHOW'],
            }],
        });

        const effective = getEffectiveCheckoutFields([source, target], address, { [source.id]: '2026-08-09T23:00:00-02:00' });

        expect(effective.some((item) => item.id === target.id)).toBe(false);
    });

    it('admits year 0001 dates consistently with backend DateOnly parsing', () => {
        const source = field('source-date', 'DATE', 'Date', {});
        const target = field('target-date', 'TEXT', 'Target', {
            conditionalRules: [{
                ruleId: 'date-rule',
                conditionMode: 'ALL',
                conditions: [{ sourceFieldId: source.id, operator: 'EQUALS', comparisonValue: '0001-01-01' }],
                actions: ['SHOW'],
            }],
        });

        const effective = getEffectiveCheckoutFields([source, target], address, { [source.id]: '0001-01-01' });

        expect(effective.some((item) => item.id === target.id)).toBe(true);
    });

    it('builds review-safe option and upload presentation without object keys', () => {
        const option = field('option', 'SELECT', 'Size', {
            options: [{ value: 'large', label: 'Large' }],
        });
        const upload = field('upload', 'IMAGE', 'Design', {});
        const items = getCustomCheckoutFieldReviewItems([option, upload], address, {
            option: 'large',
            upload: JSON.stringify([{ attachmentId: 'attachment', fileName: 'design.jpg', objectKey: 'private/key' }]),
        });

        expect(items).toEqual([
            { id: 'option', label: 'Size', value: 'Large' },
            { id: 'upload', label: 'Design', value: 'design.jpg' },
        ]);
        expect(JSON.stringify(items)).not.toContain('private/key');
    });
});

function field(id: string, fieldType: CheckoutFieldDefinition['fieldType'], label: string, configuration: Partial<CheckoutFieldDefinition['configuration']>): CheckoutFieldDefinition {
    return {
        id,
        fieldKind: 'CUSTOM',
        fieldType,
        label,
        isEnabled: true,
        isRequired: false,
        displayOrder: 10,
        configuration: { schemaVersion: 1, ...configuration },
    };
}

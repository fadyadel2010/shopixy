import { describe, expect, it } from 'vitest';
import { formatPhoneForLocalInput, getCallingCodeForCountry, normalizePhoneForSubmission } from './phone';

describe('checkout phone presentation', () => {
    it.each([
        ['01022174803', 'EG', '+201022174803'],
        ['00201022174803', 'EG', '+201022174803'],
        ['0551234567', 'SA', '+966551234567'],
        ['0501234567', 'AE', '+971501234567'],
        ['(202) 555-1234', 'US', '+12025551234'],
        ['+447911123456', 'EG', '+447911123456'],
        ['012', 'EG', '012'],
        ['not a phone', 'SA', 'not a phone'],
    ])('preserves the displayed dialing country for %s', (input, country, expected) => {
        expect(normalizePhoneForSubmission(input, country)).toBe(expected);
    });
    it('derives calling codes from the configured store country', () => {
        expect(getCallingCodeForCountry('EG')).toBe('+20');
        expect(getCallingCodeForCountry('SA')).toBe('+966');
        expect(getCallingCodeForCountry('US')).toBe('+1');
    });

    it('displays a saved E.164 number in its national format', () => {
        expect(formatPhoneForLocalInput('+201022174803', 'EG')).toBe('010 22174803');
        expect(formatPhoneForLocalInput('+14155552671', 'US')).toBe('(415) 555-2671');
    });

    it('keeps local input and foreign international numbers unchanged', () => {
        expect(formatPhoneForLocalInput('01022174803', 'EG')).toBe('01022174803');
        expect(formatPhoneForLocalInput('+14155552671', 'EG')).toBe('+14155552671');
    });
});

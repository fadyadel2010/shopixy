import { describe, expect, it } from 'vitest';
import { formatPhoneForLocalInput, getCallingCodeForCountry } from './phone';

describe('checkout phone presentation', () => {
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

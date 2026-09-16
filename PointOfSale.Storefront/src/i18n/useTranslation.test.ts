import { describe, expect, it } from 'vitest';
import { getLocalizedField } from './useTranslation';

describe('getLocalizedField', () => {
    it('uses Arabic content for an Arabic request', () => {
        expect(getLocalizedField({ name: 'Laptop', nameAr: 'حاسوب' }, 'name', 'ar')).toBe('حاسوب');
    });

    it('falls back to primary content when Arabic is absent', () => {
        expect(getLocalizedField({ name: 'Laptop', nameAr: '  ' }, 'name', 'ar')).toBe('Laptop');
    });

    it('falls back to Arabic content when primary content is absent', () => {
        expect(getLocalizedField({ name: '  ', nameAr: 'حاسوب' }, 'name', 'en')).toBe('حاسوب');
    });

    it('applies the same fallback to descriptions', () => {
        const product = { description: 'Primary description', descriptionAr: 'وصف عربي' };

        expect(getLocalizedField(product, 'description', 'en')).toBe('Primary description');
        expect(getLocalizedField(product, 'description', 'ar')).toBe('وصف عربي');
    });

    it('never returns null when both values are absent', () => {
        expect(getLocalizedField({ name: null, nameAr: null }, 'name', 'ar')).toBe('');
        expect(getLocalizedField({ name: '  ', nameAr: '  ' }, 'name', 'en')).toBe('');
    });
});

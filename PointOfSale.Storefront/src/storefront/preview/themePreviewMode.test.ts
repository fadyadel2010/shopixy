import { describe, expect, it } from 'vitest';
import { getThemePreviewToken, isThemePreviewMode } from './themePreviewMode';

describe('themePreviewMode', () => {
    it('recognizes a non-empty signed preview token', () => {
        expect(getThemePreviewToken('?themePreviewToken=signed-token')).toBe('signed-token');
        expect(isThemePreviewMode('?themePreviewToken=signed-token')).toBe(true);
    });

    it('does not enable preview mode for missing or blank tokens', () => {
        expect(getThemePreviewToken('?category=featured')).toBeNull();
        expect(getThemePreviewToken('?themePreviewToken=%20%20')).toBeNull();
        expect(isThemePreviewMode('')).toBe(false);
    });
});

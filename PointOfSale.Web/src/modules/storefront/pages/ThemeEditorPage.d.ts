import type { ReactElement } from 'react';
export function ThemeEditorPage(): ReactElement;
export function validateThemeCandidate(candidate: { colors: Record<string, string>; font: string; baseSize: string }): {
    colors: Record<string, string>; font: string; baseSize: string; isValid: boolean;
};

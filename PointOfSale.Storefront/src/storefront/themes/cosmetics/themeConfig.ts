import type { ThemeConfig } from '@/types';

export const cosmeticsThemeConfig: ThemeConfig = {
    colors: {
        primary: '#D05D61',
        secondary: '#E7DFDB',
        background: '#FFFFFF',
        text: '#131316',
        muted: '#6D5F5F',
    },
    font: {
        family: "'Instrument Sans', sans-serif",
        familyAr: "'Almarai', 'Tajawal', 'Noto Sans Arabic'",
        baseSize: '16px',
    },
    ui: {
        borderRadius: '0px',
        buttonStyle: 'outline',
    },
    layout: {
        productCardVariant: 'compact',
        headerVariant: 'centered',
    },
};

export default cosmeticsThemeConfig;

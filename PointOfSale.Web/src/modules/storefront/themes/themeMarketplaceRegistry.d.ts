export interface MarketplaceTheme {
    id: string; name: string; version: string; capabilities: string[]; isDefault: boolean;
    previewImage: string; description: string; accent: string;
}
export function getAvailableMarketplaceThemes(): MarketplaceTheme[];
export function getMarketplaceThemeById(themeId: string): MarketplaceTheme | undefined;
export function getDefaultMarketplaceTheme(): MarketplaceTheme | undefined;
export function getThemeArtworkStyle(theme: Pick<MarketplaceTheme, 'accent'>): { backgroundColor: string };

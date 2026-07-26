export const brand = {
  name: "Shopixy",
  fullName: "Shopixy Marketplace",
  legacyName: "CommerceHub",
  tagline: "Curated multi-store shopping with a premium, modern feel."
} as const;

export const replaceLegacyBranding = (value: string) => value.split(brand.legacyName).join(brand.fullName);

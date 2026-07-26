export type MarketplaceCmsPageContentType = "RichText" | "Blocks";
export type MarketplaceFooterLinkType = "Internal" | "External" | "Cms";

export interface MarketplaceCmsPage {
  slug: string;
  title: string;
  contentType: MarketplaceCmsPageContentType;
  content: string | MarketplaceCmsPageBlock[];
  seo: MarketplaceCmsPageSeo;
  updatedAtUtc: string;
  publishedAtUtc?: string | null;
}

export interface MarketplaceCmsPageSeo {
  metaTitle?: string | null;
  metaDescription?: string | null;
  openGraphImageUrl?: string | null;
}

export interface MarketplaceCmsPageBlock {
  type: string;
  data: Record<string, unknown>;
}

export interface MarketplaceFooter {
  source: "Configured" | "Derived" | "Empty";
  sections: MarketplaceFooterSection[];
  socialLinks: MarketplaceSocialLink[];
  generatedAtUtc: string;
}

export interface MarketplaceFooterSection {
  title: string;
  sortOrder: number;
  links: MarketplaceFooterLink[];
}

export interface MarketplaceFooterLink {
  label: string;
  type: MarketplaceFooterLinkType;
  url: string;
  openInNewTab: boolean;
}

export interface MarketplaceSocialLink {
  platform: string;
  url: string;
  icon?: string | null;
}

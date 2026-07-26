import { homepageEndpoints } from "@/api/endpoints";
import { httpClient } from "@/api/http-client";
import type { HomepageSectionRecord, HomepageSectionType } from "@/homepage/types";

type MarketplaceHomepageSectionResponse = {
  type: HomepageSectionType;
  order: number;
  data: Record<string, unknown>;
  version: number;
  schemaVersion: number;
  isActive: boolean;
};

type MarketplaceHomepageResponse = {
  tenantId?: string | null;
  sections: MarketplaceHomepageSectionResponse[];
  lastUpdated: string;
};

export const homepageService = {
  async getSections(signal?: AbortSignal) {
    const response = await httpClient.get<MarketplaceHomepageResponse>(homepageEndpoints.homepage, {
      signal
    });

    return response.data.sections
      .filter((section) => section.isActive)
      .map((section) => ({
        id: `${section.type}-${section.order}-${section.version}`,
        type: section.type,
        order: section.order,
        isActive: section.isActive,
        tenantId: response.data.tenantId ?? null,
        data: section.data,
        version: section.version,
        schemaVersion: section.schemaVersion,
        updatedAt: response.data.lastUpdated
      }) as unknown as HomepageSectionRecord);
  }
};

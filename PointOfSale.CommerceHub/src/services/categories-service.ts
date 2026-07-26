import { categoryEndpoints } from "@/api";
import { httpClient } from "@/api/http-client";
import type { CommerceHubCategory } from "@/types";
import { buildCategoryTree } from "@/utils";

type CommerceHubCategoryTreeNodeResponse = {
  id: string;
  name: string;
  slug: string;
  parentId?: string | null;
  level: number;
  iconEmoji?: string | null;
  hasAttributes: boolean;
};

export const categoriesService = {
  async getCategoryTree() {
    const response = await httpClient.get<CommerceHubCategoryTreeNodeResponse[]>(categoryEndpoints.tree);
    return buildCategoryTree(
      response.data.map((category) => ({
        id: category.id,
        name: category.name,
        slug: category.slug,
        parentId: category.parentId,
        level: category.level,
        iconEmoji: category.iconEmoji,
        hasAttributes: category.hasAttributes
      })) as CommerceHubCategory[]
    );
  }
};

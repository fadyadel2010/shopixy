import type { CommerceHubCategory } from "@/types";

type FlatCommerceHubCategory = Omit<CommerceHubCategory, "children"> & {
  parentId?: string | null;
  level?: number;
};

export const buildCategoryTree = (categories: FlatCommerceHubCategory[]) => {
  const categoryMap = new Map<string, CommerceHubCategory>();
  const roots: CommerceHubCategory[] = [];

  categories.forEach((category) => {
    categoryMap.set(category.id, {
      ...category,
      children: []
    });
  });

  categories.forEach((category) => {
    const node = categoryMap.get(category.id);
    if (!node) {
      return;
    }

    if (category.parentId && categoryMap.has(category.parentId)) {
      categoryMap.get(category.parentId)?.children?.push(node);
      return;
    }

    roots.push(node);
  });

  const sortNodes = (nodes: CommerceHubCategory[]) => {
    nodes.sort((left, right) => {
      const leftLevel = left.level ?? 0;
      const rightLevel = right.level ?? 0;
      if (leftLevel !== rightLevel) {
        return leftLevel - rightLevel;
      }

      return left.name.localeCompare(right.name);
    });

    nodes.forEach((node) => {
      if (node.children?.length) {
        sortNodes(node.children);
      }
    });
  };

  sortNodes(roots);
  return roots;
};

export const flattenCategoryTree = (categories: CommerceHubCategory[]) => {
  const flattened: CommerceHubCategory[] = [];

  const visit = (category: CommerceHubCategory) => {
    flattened.push(category);
    category.children?.forEach(visit);
  };

  categories.forEach(visit);
  return flattened;
};

export type CategoryOption = {
  id: string;
  name: string;
  label: string;
  depth: number;
  productCount?: number;
};

export const buildCategoryOptions = (categories: CommerceHubCategory[]) => {
  const flattened: CategoryOption[] = [];

  const visit = (category: CommerceHubCategory, trail: string[] = [], depth = 0) => {
    const nextTrail = [...trail, category.name];
    flattened.push({
      id: category.id,
      name: category.name,
      label: nextTrail.join(" / "),
      depth,
      productCount: category.productCount
    });

    category.children?.forEach((child) => visit(child, nextTrail, depth + 1));
  };

  categories.forEach((category) => visit(category));
  return flattened;
};

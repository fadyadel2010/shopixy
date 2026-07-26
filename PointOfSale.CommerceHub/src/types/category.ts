export type CommerceHubCategory = {
  id: string;
  name: string;
  slug?: string;
  parentId?: string | null;
  level?: number;
  iconEmoji?: string | null;
  hasAttributes?: boolean;
  productCount?: number;
  image?: string | null;
  children?: CommerceHubCategory[];
};

import { useEffect } from "react";

import { brand, replaceLegacyBranding } from "@/shared/brand";

const DEFAULT_TITLE = brand.fullName;

export const useDocumentTitle = (title?: string) => {
  useEffect(() => {
    const normalizedTitle = title ? replaceLegacyBranding(title) : undefined;
    document.title =
      normalizedTitle && !normalizedTitle.includes(DEFAULT_TITLE)
        ? `${normalizedTitle} - ${DEFAULT_TITLE}`
        : normalizedTitle ?? DEFAULT_TITLE;

    return () => {
      document.title = DEFAULT_TITLE;
    };
  }, [title]);
};

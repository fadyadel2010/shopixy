type AnalyticsPayload = Record<string, string | number | boolean | undefined | null>;

const logEvent = (eventName: string, payload: AnalyticsPayload) => {
  if (!import.meta.env.DEV && import.meta.env.VITE_ENABLE_ANALYTICS_DEBUG !== "true") {
    return;
  }

  console.info(`[CommerceHub Analytics] ${eventName}`, payload);
};

export const analyticsService = {
  trackSearchSubmitted(query: string, source: string) {
    logEvent("search_submitted", {
      query,
      source
    });
  },

  trackCategorySelected(categoryId: string, categoryName: string, source: string) {
    logEvent("category_selected", {
      categoryId,
      categoryName,
      source
    });
  },

  trackFilterApplied(filterKey: string, filterValue: string, source: string, query?: string) {
    logEvent("filter_applied", {
      filterKey,
      filterValue,
      source,
      query
    });
  },

  trackProductClicked(productId: string, variantId: string, source: string) {
    logEvent("product_clicked", {
      productId,
      variantId,
      source
    });
  },

  trackProductViewed(productId: string, variantId: string | undefined, source: string) {
    logEvent("product_viewed", {
      productId,
      variantId,
      source
    });
  },

  trackProductAddedToCart(productId: string, variantId: string, quantity: number, source: string) {
    logEvent("product_added_to_cart", {
      productId,
      variantId,
      quantity,
      source
    });
  }
};

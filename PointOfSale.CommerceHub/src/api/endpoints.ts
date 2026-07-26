export const commerceHubEndpoints = {
  cart: "/api/commercehub/cart",
  cartPricing: "/api/commercehub/cart/price",
  products: "/api/commercehub/products",
  productById: (id: string) => `/api/marketplace/products/${id}`,
  resolveProductVariant: (id: string) => `/api/marketplace/products/${id}/variant-resolution`,
  search: "/api/commercehub/search",
  searchAutocomplete: "/api/commercehub/search/autocomplete",
  searchPopular: "/api/commercehub/search/popular",
  searchClick: "/api/commercehub/search/click",
  searchImpression: "/api/commercehub/search/impression",
  searchFilterEvent: "/api/commercehub/search/filter-event",
  checkout: "/api/commercehub/checkout",
  paymentInitiate: "/api/commercehub/payments/initiate",
  orderDetail: (orderId: string) => `/api/commercehub/orders/${orderId}`
};

export const categoryEndpoints = {
  tree: "/api/commercehub/categories/tree"
};

export const homepageEndpoints = {
  homepage: "/api/read/homepage"
};

export const marketplaceContentEndpoints = {
  footer: "/api/marketplace/footer",
  page: (slug: string) => `/api/marketplace/pages/${slug}`
};

export const marketplaceReviewEndpoints = {
  create: "/api/marketplace/reviews",
  update: (reviewId: string) => `/api/marketplace/reviews/${reviewId}`,
  delete: (reviewId: string) => `/api/marketplace/reviews/${reviewId}`,
  list: (productId: string) => `/api/marketplace/products/${productId}/reviews`,
  summary: (productId: string) => `/api/marketplace/products/${productId}/rating-summary`,
  context: (productId: string) => `/api/marketplace/products/${productId}/review-context`
};

export const customerAuthEndpoints = {
  register: "/api/customers/auth/register",
  login: "/api/customers/auth/login",
  me: "/api/customers/auth/me",
  logout: "/api/customers/auth/logout",
  forgotPassword: "/api/customers/auth/forgot-password",
  resetPassword: "/api/customers/auth/reset-password",
  verifyEmail: "/api/customers/auth/verify-email",
  resendVerification: "/api/customers/auth/resend-verification",
  updateProfile: "/api/customers/auth/profile",
  changePassword: "/api/customers/auth/change-password",
  changeEmail: "/api/customers/auth/change-email",
  revokeAllSessions: "/api/customers/auth/revoke-all-sessions"
};

export const customerOrderEndpoints = {
  list: "/api/customers/orders",
  detail: (orderId: string) => `/api/customers/orders/${orderId}`
};

export const customerRefundEndpoints = {
  list: "/api/customers/refunds",
  upload: "/api/customers/refunds/upload"
};

export const customerPayoutDetailsEndpoints = {
  list: "/api/customers/payout-details",
  detail: (id: string) => `/api/customers/payout-details/${id}`
};

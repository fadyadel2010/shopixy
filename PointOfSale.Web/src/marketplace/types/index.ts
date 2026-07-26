/**
 * Marketplace domain types.
 * These match backend API contracts EXACTLY.
 * NO client-side enhancements.
 * 
 * HARDENING RULES:
 * - All response types are Readonly (immutable snapshots)
 * - Request types are mutable (for form state)
 * - No mutation of DTOs allowed
 * - Data treated as snapshots from backend
 */

export type MarketplaceProfile = Readonly<{
  hasMarketplaceProfile: boolean;
  profileId?: string;
  displayName?: string;
  isActive: boolean;
  bio?: string;
  profileImageUrl?: string;
  coverImageUrl?: string;
  location?: string;
  skills?: string; // JSON array
  portfolioLinks?: string; // JSON array
}>;

// Request type - mutable for form state
export interface ServiceSearchRequest {
  query?: string;
  category?: string;     // DS-1: primary category filter
  categoryId?: string;   // Legacy alias — kept for backward compat
  minRating?: number;
  minCompletedRequests?: number;
  trustBadge?: string;
  page: number;
  pageSize: number;
  sort: 'relevance' | 'rating' | 'newest' | 'price_low' | 'price_high';
}

export type ServiceSearchResponse = Readonly<{
  items: readonly ServiceResult[];
  totalCount: number;
  page: number;
  pageSize: number;
}>;

export type ServiceResult = Readonly<{
  serviceId: string;
  title: string;
  description: string;
  categoryId?: string;
  providerProfileId: string;
  providerDisplayName: string;
  averageRating: number;
  totalReviews: number;
  trustBadge: string;
  totalRequests: number;
  completedRequests: number;
  completionRate: number;
  createdAt: string;
  startingPrice?: number;
  pricingType?: 'Fixed' | 'Hourly' | 'Package';
  currency?: string;
  serviceImages?: string; // JSON array
}>;

export type ServiceDetails = Readonly<{
  serviceId: string;
  title: string;
  description: string;
  category?: string;
  providerProfileId: string;
  providerDisplayName?: string;
  isActive: boolean;
  createdAt: string;
  startingPrice?: number;
  pricingType?: 'Fixed' | 'Hourly' | 'Package';
  currency?: string;
  serviceImages?: string; // JSON array
  packages?: string; // JSON array
  requestEntry?: Readonly<{
    canRequest: boolean;
    reason: string;
    activeRequestId?: string | null;
  }>;
  // Phase FE-3: Provider profile details
  providerProfile?: Readonly<{
    bio?: string;
    profileImageUrl?: string;
    coverImageUrl?: string;
    location?: string;
    skills?: string; // JSON array
    portfolioLinks?: string; // JSON array
  }>;
  trust?: Readonly<{
    averageRating: number;
    totalReviews: number;
    trustBadge: string;
    ratingDistribution: Readonly<Record<number, number>>;
    ratingPercentages: Readonly<Record<number, number>>;
  }>;
}>;

export type MarketplaceProfileStats = Readonly<{
  tenantId: string;
  providerUserId: string;
  providerProfileId: string;
  servicesOffered: number;
  completedRequests: number;
  averageRating: number;
  totalReviews: number;
  trustBadge: string;
}>;

export type ServiceReview = Readonly<{
  reviewId: string;
  serviceRequestId: string;
  serviceId: string;
  reviewerUserId: string;
  providerUserId: string;
  rating: number;
  comment?: string;
  createdAt: string;
  updatedAt: string;
  buyerName: string;
  marketingEvidence?: Readonly<{
    campaignName: string;
    platform: string;
    startDate: string;
    endDate: string;
    evidenceType: 'SelfReported' | 'PosVerified';
    ordersCount?: number;
    revenueAmount?: number;
  }> | null;
}>;

export type ServiceReviewHistory = Readonly<{
  serviceId: string;
  providerProfileId: string;
  providerUserId: string;
  totalReviews: number;
  items: readonly ServiceReview[];
}>;

export type ServiceRequest = Readonly<{
  requestId: string;
  serviceId: string;
  serviceTitle?: string;
  serviceCategory?: string;
  startingPrice?: number;
  pricingType?: 'Fixed' | 'Hourly' | 'Package';
  currency?: string;
  packages?: string;
  providerDisplayName?: string;
  buyerDisplayName?: string;
  buyerUserId?: string;
  providerUserId?: string;
  status: string;
  completionEvidence?: ServiceRequestCompletionEvidence;
  requestMessage?: string;
  createdAt: string;
  canChat: boolean;
  canViewChatHistory?: boolean;
  canSendChat?: boolean;
  canConfirmCompletion: boolean;
  canCancel: boolean;
  canReview: boolean;
  existingReviewId?: string | null;
  canAccept?: boolean;
  canReject?: boolean;
  canMarkCompleted?: boolean;
}>;

export type ServiceRequestDetails = Readonly<{
  requestId: string;
  serviceId: string;
  serviceTitle?: string;
  serviceCategory?: string;
  startingPrice?: number;
  pricingType?: 'Fixed' | 'Hourly' | 'Package';
  currency?: string;
  packages?: string;
  buyerUserId: string;
  providerUserId: string;
  providerProfileId?: string;
  providerDisplayName?: string;
  status: string;
  requestMessage?: string;
  createdAt: string;
  auditTimeline?: readonly ServiceRequestAuditEvent[];
  completionEvidence?: ServiceRequestCompletionEvidence;
  capabilities: Readonly<{
    canAccept: boolean;
    canMarkCompleted: boolean;
    canConfirmCompletion: boolean;
    canCancel: boolean;
    canChat: boolean;
    canViewChatHistory: boolean;
    canSendChat: boolean;
    canReview: boolean;
    existingReviewId?: string | null;
    canReject?: boolean;
  }>;
}>;

export type ServiceRequestCompletionEvidencePhase = Readonly<{
  evidenceId: string;
  requestId: string;
  evidenceType: 'ProviderCompletion' | 'BuyerConfirmation';
  occurredAt: string;
  actorRole: 'Buyer' | 'Provider';
  actorUserId: string;
}>;

export type ServiceRequestCompletionEvidence = Readonly<{
  requestId: string;
  status: string;
  providerCompletion: ServiceRequestCompletionEvidencePhase | null;
  buyerConfirmation: ServiceRequestCompletionEvidencePhase | null;
  notes: readonly never[];
  attachments: readonly never[];
  deliverables: readonly never[];
}>;

export type ServiceRequestAuditEvent = Readonly<{
  eventId: string;
  requestId: string;
  eventType: string;
  status: string;
  occurredAt: string;
  actorRole?: 'Buyer' | 'Provider' | null;
  actorUserId?: string | null;
}>;

export type ChatMessage = Readonly<{
  id: string;
  senderUserId: string;
  senderRole: 'Buyer' | 'Provider';
  messageType: 'Text' | 'System';
  body: string;
  createdAt: string; // ISO 8601
  serviceRequestId: string;
  attachmentId?: string; // Server-issued attachment identifier used to build the secure download route
  attachmentFileName?: string;
}>;

export type ChatHistoryPage = Readonly<{
  requestId: string;
  beforeMessageId: string | null;
  pageSize: number;
  hasMore: boolean;
  nextBeforeMessageId: string | null;
  items: readonly ChatMessage[];
}>;

// Request type - mutable for form state
export interface CreateServiceRequest {
  title: string;
  description: string;
  category?: string;
  startingPrice?: number;
  pricingType?: 'Fixed' | 'Hourly' | 'Package';
  currency?: string;
  serviceImages?: string; // JSON array
  packages?: string; // JSON array
}

// Marketing platform enum values (matches backend MarketingPlatform)
export type MarketingPlatform =
  | 'Facebook'
  | 'Instagram'
  | 'Google'
  | 'TikTok'
  | 'Twitter'
  | 'LinkedIn'
  | 'YouTube'
  | 'Snapchat'
  | 'Email'
  | 'WhatsApp'
  | 'Other';

export interface MarketingEvidence {
  campaignName: string;
  platform: MarketingPlatform;
  startDate: string; // ISO 8601
  endDate: string;   // ISO 8601
}

// Request type - mutable for form state
export interface CreateReviewRequest {
  serviceRequestId: string;
  rating: number;
  comment?: string;
  evidence?: MarketingEvidence;
}

export type ServiceReviewCreationAuthority = Readonly<{
  reviewId: string;
  serviceRequestId: string;
  serviceId: string;
  reviewerUserId: string;
  providerUserId: string;
  rating: number;
  createdAt: string;
}>;

export type MyService = Readonly<{
  serviceId: string;
  title: string;
  description: string;
  category?: string;
  isActive: boolean;
  createdAt: string;
  startingPrice?: number;
  pricingType?: 'Fixed' | 'Hourly' | 'Package';
  currency?: string;
  serviceImages?: string; // JSON array
}>;

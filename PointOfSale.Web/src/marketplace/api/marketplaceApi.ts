import { apiClient } from '../../core/apiClient';
import type {
  MarketplaceProfile,
  MarketplaceProfileStats,
  ServiceSearchRequest,
  ServiceSearchResponse,
  ServiceDetails,
  ServiceRequest,
  ServiceRequestDetails,
  ServiceReview,
  ServiceReviewHistory,
  CreateReviewRequest,
  ServiceReviewCreationAuthority,
  CreateServiceRequest,
  MyService,
} from '../types';
import type { CreatedServiceRequestAuthority } from '../requestCreationAuthority';

/**
 * Marketplace API layer.
 * Pure data fetching - NO business logic.
 * All methods return exactly what backend sends.
 */
export const marketplaceApi = {
  // Profile
  getMyProfile: (): Promise<MarketplaceProfile> => {
    return apiClient.get('/marketplace/me');
  },

  getMyProfileStats: (): Promise<MarketplaceProfileStats> => {
    return apiClient.get('/marketplace/me/stats');
  },

  getServiceReviews: (serviceId: string): Promise<ServiceReviewHistory> => {
    return apiClient.get(`/marketplace/services/${serviceId}/reviews`);
  },

  createProfile: (data: { displayName: string; bio?: string }): Promise<any> => {
    return apiClient.post('/marketplace/profile', data);
  },

  updateProfile: (data: any): Promise<any> => {
    return apiClient.put('/marketplace/profile', data);
  },

  // Search
  searchServices: (request: ServiceSearchRequest): Promise<ServiceSearchResponse> => {
    return apiClient.post('/marketplace/search', request);
  },

  getServiceDetails: (serviceId: string): Promise<ServiceDetails> => {
    return apiClient.get(`/marketplace/services/${serviceId}`);
  },

  // Requests (Buyer)
  requestService: (data: { serviceId: string; requestMessage?: string }): Promise<CreatedServiceRequestAuthority> => {
    return apiClient.post('/marketplace/requests', data);
  },

  getMyRequests: (): Promise<ServiceRequest[]> => {
    return apiClient.get('/marketplace/my-requests');
  },

  confirmCompletion: (requestId: string): Promise<{ status: string }> => {
    return apiClient.post(`/marketplace/requests/${requestId}/confirm`);
  },

  cancelRequest: (requestId: string, reason?: string): Promise<{ status: string }> => {
    return apiClient.post(`/marketplace/requests/${requestId}/cancel`, { reason });
  },

  rejectRequest: (requestId: string): Promise<{ status: string }> => {
    return apiClient.post(`/marketplace/requests/${requestId}/reject`);
  },

  // Requests (Provider)
  getIncomingRequests: (): Promise<ServiceRequest[]> => {
    return apiClient.get('/marketplace/incoming-requests');
  },

  acceptRequest: (requestId: string): Promise<{ status: string }> => {
    return apiClient.post(`/marketplace/requests/${requestId}/accept`);
  },

  markCompleted: (requestId: string): Promise<{ status: string }> => {
    return apiClient.post(`/marketplace/requests/${requestId}/complete`);
  },

  // Request Details
  getRequestDetails: (requestId: string): Promise<ServiceRequestDetails> => {
    return apiClient.get(`/marketplace/requests/${requestId}`);
  },

  // Chat
  getChatMessages: (requestId: string, beforeMessageId?: string | null): Promise<unknown> => {
    const cursor = beforeMessageId
      ? `?beforeMessageId=${encodeURIComponent(beforeMessageId)}`
      : '';
    return apiClient.get(`/marketplace/requests/${requestId}/chat/messages${cursor}`);
  },

  uploadChatAttachment: (requestId: string, file: File): Promise<unknown> => {
    const formData = new FormData();
    formData.append('file', file);
    return apiClient.post(`/marketplace/requests/${requestId}/attachments`, formData);
  },

  // Reviews
  createReview: (data: CreateReviewRequest): Promise<ServiceReviewCreationAuthority> => {
    return apiClient.post('/marketplace/service-reviews', data);
  },

  // My Services (Provider)
  getMyServices: (): Promise<MyService[]> => {
    return apiClient.get('/marketplace/my-services');
  },

  createService: (data: CreateServiceRequest): Promise<any> => {
    return apiClient.post('/marketplace/my-services', data);
  },

  updateService: (serviceId: string, data: { title: string; description: string; category?: string }): Promise<any> => {
    return apiClient.put(`/marketplace/my-services/${serviceId}`, data);
  },

  activateService: (serviceId: string): Promise<{ isActive: boolean }> => {
    return apiClient.post(`/marketplace/my-services/${serviceId}/activate`);
  },

  deactivateService: (serviceId: string): Promise<{ isActive: boolean }> => {
    return apiClient.post(`/marketplace/my-services/${serviceId}/deactivate`);
  },
};

/**
 * Account API Module
 * Phase F2: Storefront Account Area
 * 
 * API calls for account management (profile, security, sessions).
 * All endpoints require authentication.
 */

import { httpClient } from '@/services/api/httpClient';
import type { OrderRefundDto } from '@/types/order';

// API base path
const ACCOUNT_BASE = '/api/storefront/account';

// ============================================================================
// Types
// ============================================================================

export interface ProfileUpdateRequest {
    displayName?: string;
    phone?: string;
}

export interface ProfileUpdateResponse {
    success: boolean;
    message: string;
    customer?: CustomerDto;
}

export interface CustomerDto {
    id: string;
    email: string;
    displayName?: string;
    phone?: string;
    provider: string;
    emailVerified: boolean;
    createdAt: string;
    lastLoginAt?: string;
}

export interface ChangePasswordRequest {
    currentPassword: string;
    newPassword: string;
    confirmPassword: string;
}

export interface ChangePasswordResponse {
    success: boolean;
    message: string;
    sessionsRevoked: boolean;
}

export interface HasPasswordResponse {
    hasPassword: boolean;
    provider: string;
}

export interface SessionDto {
    id: string;
    device: string;
    ipAddress: string;
    createdAt: string;
    lastUsedAt?: string;
    isCurrent: boolean;
}

export interface SessionsListResponse {
    sessions: SessionDto[];
    totalCount: number;
}

export interface RevokeSessionResponse {
    success: boolean;
    message: string;
}

export interface RevokeAllSessionsResponse {
    success: boolean;
    message: string;
    revokedCount: number;
}

export interface AccountStatusResponse {
    isActive: boolean;
    emailVerified: boolean;
    provider: string;
    createdAt: string;
    lastLoginAt?: string;
    activeSessionsCount: number;
}

// ============================================================================
// Profile APIs
// ============================================================================

/**
 * Update customer profile (display name, phone)
 */
export async function updateProfile(
    request: ProfileUpdateRequest
): Promise<ProfileUpdateResponse> {
    const response = await httpClient.patch<ProfileUpdateResponse>(
        `${ACCOUNT_BASE}/profile`,
        request,
        { withCredentials: true }
    );
    return response.data;
}

// ============================================================================
// Security APIs
// ============================================================================

/**
 * Change password (local accounts only)
 * IMPORTANT: This will log out all sessions
 */
export async function changePassword(
    request: ChangePasswordRequest
): Promise<ChangePasswordResponse> {
    const response = await httpClient.post<ChangePasswordResponse>(
        `${ACCOUNT_BASE}/change-password`,
        request,
        { withCredentials: true }
    );
    return response.data;
}

/**
 * Check if customer has a local password
 */
export async function hasPassword(): Promise<HasPasswordResponse> {
    const response = await httpClient.get<HasPasswordResponse>(
        `${ACCOUNT_BASE}/has-password`,
        { withCredentials: true }
    );
    return response.data;
}

// ============================================================================
// Sessions APIs
// ============================================================================

/**
 * Get list of active sessions
 */
export async function getSessions(): Promise<SessionsListResponse> {
    const response = await httpClient.get<SessionsListResponse>(
        `${ACCOUNT_BASE}/sessions`,
        { withCredentials: true }
    );
    return response.data;
}

/**
 * Revoke a specific session
 */
export async function revokeSession(
    sessionId: string
): Promise<RevokeSessionResponse> {
    const response = await httpClient.delete<RevokeSessionResponse>(
        `${ACCOUNT_BASE}/sessions/${sessionId}`,
        { withCredentials: true }
    );
    return response.data;
}

/**
 * Revoke all sessions except current
 */
export async function revokeAllSessions(): Promise<RevokeAllSessionsResponse> {
    const response = await httpClient.delete<RevokeAllSessionsResponse>(
        `${ACCOUNT_BASE}/sessions`,
        { withCredentials: true }
    );
    return response.data;
}

// ============================================================================
// Account Status API
// ============================================================================

/**
 * Get account status information
 */
export async function getAccountStatus(): Promise<AccountStatusResponse> {
    const response = await httpClient.get<AccountStatusResponse>(
        `${ACCOUNT_BASE}/status`,
        { withCredentials: true }
    );
    return response.data;
}

// ============================================================================
// Orders APIs (Phase F2.Orders)
// ============================================================================

export interface CustomerOrderItem {
    productId?: string;
    productName: string;
    sku?: string;
    quantity: number;
    price: number;
    total?: number;
    imageUrl?: string;
}

export interface CustomerOrder {
    id: string;
    orderNumber: string;
    status: string;
    paymentStatus: string;
    total: number;
    currency: string;
    itemCount: number;
    shippingMethodName?: string;
    paymentMethodName?: string;
    createdAt: string;
    items: CustomerOrderItem[];
    cancellationReason?: string;
}

export interface CustomerOrdersResponse {
    orders: CustomerOrder[];
    totalCount: number;
    page: number;
    pageSize: number;
    totalPages: number;
}

export interface CustomerOrderDetail extends CustomerOrder {
    subtotal: number;
    shippingCost: number;
    discountAmount: number;
    taxAmount: number;
    couponCode?: string;
    customerNotes?: string;
    shippingAddress: string;
    shippingCity?: string;
    shippingState?: string;
    shippingZipCode?: string;
    shippingCountry?: string;
    refunds?: OrderRefundDto[];
}

/**
 * Get customer's order history
 */
export async function getOrders(
    page: number = 1,
    pageSize: number = 10
): Promise<CustomerOrdersResponse> {
    const response = await httpClient.get<CustomerOrdersResponse>(
        `${ACCOUNT_BASE}/orders`,
        {
            params: { page, pageSize },
            withCredentials: true,
        }
    );
    return response.data;
}

/**
 * Get a single order by ID
 */
export async function getOrderById(
    orderId: string
): Promise<CustomerOrderDetail> {
    const response = await httpClient.get<CustomerOrderDetail>(
        `${ACCOUNT_BASE}/orders/${orderId}`,
        { withCredentials: true }
    );
    return response.data;
}


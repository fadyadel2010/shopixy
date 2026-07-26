import { customerAuthEndpoints } from "@/api/endpoints";
import { httpClient } from "@/api/http-client";
import { deriveCustomerAuthResponseAuthority, deriveCustomerSessionProfileAuthority } from "@/auth/customer-session-authority";
import type {
  CustomerAuthResponse,
  CustomerChangePasswordRequest,
  CustomerChangeEmailRequest,
  CustomerForgotPasswordRequest,
  CustomerLoginRequest,
  CustomerProfile,
  CustomerRegisterRequest,
  CustomerResendVerificationRequest,
  CustomerResetPasswordRequest,
  CustomerUpdateProfileRequest,
  CustomerVerifyEmailRequest
} from "@/types";

const requireCustomerProfileAuthority = (payload: unknown): CustomerProfile => {
  const customer = deriveCustomerSessionProfileAuthority(payload);
  if (!customer) {
    throw new Error("Customer session authority response was malformed.");
  }

  return customer;
};

const requireCustomerAuthResponseAuthority = (payload: unknown): CustomerAuthResponse => {
  const response = deriveCustomerAuthResponseAuthority(payload);
  if (!response) {
    throw new Error("Customer login authority response was malformed.");
  }

  return response;
};

export const customerAuthService = {
  async register(payload: CustomerRegisterRequest) {
    const response = await httpClient.post<{ message: string }>(customerAuthEndpoints.register, payload);
    return response.data;
  },

  async login(payload: CustomerLoginRequest) {
    const response = await httpClient.post<unknown>(customerAuthEndpoints.login, payload);
    return requireCustomerAuthResponseAuthority(response.data);
  },

  async getProfile(signal?: AbortSignal) {
    const response = await httpClient.get<unknown>(customerAuthEndpoints.me, { signal });
    return requireCustomerProfileAuthority(response.data);
  },

  async logout() {
    await httpClient.post(customerAuthEndpoints.logout, {});
  },

  async forgotPassword(payload: CustomerForgotPasswordRequest) {
    const response = await httpClient.post<{ message: string }>(customerAuthEndpoints.forgotPassword, payload);
    return response.data;
  },

  async resetPassword(payload: CustomerResetPasswordRequest) {
    const response = await httpClient.post<{ message: string }>(customerAuthEndpoints.resetPassword, payload);
    return response.data;
  },

  async verifyEmail(payload: CustomerVerifyEmailRequest) {
    const response = await httpClient.post<{ message: string }>(customerAuthEndpoints.verifyEmail, payload);
    return response.data;
  },

  async resendVerification(payload: CustomerResendVerificationRequest) {
    const response = await httpClient.post<{ message: string }>(customerAuthEndpoints.resendVerification, payload);
    return response.data;
  },

  async updateProfile(payload: CustomerUpdateProfileRequest) {
    const response = await httpClient.put<unknown>(customerAuthEndpoints.updateProfile, payload);
    return requireCustomerProfileAuthority(response.data);
  },

  async changePassword(payload: CustomerChangePasswordRequest) {
    const response = await httpClient.post<{ message: string }>(customerAuthEndpoints.changePassword, payload);
    return response.data;
  },

  async changeEmail(payload: CustomerChangeEmailRequest) {
    const response = await httpClient.post<{ message: string }>(customerAuthEndpoints.changeEmail, payload);
    return response.data;
  },

  async revokeAllSessions() {
    const response = await httpClient.post<{ message: string }>(customerAuthEndpoints.revokeAllSessions, {});
    return response.data;
  }
};

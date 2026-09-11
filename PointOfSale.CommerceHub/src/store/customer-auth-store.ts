import { create } from "zustand";

import { normalizeApiError } from "@/api/api-error";
import { deriveCustomerAuthResponseAuthority } from "@/auth/customer-session-authority";
import { clearActivePaymentRecoveryContext } from "@/features/checkout/activePaymentRecoveryContext";
import { customerAuthService } from "@/services";
import type { CustomerAuthResponse, CustomerProfile } from "@/types";

type CustomerAuthState = {
  expiresAt: string | null;
  customer: CustomerProfile | null;
  hasHydrated: boolean;
  isHydrating: boolean;
  beginRouteAuthorityValidation: () => void;
  setSession: (response: CustomerAuthResponse) => void;
  clearSession: () => void;
  hydrateSession: (signal?: AbortSignal) => Promise<CustomerProfile | null>;
};

let customerAuthorityRevision = 0;
let latestHydrationRequestId = 0;

const clearedSessionState = {
  expiresAt: null,
  customer: null,
  hasHydrated: true,
  isHydrating: false
} satisfies Pick<CustomerAuthState, "expiresAt" | "customer" | "hasHydrated" | "isHydrating">;

export const useCustomerAuthStore = create<CustomerAuthState>((set, get) => ({
  expiresAt: null,
  customer: null,
  hasHydrated: false,
  isHydrating: false,
  beginRouteAuthorityValidation: () => {
    set((state) => ({
      ...state,
      hasHydrated: false,
      isHydrating: true
    }));
  },
  setSession: (response) => {
    customerAuthorityRevision += 1;
    latestHydrationRequestId += 1;
    const authoritativeSession = deriveCustomerAuthResponseAuthority(response);
    if (!authoritativeSession) {
      clearActivePaymentRecoveryContext();
      set(clearedSessionState);
      return;
    }

    clearActivePaymentRecoveryContext();
    set({
      expiresAt: authoritativeSession.expiresAt,
      customer: authoritativeSession.customer,
      hasHydrated: true,
      isHydrating: false
    });
  },
  clearSession: () => {
    customerAuthorityRevision += 1;
    latestHydrationRequestId += 1;
    // Recovery context is browser-visible continuity state and must not outlive
    // the customer session boundary on shared or expired-auth browsers.
    clearActivePaymentRecoveryContext();
    set(clearedSessionState);
  },
  hydrateSession: async (signal) => {
    const hydrationRequestId = ++latestHydrationRequestId;
    const authorityRevisionAtStart = customerAuthorityRevision;
    set((state) => ({
      ...state,
      isHydrating: true
    }));

    try {
      const customer = await customerAuthService.getProfile(signal);
      
      if (
        signal?.aborted ||
        hydrationRequestId !== latestHydrationRequestId ||
        authorityRevisionAtStart !== customerAuthorityRevision
      ) {
        set((state) => ({
          ...state,
          isHydrating: false
        }));
        return get().customer;
      }
      
      set((state) => ({
        ...state,
        customer,
        hasHydrated: true,
        isHydrating: false
      }));
      return customer;
    } catch (error) {
      if (
        signal?.aborted ||
        hydrationRequestId !== latestHydrationRequestId ||
        authorityRevisionAtStart !== customerAuthorityRevision
      ) {
        set((state) => ({
          ...state,
          isHydrating: false
        }));
        return get().customer;
      }

      const apiError = normalizeApiError(error);
      if (apiError.status === 401 || apiError.status === 403) {
        customerAuthorityRevision += 1;
        clearActivePaymentRecoveryContext();
        set(clearedSessionState);
        return null;
      }

      set((state) => ({
        ...state,
        hasHydrated: true,
        isHydrating: false
      }));
      return get().customer;
    }
  }
}));

export const selectCustomerAuthHydrated = (state: CustomerAuthState) => state.hasHydrated;

export const selectIsCustomerAuthenticated = (state: CustomerAuthState) => Boolean(state.customer);

export const selectCustomerProfile = (state: CustomerAuthState) => state.customer;

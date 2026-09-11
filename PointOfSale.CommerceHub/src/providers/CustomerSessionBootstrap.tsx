import { useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";

import { CUSTOMER_SESSION_AUTHORITY_LOST_EVENT, CUSTOMER_SESSION_LOGOUT_STORAGE_KEY } from "@/auth/customer-session-authority";
import { customerQueryKeys } from "@/features/customer/queries";
import { useCustomerAuthStore } from "@/store";

const CUSTOMER_SESSION_REVALIDATION_INTERVAL_MS = 30_000;

export const CustomerSessionBootstrap = () => {
  const queryClient = useQueryClient();
  const hydrateSession = useCustomerAuthStore((state) => state.hydrateSession);
  const clearSession = useCustomerAuthStore((state) => state.clearSession);
  const beginRouteAuthorityValidation = useCustomerAuthStore((state) => state.beginRouteAuthorityValidation);

  useEffect(() => {
    const controller = new AbortController();

    const purgeCustomerAuthority = () => {
      clearSession();
      queryClient.removeQueries({
        queryKey: customerQueryKeys.all
      });
    };

    const revalidateSession = async (withholdRoutePresentation = false) => {
      if (withholdRoutePresentation) {
        beginRouteAuthorityValidation();
      }

      const previousCustomerId = useCustomerAuthStore.getState().customer?.id ?? null;
      const customer = await hydrateSession(controller.signal);
      if (controller.signal.aborted) return;

      if (customer) {
        if (previousCustomerId && previousCustomerId !== customer.id) {
          queryClient.removeQueries({
            queryKey: customerQueryKeys.all
          });
        }
        queryClient.setQueryData(customerQueryKeys.profile(), customer);
      } else {
        purgeCustomerAuthority();
      }
    };

    const handleStorage = (event: StorageEvent) => {
      if (event.key === CUSTOMER_SESSION_LOGOUT_STORAGE_KEY && event.oldValue !== event.newValue) {
        purgeCustomerAuthority();
      }
    };

    const handleVisibility = () => {
      if (document.visibilityState === "visible") void revalidateSession();
    };

    const handleBackgroundRevalidation = () => {
      void revalidateSession();
    };

    const handleHistoryNavigation = () => {
      void revalidateSession(true);
    };

    const handlePageShow = (event: PageTransitionEvent) => {
      if (event.persisted) {
        void revalidateSession(true);
      }
    };

    void revalidateSession();
    const intervalId = window.setInterval(() => void revalidateSession(), CUSTOMER_SESSION_REVALIDATION_INTERVAL_MS);
    window.addEventListener("focus", handleBackgroundRevalidation);
    window.addEventListener("online", handleBackgroundRevalidation);
    window.addEventListener("popstate", handleHistoryNavigation);
    window.addEventListener("pageshow", handlePageShow);
    window.addEventListener("storage", handleStorage);
    window.addEventListener(CUSTOMER_SESSION_AUTHORITY_LOST_EVENT, purgeCustomerAuthority);
    document.addEventListener("visibilitychange", handleVisibility);

    return () => {
      controller.abort();
      window.clearInterval(intervalId);
      window.removeEventListener("focus", handleBackgroundRevalidation);
      window.removeEventListener("online", handleBackgroundRevalidation);
      window.removeEventListener("popstate", handleHistoryNavigation);
      window.removeEventListener("pageshow", handlePageShow);
      window.removeEventListener("storage", handleStorage);
      window.removeEventListener(CUSTOMER_SESSION_AUTHORITY_LOST_EVENT, purgeCustomerAuthority);
      document.removeEventListener("visibilitychange", handleVisibility);
    };
  }, [beginRouteAuthorityValidation, clearSession, hydrateSession, queryClient]);

  return null;
};

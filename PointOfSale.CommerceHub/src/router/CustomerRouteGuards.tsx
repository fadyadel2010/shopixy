import { Navigate, Outlet, useLocation } from "react-router-dom";

import { customerHasNavigationCapability } from "@/auth/customer-session-authority";
import { selectCustomerAuthHydrated, selectIsCustomerAuthenticated, useCustomerAuthStore } from "@/store";
import type { CustomerNavigationCapability } from "@/types";

export const resolveCustomerRouteRedirect = (value: unknown, fallback = "/customer") => {
  if (typeof value !== "string" || !value.startsWith("/") || value.startsWith("//") || value.includes("\\")) {
    return fallback;
  }

  try {
    const url = new URL(value, window.location.origin);
    if (url.origin !== window.location.origin) {
      return fallback;
    }

    return `${url.pathname}${url.search}${url.hash}`;
  } catch {
    return fallback;
  }
};

const AuthLoadingFallback = () => (
  <div className="flex items-center justify-center min-h-screen">
    <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-gray-900"></div>
  </div>
);

export const RequireCustomerAuth = () => {
  const location = useLocation();
  const hasHydrated = useCustomerAuthStore(selectCustomerAuthHydrated);
  const isAuthenticated = useCustomerAuthStore(selectIsCustomerAuthenticated);

  if (!hasHydrated) {
    return <AuthLoadingFallback />;
  }

  if (!isAuthenticated) {
    return (
      <Navigate
        to="/login"
        replace
        state={{
          redirectTo: `${location.pathname}${location.search}${location.hash}`
        }}
      />
    );
  }

  return <Outlet />;
};

export const RedirectAuthenticatedCustomers = () => {
  const hasHydrated = useCustomerAuthStore(selectCustomerAuthHydrated);
  const isAuthenticated = useCustomerAuthStore(selectIsCustomerAuthenticated);

  if (!hasHydrated) {
    return <AuthLoadingFallback />;
  }

  if (isAuthenticated) {
    return <Navigate to="/customer" replace />;
  }

  return <Outlet />;
};

export const RequireCustomerNavigationCapability = ({
  capability
}: {
  capability: CustomerNavigationCapability;
}) => {
  const customer = useCustomerAuthStore((state) => state.customer);

  if (!customerHasNavigationCapability(customer, capability)) {
    return <Navigate to="/" replace />;
  }

  return <Outlet />;
};

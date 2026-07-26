import { useQueryClient } from "@tanstack/react-query";
import { CircleUserRound, LogOut, PackageSearch, RotateCcw } from "lucide-react";
import { Link, NavLink, useNavigate } from "react-router-dom";

import { CartButton } from "@/cart/CartButton";
import { LanguageSwitcher } from "@/components/layout/LanguageSwitcher";
import { clearActivePaymentRecoveryContext } from "@/features/checkout/activePaymentRecoveryContext";
import { broadcastCustomerLogout, customerHasNavigationCapability } from "@/auth/customer-session-authority";
import { customerQueryKeys } from "@/features/customer/queries";
import { brand } from "@/shared/brand";
import { Logo } from "@/shared/Logo";
import { SearchBar } from "@/search/components/SearchBar";
import { customerAuthService } from "@/services";
import { selectCustomerProfile, selectIsCustomerAuthenticated, useCustomerAuthStore } from "@/store";
import { Button } from "@/ui/button";

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  [
    "rounded-full px-3 py-2 text-sm font-semibold transition-all",
    isActive
      ? "bg-secondary text-primary shadow-soft"
      : "text-muted-foreground hover:bg-white/70 hover:text-foreground"
  ].join(" ");

export const Header = () => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const customer = useCustomerAuthStore(selectCustomerProfile);
  const isAuthenticated = useCustomerAuthStore(selectIsCustomerAuthenticated);
  const clearSession = useCustomerAuthStore((state) => state.clearSession);
  const canViewOrders = customerHasNavigationCapability(customer, "customer.orders.view");
  const canViewRefunds = customerHasNavigationCapability(customer, "customer.refunds.view");
  const canViewAccount = customerHasNavigationCapability(customer, "customer.account.view");

  const handleLogout = async () => {
    try {
      await customerAuthService.logout();
    } catch {
      // Clear client state even if the server-side logout call fails.
    }

    clearSession();
    broadcastCustomerLogout();
    clearActivePaymentRecoveryContext();
    queryClient.removeQueries({
      queryKey: customerQueryKeys.all
    });
    navigate("/");
  };

  return (
    <header className="sticky top-0 z-50 border-b border-white/60 bg-background/80 backdrop-blur-xl">
      <div className="container py-3">
        <div className="surface-panel grid gap-3 px-4 py-4 md:grid-cols-[auto_minmax(0,1fr)_auto] md:items-center md:gap-6 md:px-5">
          <div className="flex items-center justify-between gap-3">
            <Link to="/" aria-label={`${brand.fullName} home`} className="shrink-0">
              <Logo />
            </Link>

            <div className="flex items-center gap-2 md:hidden">
              <LanguageSwitcher />
              <CartButton compact />
              <Button asChild variant="ghost" size="icon" aria-label={isAuthenticated ? "Customer account" : "Sign in"}>
                <Link to={isAuthenticated && canViewAccount ? "/customer" : "/login"}>
                  <CircleUserRound className="h-5 w-5" />
                </Link>
              </Button>
              {isAuthenticated ? (
                <Button type="button" variant="ghost" size="icon" aria-label="Sign out" onClick={() => void handleLogout()}>
                  <LogOut className="h-5 w-5" />
                </Button>
              ) : null}
            </div>
          </div>

          <div className="flex flex-col gap-3 md:min-w-0">
            <SearchBar placeholder="Search Shopixy products, brands, and categories" />
            <nav className="hidden items-center gap-2 md:flex">
              <NavLink to="/" end className={navLinkClass}>
                Home
              </NavLink>
              <NavLink to="/products" className={navLinkClass}>
                Products
              </NavLink>
              <NavLink to="/cart" className={navLinkClass}>
                Cart
              </NavLink>
              {isAuthenticated ? (
                <>
                  {canViewOrders ? <NavLink to="/orders" className={navLinkClass}>
                    Orders
                  </NavLink> : null}
                  {canViewRefunds ? <NavLink to="/customer/refunds" className={navLinkClass}>
                    Refunds
                  </NavLink> : null}
                  {canViewAccount ? <NavLink to="/customer" className={navLinkClass}>
                    Account
                  </NavLink> : null}
                </>
              ) : null}
            </nav>
          </div>

          <div className="hidden items-center justify-end gap-2 md:flex">
            <LanguageSwitcher />
            <CartButton />
            {isAuthenticated ? (
              <>
                {canViewOrders ? <Button asChild variant="ghost" className="gap-2 rounded-full px-4">
                  <Link to="/orders">
                    <PackageSearch className="h-4 w-4" />
                    My Orders
                  </Link>
                </Button> : null}
                {canViewRefunds ? <Button asChild variant="ghost" className="gap-2 rounded-full px-4">
                  <Link to="/customer/refunds">
                    <RotateCcw className="h-4 w-4" />
                    Refunds
                  </Link>
                </Button> : null}
                {canViewAccount ? <Button asChild variant="ghost" className="gap-2 rounded-full px-4">
                  <Link to="/customer">
                    <CircleUserRound className="h-4 w-4" />
                    {customer?.name.split(" ")[0] ?? "Account"}
                  </Link>
                </Button> : null}
                <Button type="button" variant="outline" className="gap-2 rounded-full px-4" onClick={() => void handleLogout()}>
                  <LogOut className="h-4 w-4" />
                  Sign out
                </Button>
              </>
            ) : (
              <>
                <Button asChild variant="ghost" className="gap-2 rounded-full px-4">
                  <Link to="/login">
                    <CircleUserRound className="h-4 w-4" />
                    Sign in
                  </Link>
                </Button>
                <Button asChild className="gap-2 rounded-full px-4">
                  <Link to="/register">Create account</Link>
                </Button>
              </>
            )}
          </div>
        </div>
      </div>
    </header>
  );
};

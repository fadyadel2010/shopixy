import { ArrowRight, LockKeyhole, Package, ShieldCheck, UserRound } from "lucide-react";
import { FormEvent, useMemo, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";

import { normalizeApiError } from "@/api";
import { isSameCustomerActorAuthority } from "@/auth/customer-session-authority";
import { customerQueryKeys } from "@/features/customer/queries";
import { resolveCustomerRouteRedirect } from "@/router/CustomerRouteGuards";
import { customerAuthService } from "@/services";
import { useCustomerAuthStore, useToastStore } from "@/store";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { Input } from "@/ui/input";
import { useDocumentTitle } from "@/utils";

type LoginFormState = {
  email: string;
  password: string;
};

type RedirectState = {
  redirectTo?: string;
};

const benefits = [
  "Track marketplace orders across all vendors in one place.",
  "Review item-level delivery progress as stores ship independently.",
  "Request item-level refunds and track their status from one customer account."
];

export const CustomerLoginPage = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const showToast = useToastStore((state) => state.showToast);
  const setSession = useCustomerAuthStore((state) => state.setSession);
  const clearSession = useCustomerAuthStore((state) => state.clearSession);
  const [form, setForm] = useState<LoginFormState>({
    email: "",
    password: ""
  });
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useDocumentTitle("Sign In | CommerceHub");

  const redirectState = location.state as RedirectState | null;
  const redirectTo = useMemo(
    () => resolveCustomerRouteRedirect(redirectState?.redirectTo),
    [redirectState?.redirectTo]
  );

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    setIsSubmitting(true);

    try {
      const response = await customerAuthService.login({
        email: form.email.trim(),
        password: form.password
      });
      const customer = await customerAuthService.getProfile();
      if (!isSameCustomerActorAuthority(response.customer, customer)) {
        throw new Error("Customer actor changed while login authority was being established.");
      }

      queryClient.removeQueries({
        queryKey: customerQueryKeys.all
      });
      setSession({ expiresAt: response.expiresAt, customer });
      queryClient.setQueryData(customerQueryKeys.profile(), customer);
      await queryClient.invalidateQueries({
        queryKey: customerQueryKeys.all
      });

      showToast({
        title: "Welcome back",
        description: "Your Shopixy account is ready and your orders are synced.",
        tone: "success"
      });

      navigate(redirectTo, { replace: true });
    } catch (requestError) {
      clearSession();
      queryClient.removeQueries({
        queryKey: customerQueryKeys.all
      });
      const apiError = normalizeApiError(requestError);
      setError(apiError.message);
    } finally {
      setIsSubmitting(false);
    }
  };

  const resendVerification = async () => {
    setError(null);
    try {
      const response = await customerAuthService.resendVerification({ email: form.email.trim() });
      showToast({ title: "Verification email requested", description: response.message, tone: "success" });
    } catch (requestError) {
      setError(normalizeApiError(requestError).message);
    }
  };

  return (
    <div className="grid gap-6 lg:grid-cols-[minmax(0,1.15fr)_minmax(0,0.85fr)]">
      <Card className="overflow-hidden border-white/70 bg-white/90">
        <CardContent className="p-0">
          <div className="grid gap-0 md:grid-cols-[1.1fr_0.9fr]">
            <div className="space-y-6 bg-[linear-gradient(135deg,rgba(15,91,216,0.94),rgba(30,64,175,0.92))] px-6 py-8 text-white md:px-8 md:py-10">
              <div className="inline-flex w-fit items-center gap-2 rounded-full bg-white/12 px-4 py-2 text-sm font-semibold">
                <ShieldCheck className="h-4 w-4" />
                Customer account
              </div>

              <div className="space-y-3">
                <h1 className="font-display text-4xl leading-tight md:text-5xl">Keep every Shopixy order in one secure place.</h1>
                <p className="max-w-xl text-sm leading-7 text-white/85 md:text-base">
                  Sign in once to follow multi-vendor orders, check item delivery status, and submit refund requests for delivered items.
                </p>
              </div>

              <div className="grid gap-3">
                {benefits.map((benefit) => (
                  <div key={benefit} className="rounded-[1.5rem] border border-white/15 bg-white/10 px-4 py-3 text-sm leading-6">
                    {benefit}
                  </div>
                ))}
              </div>
            </div>

            <div className="space-y-6 px-6 py-8 md:px-8 md:py-10">
              <div className="space-y-2">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Sign In</p>
                <h2 className="text-3xl font-black tracking-tight text-foreground">Continue your customer account</h2>
                <p className="text-sm leading-6 text-muted-foreground">Use your email and password to open your orders dashboard and checkout securely.</p>
              </div>

              <form className="space-y-4" onSubmit={handleSubmit}>
                <label className="block space-y-2">
                  <span className="text-sm font-semibold text-foreground">Email</span>
                  <Input
                    type="email"
                    autoComplete="email"
                    placeholder="you@example.com"
                    value={form.email}
                    onChange={(event) =>
                      setForm((current) => ({
                        ...current,
                        email: event.target.value
                      }))
                    }
                    disabled={isSubmitting}
                  />
                </label>

                <label className="block space-y-2">
                  <span className="text-sm font-semibold text-foreground">Password</span>
                  <Input
                    type="password"
                    autoComplete="current-password"
                    placeholder="Enter your password"
                    value={form.password}
                    onChange={(event) =>
                      setForm((current) => ({
                        ...current,
                        password: event.target.value
                      }))
                    }
                    disabled={isSubmitting}
                  />
                </label>

                {error ? <p className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">{error}</p> : null}

                <Button
                  type="submit"
                  size="lg"
                  className="w-full justify-between"
                  disabled={isSubmitting || !form.email.trim() || !form.password}
                >
                  {isSubmitting ? "Signing in..." : "Sign in"}
                  <ArrowRight className="h-4 w-4" />
                </Button>
              </form>

              <div className="grid gap-3 rounded-[1.5rem] border border-border/70 bg-slate-50/80 p-4 text-sm text-muted-foreground">
                <div className="flex items-center gap-3">
                  <UserRound className="h-4 w-4 text-primary" />
                  Customer-only access protects your order history.
                </div>
                <div className="flex items-center gap-3">
                  <Package className="h-4 w-4 text-primary" />
                  Orders stay grouped by vendor while remaining visible in one account.
                </div>
                <div className="flex items-center gap-3">
                  <LockKeyhole className="h-4 w-4 text-primary" />
                  Checkout now uses customer JWT authentication for ownership validation.
                </div>
              </div>

              <p className="text-sm text-muted-foreground">
                <Link to="/forgot-password" className="font-semibold text-primary hover:underline">
                  Forgot your password?
                </Link>
              </p>

              <Button type="button" variant="ghost" className="px-0 text-primary" disabled={!form.email.trim()} onClick={() => void resendVerification()}>
                Resend verification email
              </Button>

              <p className="text-sm text-muted-foreground">
                New to Shopixy?{" "}
                <Link to="/register" state={{ redirectTo }} className="font-semibold text-primary hover:underline">
                  Create your account
                </Link>
              </p>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  );
};

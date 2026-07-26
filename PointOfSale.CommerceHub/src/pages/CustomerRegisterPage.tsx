import { ArrowRight, CheckCircle2, UserPlus, WalletCards } from "lucide-react";
import { FormEvent, useMemo, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";

import { normalizeApiError } from "@/api";
import { customerAuthService } from "@/services";
import { resolveCustomerRouteRedirect } from "@/router/CustomerRouteGuards";
import { useToastStore } from "@/store";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { Input } from "@/ui/input";
import { useDocumentTitle } from "@/utils";

type RegisterFormState = {
  name: string;
  email: string;
  phoneNumber: string;
  password: string;
  confirmPassword: string;
};

type RedirectState = {
  redirectTo?: string;
};

const highlights = [
  "Unified order history across every vendor you buy from.",
  "Faster repeat checkout with your saved account identity.",
  "Refund-ready order ownership for the next Shopixy phase."
];

export const CustomerRegisterPage = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const showToast = useToastStore((state) => state.showToast);
  const [form, setForm] = useState<RegisterFormState>({
    name: "",
    email: "",
    phoneNumber: "",
    password: "",
    confirmPassword: ""
  });
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useDocumentTitle("Register | CommerceHub");

  const redirectState = location.state as RedirectState | null;
  const redirectTo = useMemo(
    () => resolveCustomerRouteRedirect(redirectState?.redirectTo),
    [redirectState?.redirectTo]
  );

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);

    if (form.password.trim().length < 8) {
      setError("Password must be at least 8 characters.");
      return;
    }

    if (form.password !== form.confirmPassword) {
      setError("Password confirmation does not match.");
      return;
    }

    setIsSubmitting(true);

    try {
      const response = await customerAuthService.register({
        name: form.name.trim(),
        email: form.email.trim(),
        phoneNumber: form.phoneNumber.trim() || undefined,
        password: form.password
      });

      showToast({
        title: "Account created",
        description: response.message,
        tone: "success"
      });

      navigate("/login", { replace: true, state: { redirectTo } });
    } catch (requestError) {
      const apiError = normalizeApiError(requestError);
      setError(apiError.message);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="grid gap-6 lg:grid-cols-[minmax(0,0.95fr)_minmax(0,1.05fr)]">
      <Card className="border-white/70 bg-[linear-gradient(160deg,rgba(255,255,255,0.98),rgba(244,248,255,0.92))]">
        <CardContent className="space-y-6 px-6 py-8 md:px-8 md:py-10">
          <div className="space-y-2">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-primary">Create Account</p>
            <h1 className="font-display text-4xl leading-tight text-foreground md:text-5xl">Start your Shopixy customer profile.</h1>
            <p className="text-sm leading-7 text-muted-foreground md:text-base">
              This account is separate from vendor and Super Admin access so your marketplace orders stay clearly owned, protected, and easy to follow.
            </p>
          </div>

          <div className="grid gap-3">
            {highlights.map((highlight) => (
              <div key={highlight} className="flex items-start gap-3 rounded-[1.5rem] border border-border/70 bg-white/85 px-4 py-3">
                <div className="mt-0.5 rounded-full bg-emerald-100 p-1 text-emerald-700">
                  <CheckCircle2 className="h-4 w-4" />
                </div>
                <p className="text-sm leading-6 text-foreground">{highlight}</p>
              </div>
            ))}
          </div>

          <div className="grid gap-3 rounded-[1.75rem] border border-border/70 bg-slate-50/80 p-5 text-sm text-muted-foreground">
            <div className="flex items-center gap-3">
              <UserPlus className="h-4 w-4 text-primary" />
              One account follows every multi-vendor order you place.
            </div>
            <div className="flex items-center gap-3">
              <WalletCards className="h-4 w-4 text-primary" />
              Shopixy now supports customer-owned refund requests without exposing approval or payout actions to vendors.
            </div>
          </div>
        </CardContent>
      </Card>

      <Card className="border-white/70 bg-white/92">
        <CardContent className="space-y-6 px-6 py-8 md:px-8 md:py-10">
          <div className="space-y-2">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Registration</p>
            <h2 className="text-3xl font-black tracking-tight text-foreground">Create your customer credentials</h2>
            <p className="text-sm leading-6 text-muted-foreground">Use an email you can come back to whenever you need order updates.</p>
          </div>

          <form className="space-y-4" onSubmit={handleSubmit}>
            <label className="block space-y-2">
              <span className="text-sm font-semibold text-foreground">Full name</span>
              <Input
                type="text"
                autoComplete="name"
                placeholder="Enter your full name"
                value={form.name}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    name: event.target.value
                  }))
                }
                disabled={isSubmitting}
              />
            </label>

            <div className="grid gap-4 md:grid-cols-2">
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
                <span className="text-sm font-semibold text-foreground">Phone number</span>
                <Input
                  type="tel"
                  autoComplete="tel"
                  placeholder="+20 10 1234 5678"
                  value={form.phoneNumber}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      phoneNumber: event.target.value
                    }))
                  }
                  disabled={isSubmitting}
                />
              </label>
            </div>

            <div className="grid gap-4 md:grid-cols-2">
              <label className="block space-y-2">
                <span className="text-sm font-semibold text-foreground">Password</span>
                <Input
                  type="password"
                  autoComplete="new-password"
                  placeholder="At least 8 characters"
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

              <label className="block space-y-2">
                <span className="text-sm font-semibold text-foreground">Confirm password</span>
                <Input
                  type="password"
                  autoComplete="new-password"
                  placeholder="Repeat your password"
                  value={form.confirmPassword}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      confirmPassword: event.target.value
                    }))
                  }
                  disabled={isSubmitting}
                />
              </label>
            </div>

            {error ? <p className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">{error}</p> : null}

            <Button
              type="submit"
              size="lg"
              className="w-full justify-between"
              disabled={isSubmitting || !form.name.trim() || !form.email.trim() || !form.password || !form.confirmPassword}
            >
              {isSubmitting ? "Creating account..." : "Create account"}
              <ArrowRight className="h-4 w-4" />
            </Button>
          </form>

          <p className="text-sm text-muted-foreground">
            Already have an account?{" "}
            <Link to="/login" state={{ redirectTo }} className="font-semibold text-primary hover:underline">
              Sign in
            </Link>
          </p>
        </CardContent>
      </Card>
    </div>
  );
};

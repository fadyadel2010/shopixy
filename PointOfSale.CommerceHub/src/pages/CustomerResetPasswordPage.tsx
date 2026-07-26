import { ArrowRight, CheckCircle2, ShieldCheck } from "lucide-react";
import { FormEvent, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router-dom";

import { normalizeApiError } from "@/api";
import { broadcastCustomerLogout } from "@/auth/customer-session-authority";
import { customerQueryKeys } from "@/features/customer/queries";
import { customerAuthService } from "@/services";
import { useCustomerAuthStore } from "@/store";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { Input } from "@/ui/input";
import { useDocumentTitle } from "@/utils";

export const CustomerResetPasswordPage = () => {
  const [searchParams] = useSearchParams();
  const token = searchParams.get("token") ?? "";
  const queryClient = useQueryClient();
  const clearSession = useCustomerAuthStore((state) => state.clearSession);

  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isSuccess, setIsSuccess] = useState(false);

  useDocumentTitle("Reset Password | CommerceHub");

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);

    if (newPassword.trim().length < 8) {
      setError("Password must be at least 8 characters.");
      return;
    }

    if (newPassword !== confirmPassword) {
      setError("Password confirmation does not match.");
      return;
    }

    setIsSubmitting(true);

    try {
      await customerAuthService.resetPassword({ token, newPassword });
      clearSession();
      broadcastCustomerLogout();
      queryClient.removeQueries({
        queryKey: customerQueryKeys.all
      });
      setIsSuccess(true);
    } catch (requestError) {
      const apiError = normalizeApiError(requestError);
      setError(apiError.message);
    } finally {
      setIsSubmitting(false);
    }
  };

  if (isSuccess) {
    return (
      <div className="mx-auto max-w-lg">
        <Card className="border-white/70 bg-white/92">
          <CardContent className="space-y-6 px-6 py-10 text-center md:px-10">
            <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-full bg-emerald-100">
              <CheckCircle2 className="h-8 w-8 text-emerald-600" />
            </div>
            <div className="space-y-2">
              <h1 className="font-display text-3xl font-black tracking-tight text-foreground">Password reset complete</h1>
              <p className="text-sm leading-7 text-muted-foreground">
                Your password has been updated and all existing sessions have been revoked. Sign in with your new password to continue.
              </p>
            </div>
            <Link to="/login">
              <Button size="lg" className="gap-2">
                Sign in
                <ArrowRight className="h-4 w-4" />
              </Button>
            </Link>
          </CardContent>
        </Card>
      </div>
    );
  }

  if (!token) {
    return (
      <div className="mx-auto max-w-lg">
        <Card className="border-white/70 bg-white/92">
          <CardContent className="space-y-4 px-6 py-8 text-center md:px-8">
            <h1 className="text-2xl font-black tracking-tight text-foreground">Invalid reset link</h1>
            <p className="text-sm text-muted-foreground">This password reset link appears to be invalid or incomplete.</p>
            <Link to="/forgot-password" className="inline-flex text-sm font-semibold text-primary hover:underline">
              Request a new reset link
            </Link>
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-lg">
      <Card className="border-white/70 bg-white/92">
        <CardContent className="space-y-6 px-6 py-8 md:px-8 md:py-10">
          <div className="space-y-2">
            <div className="inline-flex w-fit items-center gap-2 rounded-full bg-primary/10 px-4 py-2 text-xs font-semibold text-primary">
              <ShieldCheck className="h-4 w-4" />
              Secure reset
            </div>
            <h1 className="font-display text-3xl font-black tracking-tight text-foreground">Choose a new password</h1>
            <p className="text-sm leading-6 text-muted-foreground">
              Enter and confirm your new password. All existing sessions will be revoked for security.
            </p>
          </div>

          <form className="space-y-4" onSubmit={handleSubmit}>
            <label className="block space-y-2">
              <span className="text-sm font-semibold text-foreground">New password</span>
              <Input
                type="password"
                autoComplete="new-password"
                placeholder="At least 8 characters"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                disabled={isSubmitting}
              />
            </label>

            <label className="block space-y-2">
              <span className="text-sm font-semibold text-foreground">Confirm new password</span>
              <Input
                type="password"
                autoComplete="new-password"
                placeholder="Repeat your new password"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                disabled={isSubmitting}
              />
            </label>

            {error ? <p className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">{error}</p> : null}

            <Button
              type="submit"
              size="lg"
              className="w-full justify-between"
              disabled={isSubmitting || !newPassword || !confirmPassword}
            >
              {isSubmitting ? "Resetting..." : "Reset password"}
              <ArrowRight className="h-4 w-4" />
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
};

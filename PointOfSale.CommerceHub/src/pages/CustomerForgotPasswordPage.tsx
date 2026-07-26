import { ArrowRight, KeyRound, MailCheck } from "lucide-react";
import { FormEvent, useState } from "react";
import { Link } from "react-router-dom";

import { normalizeApiError } from "@/api";
import { customerAuthService } from "@/services";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { Input } from "@/ui/input";
import { useDocumentTitle } from "@/utils";

export const CustomerForgotPasswordPage = () => {
  const [email, setEmail] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isSuccess, setIsSuccess] = useState(false);

  useDocumentTitle("Forgot Password | CommerceHub");

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    setIsSubmitting(true);

    try {
      await customerAuthService.forgotPassword({ email: email.trim() });
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
              <MailCheck className="h-8 w-8 text-emerald-600" />
            </div>
            <div className="space-y-2">
              <h1 className="font-display text-3xl font-black tracking-tight text-foreground">Check your email</h1>
              <p className="text-sm leading-7 text-muted-foreground">
                If an account exists with that email, we&apos;ve sent a password reset link. Check your inbox and follow the instructions.
              </p>
            </div>
            <Link to="/login" className="inline-flex text-sm font-semibold text-primary hover:underline">
              Back to sign in
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
              <KeyRound className="h-4 w-4" />
              Password recovery
            </div>
            <h1 className="font-display text-3xl font-black tracking-tight text-foreground">Reset your password</h1>
            <p className="text-sm leading-6 text-muted-foreground">
              Enter the email address linked to your Shopixy account and we&apos;ll send you a reset link.
            </p>
          </div>

          <form className="space-y-4" onSubmit={handleSubmit}>
            <label className="block space-y-2">
              <span className="text-sm font-semibold text-foreground">Email</span>
              <Input
                type="email"
                autoComplete="email"
                placeholder="you@example.com"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                disabled={isSubmitting}
              />
            </label>

            {error ? <p className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">{error}</p> : null}

            <Button
              type="submit"
              size="lg"
              className="w-full justify-between"
              disabled={isSubmitting || !email.trim()}
            >
              {isSubmitting ? "Sending..." : "Send reset link"}
              <ArrowRight className="h-4 w-4" />
            </Button>
          </form>

          <p className="text-sm text-muted-foreground">
            Remember your password?{" "}
            <Link to="/login" className="font-semibold text-primary hover:underline">
              Sign in
            </Link>
          </p>
        </CardContent>
      </Card>
    </div>
  );
};

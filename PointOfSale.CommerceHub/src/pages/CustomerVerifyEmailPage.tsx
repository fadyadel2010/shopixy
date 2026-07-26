import { CheckCircle2, MailCheck } from "lucide-react";
import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";

import { normalizeApiError } from "@/api";
import { customerAuthService } from "@/services";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { useDocumentTitle } from "@/utils";

export const CustomerVerifyEmailPage = () => {
  const [searchParams] = useSearchParams();
  const token = searchParams.get("token") ?? "";
  const [message, setMessage] = useState(token ? "Verifying your email..." : "This verification link is incomplete.");
  const [isSuccess, setIsSuccess] = useState(false);

  useDocumentTitle("Verify Email | CommerceHub");

  useEffect(() => {
    if (!token) {
      return;
    }

    void customerAuthService
      .verifyEmail({ token })
      .then((response) => {
        setIsSuccess(true);
        setMessage(response.message);
      })
      .catch((error) => setMessage(normalizeApiError(error).message));
  }, [token]);

  return (
    <div className="mx-auto max-w-lg">
      <Card className="border-white/70 bg-white/92">
        <CardContent className="space-y-6 px-6 py-10 text-center md:px-10">
          <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-full bg-primary/10">
            {isSuccess ? <CheckCircle2 className="h-8 w-8 text-emerald-600" /> : <MailCheck className="h-8 w-8 text-primary" />}
          </div>
          <div className="space-y-2">
            <h1 className="font-display text-3xl font-black tracking-tight text-foreground">
              {isSuccess ? "Email verified" : "Email verification"}
            </h1>
            <p className="text-sm leading-7 text-muted-foreground">{message}</p>
          </div>
          <Button asChild>
            <Link to={isSuccess ? "/customer/settings" : "/login"}>{isSuccess ? "Open account settings" : "Back to sign in"}</Link>
          </Button>
        </CardContent>
      </Card>
    </div>
  );
};

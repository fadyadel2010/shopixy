import { AlertTriangle, CreditCard, LoaderCircle, ShieldCheck } from "lucide-react";
import { useEffect, useRef, useState } from "react";

import { Button } from "@/ui/button";
import { formatCurrency } from "@/utils";

type EmbeddedStripePaymentFormProps = {
  orderNumber: string;
  amount: number;
  currency: string;
  clientSecret: string;
  publishableKey: string;
  onPaymentPending: (message: string) => void;
  onPaymentError: (message: string) => void;
};

type StripeElement = {
  mount: (selectorOrElement: string | HTMLElement) => void;
  destroy: () => void;
};

type StripeElements = {
  create: (type: "payment", options?: Record<string, unknown>) => StripeElement;
};

type StripeConfirmResult = {
  error?: {
    message?: string;
  };
  paymentIntent?: {
    status?: string;
  };
};

type StripeInstance = {
  elements: (options: Record<string, unknown>) => StripeElements;
  confirmPayment: (options: Record<string, unknown>) => Promise<StripeConfirmResult>;
};

declare global {
  interface Window {
    Stripe?: (publishableKey: string) => StripeInstance | null;
  }
}

let stripeScriptPromise: Promise<void> | null = null;

const loadStripeScript = () => {
  if (typeof window === "undefined") {
    return Promise.reject(new Error("Stripe is only available in the browser."));
  }

  if (window.Stripe) {
    return Promise.resolve();
  }

  if (!stripeScriptPromise) {
    stripeScriptPromise = new Promise<void>((resolve, reject) => {
      const existingScript = document.querySelector<HTMLScriptElement>('script[src="https://js.stripe.com/v3/"]');
      if (existingScript) {
        const loadState = existingScript.getAttribute("data-stripe-load-state");
        
        // If script already loaded successfully, verify window.Stripe exists
        if (loadState === "loaded") {
          if (window.Stripe) {
            resolve();
            return;
          }
          // Stripe object missing despite "loaded" state - remove and recreate
          existingScript.remove();
        }
        // If script is currently loading, attach listeners
        else if (loadState === "loading") {
          existingScript.addEventListener("load", () => {
            existingScript.setAttribute("data-stripe-load-state", "loaded");
            resolve();
          }, { once: true });
          existingScript.addEventListener("error", () => {
            existingScript.setAttribute("data-stripe-load-state", "error");
            stripeScriptPromise = null;
            reject(new Error("Failed to load Stripe.js."));
          }, { once: true });
          return;
        }
        // If script previously errored or has unknown/missing state, remove and recreate
        else {
          existingScript.remove();
        }
      }

      const script = document.createElement("script");
      script.src = "https://js.stripe.com/v3/";
      script.async = true;
      script.setAttribute("data-stripe-load-state", "loading");
      script.onload = () => {
        script.setAttribute("data-stripe-load-state", "loaded");
        resolve();
      };
      script.onerror = () => {
        script.setAttribute("data-stripe-load-state", "error");
        stripeScriptPromise = null;
        reject(new Error("Failed to load Stripe.js."));
      };
      document.head.appendChild(script);
    }).catch((error) => {
      // Clear the cached promise on failure to allow retry attempts
      stripeScriptPromise = null;
      throw error;
    });
  }

  return stripeScriptPromise;
};

const buildStripePendingMessage = (status?: string) => {
  switch (status) {
    case "succeeded":
      return "Payment details were accepted. Backend authority is still finalizing confirmation.";
    case "processing":
      return "Stripe accepted the payment and is still processing the final outcome.";
    case "requires_action":
      return "Stripe requires another customer action to complete this payment.";
    default:
      return "Payment was submitted to Stripe. Backend authority will confirm the final order state.";
  }
};

export const EmbeddedStripePaymentForm = ({
  orderNumber,
  amount,
  currency,
  clientSecret,
  publishableKey,
  onPaymentPending,
  onPaymentError
}: EmbeddedStripePaymentFormProps) => {
  const hostRef = useRef<HTMLDivElement | null>(null);
  const stripeRef = useRef<StripeInstance | null>(null);
  const elementsRef = useRef<StripeElements | null>(null);
  const paymentElementRef = useRef<StripeElement | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let isDisposed = false;

    const mount = async () => {
      setIsLoading(true);
      setError(null);

      try {
        await loadStripeScript();
        if (isDisposed) {
          return;
        }

        if (!window.Stripe) {
          throw new Error("Stripe.js did not expose a Stripe client.");
        }

        const stripe = window.Stripe(publishableKey);
        if (!stripe) {
          throw new Error("Stripe client could not be initialized.");
        }

        const elements = stripe.elements({
          clientSecret,
          appearance: {
            theme: "stripe",
            variables: {
              colorPrimary: "#0f766e",
              colorBackground: "#ffffff",
              colorText: "#0f172a",
              colorDanger: "#be123c",
              borderRadius: "16px"
            }
          }
        });

        const paymentElement = elements.create("payment", { layout: "tabs" });
        if (!hostRef.current) {
          throw new Error("Stripe payment container is unavailable.");
        }

        paymentElement.mount(hostRef.current);
        stripeRef.current = stripe;
        elementsRef.current = elements;
        paymentElementRef.current = paymentElement;
        setIsLoading(false);
      } catch (mountError) {
        const message =
          mountError instanceof Error
            ? mountError.message
            : "Stripe payment form could not be loaded.";
        setError(message);
        setIsLoading(false);
        onPaymentError(message);
      }
    };

    void mount();

    return () => {
      isDisposed = true;
      paymentElementRef.current?.destroy();
      paymentElementRef.current = null;
      elementsRef.current = null;
      stripeRef.current = null;
    };
  }, [clientSecret, onPaymentError, publishableKey]);

  const handleConfirmPayment = async () => {
    if (!stripeRef.current || !elementsRef.current || isSubmitting) {
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      const result = await stripeRef.current.confirmPayment({
        elements: elementsRef.current,
        confirmParams: {
          return_url: window.location.href
        },
        redirect: "if_required"
      });

      if (result.error?.message) {
        setError(result.error.message);
        onPaymentError(result.error.message);
        return;
      }

      onPaymentPending(buildStripePendingMessage(result.paymentIntent?.status));
    } catch (submitError) {
      const message =
        submitError instanceof Error
          ? submitError.message
          : "Stripe payment confirmation failed.";
      setError(message);
      onPaymentError(message);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="rounded-[1.75rem] border border-sky-200 bg-white/90 p-5 shadow-card">
      <div className="space-y-2">
        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-sky-700">Stripe Payment</p>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-lg font-black tracking-tight text-foreground">{orderNumber}</p>
            <p className="text-sm text-muted-foreground">Enter your payment details to continue the same authoritative order.</p>
          </div>
          <div className="rounded-2xl border border-sky-100 bg-sky-50 px-4 py-3 text-right">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-sky-700">Amount</p>
            <p className="text-lg font-black tracking-tight text-sky-950">{formatCurrency(amount, currency)}</p>
          </div>
        </div>
      </div>

      <div className="mt-5 rounded-[1.5rem] border border-border/70 bg-slate-50/80 p-4">
        <div className="mb-4 flex items-center gap-2 text-sm text-muted-foreground">
          <ShieldCheck className="h-4 w-4 text-emerald-600" />
          Card details are collected by Stripe. POS remains the order and payment-state authority.
        </div>

        {isLoading ? (
          <div className="flex items-center gap-2 rounded-[1.25rem] border border-border/70 bg-white px-4 py-5 text-sm text-muted-foreground">
            <LoaderCircle className="h-4 w-4 animate-spin text-primary" />
            Loading Stripe payment form...
          </div>
        ) : (
          <div ref={hostRef} className="min-h-28 rounded-[1.25rem] border border-border/70 bg-white p-4" />
        )}

        {error ? (
          <div className="mt-4 rounded-[1.25rem] border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-950">
            <div className="flex items-start gap-2">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <span>{error}</span>
            </div>
          </div>
        ) : null}

        <div className="mt-4 flex flex-wrap gap-3">
          <Button type="button" onClick={() => void handleConfirmPayment()} disabled={isLoading || isSubmitting}>
            {isSubmitting ? (
              <>
                <LoaderCircle className="h-4 w-4 animate-spin" />
                Submitting payment...
              </>
            ) : (
              <>
                <CreditCard className="h-4 w-4" />
                Submit payment
              </>
            )}
          </Button>
        </div>
      </div>
    </div>
  );
};

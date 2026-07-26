import { memo } from "react";
import { Banknote, CreditCard } from "lucide-react";

import type { CheckoutPaymentMethod, CommerceHubCartPaymentMethodAvailability } from "@/types";
import { cn } from "@/utils";
import { Card, CardContent } from "@/ui/card";

type PaymentMethodSelectorProps = {
  value: CheckoutPaymentMethod;
  disabled: boolean;
  paymentMethods: CommerceHubCartPaymentMethodAvailability[];
  onChange: (value: CheckoutPaymentMethod) => void;
};

const optionMetadata: Record<CheckoutPaymentMethod, {
  title: string;
  description: string;
  icon: typeof Banknote;
}> = {
  COD: {
    title: "Cash on Delivery",
    description: "Order is created immediately and each store can start fulfillment.",
    icon: Banknote
  },
  PAYMOB: {
    title: "Pay with Card / Wallet",
    description: "Creates the order first, then prepares the Paymob payment step for platform collection.",
    icon: CreditCard
  },
  STRIPE: {
    title: "Pay with Stripe",
    description: "Creates the order first, then opens a Stripe-secured payment handoff when every store in this order supports it.",
    icon: CreditCard
  }
};

const PaymentMethodSelectorComponent = ({ value, disabled, paymentMethods, onChange }: PaymentMethodSelectorProps) => {
  // Filter payment methods to only those with defined metadata
  const supportedPaymentMethods = paymentMethods.filter(pm => optionMetadata[pm.code]);
  
  return (
    <Card className="border-white/70 bg-white/90 shadow-card">
      <CardContent className="space-y-5 p-6 md:p-7">
        <div className="space-y-2">
          <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Payment Method</p>
          <h2 className="text-2xl font-black tracking-tight text-foreground">How should the customer pay?</h2>
        </div>

        {paymentMethods.length === 0 ? (
          <div className="rounded-[1.5rem] border border-border/70 bg-slate-50/60 px-4 py-3 text-sm text-muted-foreground">
            We are verifying which payment methods are enabled across every store in this order.
          </div>
        ) : supportedPaymentMethods.length === 0 ? (
          <div className="rounded-[1.5rem] border border-border/70 bg-slate-50/60 px-4 py-3 text-sm text-muted-foreground">
            No supported payment methods are available for this order.
          </div>
        ) : (
          <div className="grid gap-3">
            {supportedPaymentMethods.map((paymentMethod) => {
              const metadata = optionMetadata[paymentMethod.code];
              const Icon = metadata.icon;
              const isActive = paymentMethod.code === value;
              const isOptionDisabled = disabled || !paymentMethod.isAvailable;

              return (
                <label
                  key={paymentMethod.code}
                  className={cn(
                    "flex items-start gap-4 rounded-[1.75rem] border p-4 transition-all",
                    paymentMethod.isAvailable && !disabled ? "cursor-pointer" : "cursor-not-allowed opacity-70",
                    isActive ? "border-primary bg-primary/5 shadow-soft" : "border-border/70 bg-slate-50/60",
                    paymentMethod.isAvailable && !isActive && !disabled ? "hover:border-primary/40" : ""
                  )}
                >
                  <input
                    type="radio"
                    name="checkout-payment-method"
                    value={paymentMethod.code}
                    checked={isActive}
                    disabled={isOptionDisabled}
                    onChange={() => {
                      if (paymentMethod.isAvailable) {
                        onChange(paymentMethod.code);
                      }
                    }}
                    className="mt-1 h-4 w-4 border-border text-primary focus:ring-primary"
                  />

                  <div className="flex min-w-0 flex-1 items-start gap-3">
                    <div className={cn("rounded-full p-3", isActive ? "bg-primary text-primary-foreground" : "bg-white text-primary")}>
                      <Icon className="h-4 w-4" />
                    </div>

                    <div className="space-y-1">
                      <p className="font-semibold text-foreground">{metadata.title}</p>
                      <p className="text-sm leading-6 text-muted-foreground">{metadata.description}</p>
                      {!paymentMethod.isAvailable ? (
                        <p className="text-sm font-medium text-amber-800">
                          {paymentMethod.unavailableReason ?? "This payment method is not enabled for every store in this order."}
                        </p>
                      ) : null}
                    </div>
                  </div>
                </label>
              );
            })}
          </div>
        )}
      </CardContent>
    </Card>
  );
};

export const PaymentMethodSelector = memo(PaymentMethodSelectorComponent);

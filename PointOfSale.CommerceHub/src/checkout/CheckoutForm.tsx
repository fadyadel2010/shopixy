import { memo } from "react";

import type { CheckoutCustomerFormData, CheckoutFieldErrors } from "@/types";
import { cn } from "@/utils";
import { Card, CardContent } from "@/ui/card";
import { Input } from "@/ui/input";

type CheckoutFormProps = {
  customer: CheckoutCustomerFormData;
  errors: CheckoutFieldErrors;
  disabled: boolean;
  onChange: (field: keyof CheckoutCustomerFormData, value: string) => void;
};

const CheckoutFormComponent = ({ customer, errors, disabled, onChange }: CheckoutFormProps) => {
  const renderField = (
    field: keyof CheckoutCustomerFormData,
    label: string,
    placeholder: string,
    options: {
      type?: string;
      optional?: boolean;
      multiline?: boolean;
    } = {}
  ) => {
    const error = errors[field];
    const value = customer[field];

    return (
      <label className="space-y-2">
        <span className="text-sm font-semibold text-foreground">
          {label}
          {options.optional ? <span className="font-normal text-muted-foreground"> (optional)</span> : null}
        </span>

        {options.multiline ? (
          <textarea
            value={value}
            onChange={(event) => onChange(field, event.target.value)}
            placeholder={placeholder}
            disabled={disabled}
            rows={4}
            className={cn(
              "flex min-h-28 w-full rounded-2xl border border-input bg-card px-4 py-3 text-sm transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50",
              error ? "border-rose-300 focus-visible:ring-rose-300" : ""
            )}
          />
        ) : (
          <Input
            type={options.type ?? "text"}
            value={value}
            onChange={(event) => onChange(field, event.target.value)}
            placeholder={placeholder}
            disabled={disabled}
            className={error ? "border-rose-300 focus-visible:ring-rose-300" : ""}
          />
        )}

        {error ? <p className="text-sm text-rose-600">{error}</p> : null}
      </label>
    );
  };

  return (
    <Card className="border-white/70 bg-white/90 shadow-card">
      <CardContent className="space-y-6 p-6 md:p-7">
        <div className="space-y-2">
          <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Customer Information</p>
          <h2 className="text-2xl font-black tracking-tight text-foreground">Delivery details</h2>
          <p className="text-sm leading-6 text-muted-foreground">
            We use this information to create one marketplace order and separate sub-orders for each store in your cart.
          </p>
        </div>

        <div className="grid gap-4 md:grid-cols-2">
          {renderField("fullName", "Full name", "Enter the customer name")}
          {renderField("phoneNumber", "Phone number", "e.g. +20 10 1234 5678", { type: "tel" })}
        </div>

        <div className="grid gap-4">
          {renderField("addressLine", "Address", "Street, building, floor, apartment")}
        </div>

        <div className="grid gap-4 md:grid-cols-2">
          {renderField("city", "City", "City")}
          {renderField("area", "Area", "Area / district", { optional: true })}
        </div>

        <div className="grid gap-4">
          {renderField("notes", "Notes", "Delivery notes, landmarks, or special instructions", { optional: true, multiline: true })}
        </div>
      </CardContent>
    </Card>
  );
};

export const CheckoutForm = memo(CheckoutFormComponent);

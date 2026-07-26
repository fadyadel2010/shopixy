import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CreditCard, Loader2, PencilLine, PlusCircle, ShieldCheck } from "lucide-react";

import { customerPayoutDetailsQueryOptions, customerQueryKeys } from "@/features/customer/queries";
import {
  formatRefundPayoutInput,
  formatRefundPayoutMethodLabel,
  getRefundPayoutMethodMeta,
  REFUND_PAYOUT_METHOD_OPTIONS,
  validateRefundPayoutInput
} from "@/features/customer/payout-details";
import { customerPayoutDetailsService } from "@/services";
import { useToastStore } from "@/store";
import type { CustomerPayoutDetails, RefundPayoutMethod } from "@/types";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";

type PayoutFormState = {
  method: RefundPayoutMethod;
  accountIdentifier: string;
  accountHolderName: string;
};

const initialFormState: PayoutFormState = {
  method: "VodafoneCash",
  accountIdentifier: "",
  accountHolderName: ""
};

export const CustomerPayoutMethodsCard = () => {
  const queryClient = useQueryClient();
  const showToast = useToastStore((state) => state.showToast);
  const payoutDetailsQuery = useQuery(customerPayoutDetailsQueryOptions());
  const [editingPayoutDetailsId, setEditingPayoutDetailsId] = useState<string | null>(null);
  const [formState, setFormState] = useState<PayoutFormState>(initialFormState);

  const editingPayoutDetails = useMemo(
    () => payoutDetailsQuery.data?.find((item) => item.id === editingPayoutDetailsId) ?? null,
    [editingPayoutDetailsId, payoutDetailsQuery.data]
  );
  const payoutMethodMeta = getRefundPayoutMethodMeta(formState.method);
  const canReuseExistingAccount =
    Boolean(editingPayoutDetails) &&
    !formState.accountIdentifier.trim() &&
    editingPayoutDetails?.method === formState.method;
  const validationError = canReuseExistingAccount
    ? null
    : validateRefundPayoutInput(formState.method, formState.accountIdentifier);
  const shouldShowValidationError =
    Boolean(formState.accountIdentifier.trim()) ||
    (Boolean(editingPayoutDetails) && editingPayoutDetails?.method !== formState.method);

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (validationError && !canReuseExistingAccount) {
        throw new Error(validationError);
      }

      const payload = {
        method: formState.method,
        accountIdentifier: formState.accountIdentifier.trim()
          ? formatRefundPayoutInput(formState.method, formState.accountIdentifier)
          : undefined,
        accountHolderName: formState.accountHolderName.trim() || undefined
      };

      if (editingPayoutDetailsId) {
        return customerPayoutDetailsService.updatePayoutDetails(editingPayoutDetailsId, payload);
      }

      return customerPayoutDetailsService.createPayoutDetails({
        ...payload,
        accountIdentifier: payload.accountIdentifier ?? ""
      });
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: customerQueryKeys.payoutDetails() });
      setEditingPayoutDetailsId(null);
      setFormState({ ...initialFormState });

      showToast({
        tone: "success",
        title: editingPayoutDetailsId ? "Payout method updated" : "Payout method saved",
        description: editingPayoutDetailsId
          ? "Your saved payout method was updated."
          : "Your payout method is ready to reuse in future refunds."
      });
    }
  });

  const startEditing = (payoutDetails: CustomerPayoutDetails) => {
    setEditingPayoutDetailsId(payoutDetails.id);
    setFormState({
      method: payoutDetails.method,
      accountIdentifier: "",
      accountHolderName: payoutDetails.accountHolderName ?? ""
    });
  };

  const resetForm = () => {
    setEditingPayoutDetailsId(null);
    setFormState({ ...initialFormState });
  };

  return (
    <Card className="border-white/70 bg-white/92">
      <CardContent className="space-y-6 p-6">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="space-y-2">
            <div className="inline-flex items-center gap-2 rounded-full border border-border/70 bg-slate-50 px-3 py-2 text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
              <CreditCard className="h-4 w-4" />
              Payout Details
            </div>
            <div>
              <h2 className="text-2xl font-black tracking-tight text-foreground">Saved refund destinations</h2>
              <p className="text-sm leading-6 text-muted-foreground">
                Store masked payout methods here so approved refunds can be prepared quickly without re-entering the same details.
              </p>
            </div>
          </div>

          <Button type="button" variant="outline" onClick={resetForm} disabled={saveMutation.isPending}>
            <PlusCircle className="mr-2 h-4 w-4" />
            {editingPayoutDetailsId ? "Add another" : "New payout method"}
          </Button>
        </div>

        {payoutDetailsQuery.isLoading ? (
          <div className="space-y-3">
            {[0, 1].map((item) => (
              <div key={item} className="h-20 animate-pulse rounded-[1.5rem] bg-slate-100" />
            ))}
          </div>
        ) : payoutDetailsQuery.isError ? (
          <div className="rounded-[1.5rem] border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
            Saved payout methods could not be loaded right now. You can still add a new one below.
          </div>
        ) : payoutDetailsQuery.data && payoutDetailsQuery.data.length > 0 ? (
          <div className="grid gap-3">
            {payoutDetailsQuery.data.map((payoutDetails) => (
              <div
                key={payoutDetails.id}
                className="flex flex-col gap-3 rounded-[1.5rem] border border-border/70 bg-slate-50/70 px-4 py-4 lg:flex-row lg:items-center lg:justify-between"
              >
                <div className="space-y-1">
                  <p className="text-sm font-semibold text-foreground">
                    {formatRefundPayoutMethodLabel(payoutDetails.method)} · {payoutDetails.maskedAccountIdentifier}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {payoutDetails.accountHolderName || "No account holder name saved"}
                  </p>
                </div>

                <div className="flex flex-wrap gap-2">
                  <span className="inline-flex items-center gap-2 rounded-full border border-border/70 bg-white px-3 py-2 text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                    <ShieldCheck className="h-3.5 w-3.5" />
                    {payoutDetails.isVerified ? "Verified" : "Verification pending"}
                  </span>
                  <Button type="button" variant="outline" size="sm" onClick={() => startEditing(payoutDetails)}>
                    <PencilLine className="mr-2 h-4 w-4" />
                    Edit
                  </Button>
                </div>
              </div>
            ))}
          </div>
        ) : (
          <div className="rounded-[1.5rem] border border-dashed border-border bg-slate-50/70 px-4 py-6 text-sm text-muted-foreground">
            No saved payout methods yet. Add one now or enter one-time details directly inside a refund request.
          </div>
        )}

        <div className="grid gap-4 rounded-[1.75rem] border border-border/70 bg-slate-50/80 px-4 py-4 lg:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)]">
          <div className="space-y-2">
            <p className="text-sm font-semibold text-foreground">
              {editingPayoutDetailsId ? "Edit payout method" : "Save payout method"}
            </p>
            <p className="text-xs leading-5 text-muted-foreground">
              {editingPayoutDetailsId
                ? "Leave the account field empty to keep the current saved account, or enter a replacement for this method."
                : "This method will be masked in customer views and copied into each refund snapshot when you use it."}
            </p>
          </div>

          <div className="space-y-4">
            <label className="space-y-2">
              <span className="text-sm font-semibold text-foreground">Method</span>
              <select
                value={formState.method}
                onChange={(event) =>
                  setFormState((current) => ({
                    ...current,
                    method: event.target.value as RefundPayoutMethod,
                    accountIdentifier: formatRefundPayoutInput(event.target.value as RefundPayoutMethod, current.accountIdentifier)
                  }))
                }
                className="flex h-12 w-full rounded-2xl border border-input bg-white px-4 text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                disabled={saveMutation.isPending}
              >
                {REFUND_PAYOUT_METHOD_OPTIONS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>

            <label className="space-y-2">
              <span className="text-sm font-semibold text-foreground">{payoutMethodMeta.accountLabel}</span>
              <input
                value={formState.accountIdentifier}
                onChange={(event) =>
                  setFormState((current) => ({
                    ...current,
                    accountIdentifier: formatRefundPayoutInput(current.method, event.target.value)
                  }))
                }
                placeholder={editingPayoutDetails ? `${editingPayoutDetails.maskedAccountIdentifier} (leave blank to keep)` : payoutMethodMeta.accountPlaceholder}
                className="flex h-12 w-full rounded-2xl border border-input bg-white px-4 text-sm transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                disabled={saveMutation.isPending}
              />
              <p className="text-xs text-muted-foreground">{payoutMethodMeta.helperText}</p>
            </label>

            <label className="space-y-2">
              <span className="text-sm font-semibold text-foreground">Account holder name</span>
              <input
                value={formState.accountHolderName}
                onChange={(event) =>
                  setFormState((current) => ({
                    ...current,
                    accountHolderName: event.target.value
                  }))
                }
                maxLength={200}
                placeholder="Optional"
                className="flex h-12 w-full rounded-2xl border border-input bg-white px-4 text-sm transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                disabled={saveMutation.isPending}
              />
            </label>

            {validationError && shouldShowValidationError ? (
              <p className="text-xs text-rose-600">{validationError}</p>
            ) : null}

            {saveMutation.isError ? (
              <div className="rounded-[1.25rem] border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
                {saveMutation.error instanceof Error ? saveMutation.error.message : "Unable to save payout details right now."}
              </div>
            ) : null}

            <div className="flex flex-col gap-3 sm:flex-row sm:justify-end">
              {editingPayoutDetailsId ? (
                <Button type="button" variant="outline" onClick={resetForm} disabled={saveMutation.isPending}>
                  Cancel
                </Button>
              ) : null}
              <Button
                type="button"
                onClick={() => saveMutation.mutate()}
                disabled={
                  saveMutation.isPending ||
                  Boolean(validationError && !canReuseExistingAccount)
                }
              >
                {saveMutation.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
                {editingPayoutDetailsId ? "Update payout method" : "Save payout method"}
              </Button>
            </div>
          </div>
        </div>
      </CardContent>
    </Card>
  );
};

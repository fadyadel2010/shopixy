import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { ReceiptText } from "lucide-react";

import { CustomerPayoutMethodsCard } from "@/features/customer/components/CustomerPayoutMethodsCard";
import {
  formatRefundPayoutMethodLabel,
  isDevelopmentRefundSimulation
} from "@/features/customer/payout-details";
import { RefundEvidenceDialog, formatRefundReasonLabel } from "@/features/customer/refund-evidence";
import { customerRefundsQueryOptions } from "@/features/customer/queries";
import { RefundStatusBadge, formatCustomerDate } from "@/features/customer/status";
import { EmptyState } from "@/shared/EmptyState";
import { InlineErrorState } from "@/shared/InlineErrorState";
import { SectionHeading } from "@/shared/SectionHeading";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { formatCurrency, useDocumentTitle } from "@/utils";

const getRefundStatusMessage = (
  status: string,
  isDevelopmentSimulation: boolean,
  externalMoneyMovementConfirmed: boolean
) => {
  if (status === "AwaitingPayout" || status === "ReadyForExecution") {
    return {
      tone: "border-amber-200 bg-amber-50 text-amber-950",
      message: "Your refund has been approved and is being processed. The payment will be sent shortly."
    };
  }

  if (status === "Paid" || status === "Executed" || status === "Completed") {
    return {
      tone: isDevelopmentSimulation
        ? "border-blue-200 bg-blue-50 text-blue-950"
        : externalMoneyMovementConfirmed
          ? "border-emerald-200 bg-emerald-50 text-emerald-950"
          : "border-amber-200 bg-amber-50 text-amber-950",
      message: isDevelopmentSimulation
        ? "Development simulation recorded successfully. No external money movement occurred."
        : externalMoneyMovementConfirmed
          ? "Your refund has been sent successfully."
          : "Your refund was recorded, but external money movement has not been confirmed."
    };
  }

  return null;
};

export const CustomerRefundsPage = () => {
  const navigate = useNavigate();
  const refundsQuery = useQuery(customerRefundsQueryOptions());
  const [activeRefundId, setActiveRefundId] = useState<string | null>(null);

  useDocumentTitle("Refund Requests | CommerceHub");

  const acceptedRefunds = refundsQuery.isFetching || refundsQuery.isError ? [] : refundsQuery.data ?? [];
  const activeRefund = acceptedRefunds.find((refund) => refund.refundId === activeRefundId) ?? null;

  return (
    <div className="space-y-8">
      <SectionHeading
        eyebrow="Refunds"
        title="Your refund requests"
        description="Track every item-level refund request in one place while vendor reviews and future approval steps stay isolated behind the scenes."
      />

      <CustomerPayoutMethodsCard />

      {refundsQuery.isLoading ? (
        <div className="space-y-4">
          {[0, 1, 2].map((item) => (
            <Card key={item} className="border-white/70 bg-white/90">
              <CardContent className="space-y-4 p-6">
                <div className="h-3 w-24 rounded-full bg-slate-200" />
                <div className="h-7 w-56 rounded-full bg-slate-200" />
                <div className="h-4 w-full rounded-full bg-slate-200" />
                <div className="h-4 w-4/5 rounded-full bg-slate-200" />
              </CardContent>
            </Card>
          ))}
        </div>
      ) : refundsQuery.isError ? (
        <InlineErrorState message="We could not load your refund requests right now." onRetry={() => void refundsQuery.refetch()} />
      ) : acceptedRefunds.length === 0 ? (
        <EmptyState
          icon={<ReceiptText className="h-6 w-6" />}
          title="No refund requests yet"
          description="Once you submit an item-level refund request, it will appear here with its latest review status."
          actionLabel="View orders"
          onAction={() => navigate("/orders")}
        />
      ) : (
        <div className="space-y-4">
          {acceptedRefunds.map((refund) => {
            const isDevelopmentSimulation = isDevelopmentRefundSimulation(refund.payout);
            const refundStatusMessage = getRefundStatusMessage(
              refund.status,
              isDevelopmentSimulation,
              refund.payout?.externalMoneyMovementConfirmed === true
            );

            return (
              <Card key={refund.refundId} className="border-white/70 bg-white/92">
                <CardContent className="space-y-5 p-6">
                  <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                    <div className="space-y-2">
                      <div className="flex flex-wrap items-center gap-2">
                        <RefundStatusBadge status={refund.status} />
                        <span className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                          {formatCustomerDate(refund.createdAt)}
                        </span>
                      </div>

                      <div>
                        <h2 className="text-2xl font-black tracking-tight text-foreground">{refund.productName}</h2>
                        <p className="text-sm text-muted-foreground">
                          Order {refund.orderNumber} - Qty {refund.quantity}
                        </p>
                      </div>
                    </div>

                    <div className="rounded-[1.25rem] border border-border/70 bg-slate-50/80 px-4 py-3 text-right">
                      <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Amount</p>
                      <p className="text-2xl font-black tracking-tight text-foreground">{formatCurrency(refund.amount, refund.currency)}</p>
                    </div>
                  </div>

                  {refundStatusMessage ? (
                    <div className={`rounded-[1.25rem] border px-4 py-3 text-sm ${refundStatusMessage.tone}`}>
                      {refundStatusMessage.message}
                    </div>
                  ) : null}

                  <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_auto] md:items-end">
                    <div className="rounded-[1.5rem] border border-border/70 bg-slate-50/80 px-4 py-4">
                      <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Reason</p>
                      <p className="mt-2 text-sm font-semibold text-foreground">{formatRefundReasonLabel(refund.reasonCode)}</p>
                      {refund.reasonText ? <p className="mt-2 text-sm leading-6 text-muted-foreground">{refund.reasonText}</p> : null}
                      {refund.payout ? (
                        <div className="mt-4 rounded-[1.25rem] border border-white/70 bg-white px-4 py-3">
                          <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                            {isDevelopmentSimulation ? "Development refund simulation" : "Refund method"}
                          </p>
                          {isDevelopmentSimulation ? (
                            <p className="mt-2 text-sm font-semibold text-blue-900">
                              Internal refund recognition only. No external money movement occurred.
                            </p>
                          ) : null}
                          <p className="mt-2 text-sm font-semibold text-foreground">
                            {formatRefundPayoutMethodLabel(refund.payout.method)}
                          </p>
                          <p className="mt-1 text-sm text-muted-foreground">{refund.payout.maskedAccountIdentifier}</p>
                          {refund.payout.accountHolderName ? (
                            <p className="mt-1 text-xs text-muted-foreground">{refund.payout.accountHolderName}</p>
                          ) : null}
                          {refund.payout.transactionReference ? (
                            <p className="mt-2 text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                              {isDevelopmentSimulation ? "Simulation reference" : "Transaction Ref"}
                            </p>
                          ) : null}
                          {refund.payout.transactionReference ? (
                            <p className="mt-1 break-all text-sm font-medium text-foreground">{refund.payout.transactionReference}</p>
                          ) : null}
                          {refund.payout.paidAt ? (
                            <p className="mt-2 text-xs text-muted-foreground">
                              {isDevelopmentSimulation
                                ? "Simulation recorded at"
                                : refund.payout.externalMoneyMovementConfirmed
                                  ? "Paid at"
                                  : "Recorded at"}{" "}
                              {formatCustomerDate(refund.payout.paidAt)}
                            </p>
                          ) : null}
                        </div>
                      ) : null}
                      {refund.media[0] ? (
                        <div className="mt-4 overflow-hidden rounded-[1.25rem] border border-white/70 bg-white">
                          {refund.media[0].mediaType === "video" ? (
                            <video src={refund.media[0].url} className="h-32 w-full object-cover" controls preload="metadata" />
                          ) : (
                            <img src={refund.media[0].url} alt={refund.media[0].fileName} className="h-32 w-full object-cover" />
                          )}
                        </div>
                      ) : null}
                    </div>

                    <div className="flex flex-wrap gap-2">
                      {refund.media.length > 0 ? (
                        <Button type="button" variant="outline" size="sm" onClick={() => setActiveRefundId(refund.refundId)}>
                          View evidence
                        </Button>
                      ) : null}
                      <Button asChild variant="outline" size="sm">
                        <Link to={`/orders/${refund.orderId}`}>Open order</Link>
                      </Button>
                    </div>
                  </div>
                </CardContent>
              </Card>
            );
          })}
        </div>
      )}

      <RefundEvidenceDialog
        open={activeRefund != null}
        title={activeRefund?.productName ?? "Refund evidence"}
        media={activeRefund?.media ?? []}
        reasonCode={activeRefund?.reasonCode}
        reasonText={activeRefund?.reasonText}
        onClose={() => setActiveRefundId(null)}
      />
    </div>
  );
};

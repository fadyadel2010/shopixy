import { ChevronDown, ChevronRight, ShoppingBag } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link, useNavigate, useSearchParams } from "react-router-dom";

import { customerOrdersQueryOptions } from "@/features/customer/queries";
import { ItemStatusBadge, OrderStatusBadge, PaymentStatusBadge, formatCustomerDate } from "@/features/customer/status";
import { EmptyState } from "@/shared/EmptyState";
import { InlineErrorState } from "@/shared/InlineErrorState";
import { SectionHeading } from "@/shared/SectionHeading";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { formatCurrency, useDocumentTitle } from "@/utils";

const PAGE_SIZE = 10;

export const CustomerOrdersPage = () => {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [expandedOrderIds, setExpandedOrderIds] = useState<string[]>([]);

  const page = useMemo(() => {
    const rawPage = Number.parseInt(searchParams.get("page") ?? "1", 10);
    return Number.isFinite(rawPage) && rawPage > 0 ? rawPage : 1;
  }, [searchParams]);

  const ordersQuery = useQuery(customerOrdersQueryOptions({ page, pageSize: PAGE_SIZE }));
  const acceptedOrders = useMemo(
    () => (ordersQuery.isFetching || ordersQuery.isError ? [] : ordersQuery.data?.items ?? []),
    [ordersQuery.data?.items, ordersQuery.isError, ordersQuery.isFetching]
  );

  useDocumentTitle("My Orders | CommerceHub");

  useEffect(() => {
    const visibleOrders = acceptedOrders;
    if (visibleOrders.length === 0) {
      setExpandedOrderIds([]);
      return;
    }

    const nextExpanded = expandedOrderIds.filter((orderId) => visibleOrders.some((order) => order.orderId === orderId));
    if (nextExpanded.length === expandedOrderIds.length && nextExpanded.length > 0) {
      return;
    }

    setExpandedOrderIds(nextExpanded.length > 0 ? nextExpanded : [visibleOrders[0].orderId]);
  }, [acceptedOrders, expandedOrderIds]);

  const updatePage = (nextPage: number) => {
    setSearchParams((current) => {
      const next = new URLSearchParams(current);
      next.set("page", String(nextPage));
      return next;
    });
  };

  const toggleExpanded = (orderId: string) => {
    setExpandedOrderIds((current) =>
      current.includes(orderId) ? current.filter((value) => value !== orderId) : [...current, orderId]
    );
  };

  return (
    <div className="space-y-8">
      <SectionHeading
        eyebrow="Orders"
        title="Your marketplace orders"
        description="Each Shopixy order shows backend-issued vendor sub-orders so you can follow multi-store fulfillment without losing the full customer view."
      />

      {ordersQuery.isLoading || ordersQuery.isFetching ? (
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
      ) : ordersQuery.isError ? (
        <InlineErrorState message="We could not load your customer orders right now." onRetry={() => void ordersQuery.refetch()} />
      ) : acceptedOrders.length === 0 ? (
        <EmptyState
          icon={<ShoppingBag className="h-6 w-6" />}
          title="No orders yet"
          description="When you place a Shopixy order, you will see the full vendor breakdown here."
          actionLabel="Start shopping"
          onAction={() => navigate("/products")}
        />
      ) : (
        <>
          <div className="space-y-4">
            {acceptedOrders.map((order) => {
              const isExpanded = expandedOrderIds.includes(order.orderId);

              return (
                <Card key={order.orderId} className="border-white/70 bg-white/92">
                  <CardContent className="space-y-5 p-6">
                    <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                      <div className="space-y-3">
                        <div className="flex flex-wrap items-center gap-2">
                          <OrderStatusBadge status={order.status} />
                          <span className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">{formatCustomerDate(order.createdAt)}</span>
                        </div>

                        <div>
                          <h2 className="text-2xl font-black tracking-tight text-foreground">{order.orderNumber}</h2>
                          <p className="text-sm leading-6 text-muted-foreground">
                            {order.subOrders.length} vendor sub-order{order.subOrders.length === 1 ? "" : "s"} across this marketplace purchase.
                          </p>
                        </div>
                      </div>

                      <div className="flex flex-wrap items-center gap-3 lg:justify-end">
                        <div className="rounded-[1.25rem] border border-border/70 bg-slate-50/80 px-4 py-3 text-right">
                          <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Total</p>
                          <p className="text-2xl font-black tracking-tight text-foreground">{formatCurrency(order.totalAmount, order.currency)}</p>
                        </div>

                        <Button type="button" variant="outline" size="sm" className="gap-2" onClick={() => toggleExpanded(order.orderId)}>
                          {isExpanded ? "Collapse" : "Expand"}
                          {isExpanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                        </Button>

                        <Button asChild size="sm" className="gap-2">
                          <Link to={`/orders/${order.orderId}`}>
                            Full details
                            <ChevronRight className="h-4 w-4" />
                          </Link>
                        </Button>
                      </div>
                    </div>

                    <div className="grid gap-3 md:grid-cols-3">
                      {order.subOrders.map((subOrder) => (
                        <div key={subOrder.subOrderId} className="rounded-[1.5rem] border border-border/70 bg-slate-50/80 px-4 py-4">
                          <div className="space-y-2">
                            <div className="flex flex-wrap items-center justify-between gap-2">
                              <p className="text-sm font-semibold text-foreground">{subOrder.vendorName}</p>
                              <PaymentStatusBadge status={subOrder.paymentStatus} />
                            </div>
                            <p className="text-sm text-muted-foreground">{subOrder.orderNumber}</p>
                            <p className="text-lg font-black tracking-tight text-foreground">{formatCurrency(subOrder.totalAmount, order.currency)}</p>
                          </div>
                        </div>
                      ))}
                    </div>

                    {isExpanded ? (
                      <div className="space-y-4 border-t border-border/70 pt-5">
                        {order.subOrders.map((subOrder) => (
                          <div key={subOrder.subOrderId} className="rounded-[1.75rem] border border-border/70 bg-white/90 p-5">
                            <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
                              <div className="space-y-1">
                                <h3 className="text-xl font-black tracking-tight text-foreground">{subOrder.vendorName}</h3>
                                <p className="text-sm text-muted-foreground">{subOrder.orderNumber}</p>
                              </div>

                              <div className="flex flex-wrap items-center gap-2">
                                <OrderStatusBadge status={subOrder.status} />
                                <PaymentStatusBadge status={subOrder.paymentStatus} />
                              </div>
                            </div>

                            <div className="mt-4 space-y-3">
                              {subOrder.items.map((item) => (
                                <div key={item.orderItemId} className="rounded-[1.25rem] border border-border/70 bg-slate-50/80 px-4 py-4">
                                  <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
                                    <div className="space-y-1">
                                      <p className="font-semibold text-foreground">{item.productName}</p>
                                      {item.variant ? <p className="text-sm text-muted-foreground">{item.variant}</p> : null}
                                      <p className="text-sm text-muted-foreground">Qty {item.quantity}</p>
                                    </div>

                                    <div className="flex flex-wrap items-center gap-2">
                                      <ItemStatusBadge status={item.status} />
                                      {item.canRefund ? (
                                        <span className="rounded-full bg-emerald-100 px-3 py-1 text-xs font-semibold text-emerald-700">Refund ready</span>
                                      ) : null}
                                      <span className="text-base font-bold text-foreground">{formatCurrency(item.total, order.currency)}</span>
                                    </div>
                                  </div>
                                </div>
                              ))}
                            </div>
                          </div>
                        ))}
                      </div>
                    ) : null}
                  </CardContent>
                </Card>
              );
            })}
          </div>

          <div className="flex flex-wrap items-center justify-between gap-3 rounded-[1.75rem] border border-white/70 bg-white/90 px-5 py-4">
            <p className="text-sm text-muted-foreground">
              Page {ordersQuery.data?.page} of {ordersQuery.data?.totalPages} with {ordersQuery.data?.totalCount} total order
              {ordersQuery.data?.totalCount === 1 ? "" : "s"}.
            </p>

            <div className="flex items-center gap-2">
              <Button type="button" variant="outline" size="sm" disabled={!ordersQuery.data?.hasPreviousPage} onClick={() => updatePage(page - 1)}>
                Previous
              </Button>
              <Button type="button" size="sm" disabled={!ordersQuery.data?.hasNextPage} onClick={() => updatePage(page + 1)}>
                Next
              </Button>
            </div>
          </div>
        </>
      )}
    </div>
  );
};

import { ArrowRight, ChevronRight, History, PackageCheck, ReceiptText, RotateCcw, UserRound } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { Link, useNavigate } from "react-router-dom";

import { customerOrdersQueryOptions } from "@/features/customer/queries";
import { ItemStatusBadge, OrderStatusBadge, formatCustomerDate } from "@/features/customer/status";
import { customerHasNavigationCapability } from "@/auth/customer-session-authority";
import { EmptyState } from "@/shared/EmptyState";
import { InlineErrorState } from "@/shared/InlineErrorState";
import { SectionHeading } from "@/shared/SectionHeading";
import { selectCustomerProfile, useCustomerAuthStore } from "@/store";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { formatCurrency, useDocumentTitle } from "@/utils";

export const CustomerDashboardPage = () => {
  const navigate = useNavigate();
  const customer = useCustomerAuthStore(selectCustomerProfile);
  const canManageAccount = customerHasNavigationCapability(customer, "customer.account.settings");
  const canViewOrders = customerHasNavigationCapability(customer, "customer.orders.view");
  const canViewRefunds = customerHasNavigationCapability(customer, "customer.refunds.view");
  const recentOrdersQuery = useQuery({
    ...customerOrdersQueryOptions({ page: 1, pageSize: 3 }),
    enabled: canViewOrders
  });

  useDocumentTitle("Customer Account | CommerceHub");

  const recentOrders = recentOrdersQuery.isFetching || recentOrdersQuery.isError ? [] : recentOrdersQuery.data?.items ?? [];
  const inFlightSubOrders = recentOrders.reduce(
    (count, order) =>
      count + order.subOrders.filter((subOrder) => !["Delivered", "Completed", "Cancelled", "Refunded"].includes(subOrder.status)).length,
    0
  );
  const refundReadyItems = recentOrders.reduce(
    (count, order) =>
      count +
      order.subOrders.reduce((subOrderCount, subOrder) => subOrderCount + subOrder.items.filter((item) => item.canRefund).length, 0),
    0
  );

  return (
    <div className="space-y-8">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <SectionHeading
          eyebrow="Customer Space"
          title={customer ? `Welcome back, ${customer.name.split(" ")[0]}` : "Your customer account"}
          description="Track every Shopixy marketplace order with backend-issued vendor sub-orders owned by you in one secure place."
        />

        <div className="flex flex-wrap items-center gap-3">
          {canManageAccount ? (
            <Button asChild variant="outline" size="lg" className="gap-2">
              <Link to="/customer/settings">
                Account settings
                <UserRound className="h-4 w-4" />
              </Link>
            </Button>
          ) : null}
          {canViewRefunds ? (
            <Button asChild variant="outline" size="lg" className="gap-2">
              <Link to="/customer/refunds">
                Refund requests
                <RotateCcw className="h-4 w-4" />
              </Link>
            </Button>
          ) : null}
          {canViewOrders ? (
            <Button asChild size="lg" className="gap-2">
              <Link to="/orders">
                View all orders
                <ArrowRight className="h-4 w-4" />
              </Link>
            </Button>
          ) : null}
        </div>
      </div>

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1.1fr)_repeat(3,minmax(0,0.65fr))]">
        <Card className="border-white/70 bg-white/95">
          <CardContent className="flex h-full flex-col justify-between gap-5 p-6">
            <div className="space-y-3">
              <div className="inline-flex w-fit items-center gap-2 rounded-full bg-primary/10 px-3 py-1 text-xs font-semibold text-primary">
                <UserRound className="h-4 w-4" />
                Account profile
              </div>
              <div>
                <h2 className="text-2xl font-black tracking-tight text-foreground">{customer?.name ?? "Customer"}</h2>
                <p className="text-sm text-muted-foreground">{customer?.email}</p>
                {customer?.phoneNumber ? <p className="text-sm text-muted-foreground">{customer.phoneNumber}</p> : null}
              </div>
            </div>

            <p className="text-sm leading-6 text-muted-foreground">
              Your account is isolated from vendor and admin systems, so only you can view these orders and your refund requests.
            </p>
          </CardContent>
        </Card>

        {canViewOrders ? <Card className="border-white/70 bg-white/90">
          <CardContent className="space-y-3 p-6">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Orders</p>
            <p className="text-4xl font-black tracking-tight text-foreground">
              {recentOrdersQuery.isFetching || recentOrdersQuery.isError ? "--" : recentOrdersQuery.data?.totalCount ?? 0}
            </p>
            <p className="text-sm text-muted-foreground">Marketplace orders linked to your customer ID.</p>
          </CardContent>
        </Card> : null}

        {canViewOrders ? <Card className="border-white/70 bg-white/90">
          <CardContent className="space-y-3 p-6">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">In Flight</p>
            <p className="text-4xl font-black tracking-tight text-foreground">{inFlightSubOrders}</p>
            <p className="text-sm text-muted-foreground">Vendor sub-orders still moving through fulfillment.</p>
          </CardContent>
        </Card> : null}

        {canViewOrders && canViewRefunds ? <Card className="border-white/70 bg-white/90">
          <CardContent className="space-y-3 p-6">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Refund Ready</p>
            <p className="text-4xl font-black tracking-tight text-foreground">{refundReadyItems}</p>
            <p className="text-sm text-muted-foreground">Delivered items that can still accept a refund request.</p>
          </CardContent>
        </Card> : null}
      </div>

      {canViewOrders ? <Card className="border-white/70 bg-white/92">
        <CardContent className="space-y-5 p-6 md:p-7">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="space-y-1">
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Recent Orders</p>
              <h2 className="text-2xl font-black tracking-tight text-foreground">Latest marketplace activity</h2>
            </div>

            <Button asChild variant="outline" size="sm" className="gap-2">
              <Link to="/orders">
                Order history
                <ChevronRight className="h-4 w-4" />
              </Link>
            </Button>
          </div>

          {recentOrdersQuery.isLoading || recentOrdersQuery.isFetching ? (
            <div className="grid gap-4 md:grid-cols-3">
              {[0, 1, 2].map((item) => (
                <div key={item} className="rounded-[1.75rem] border border-border/70 bg-slate-50/80 p-5">
                  <div className="space-y-3">
                    <div className="h-3 w-20 rounded-full bg-slate-200" />
                    <div className="h-7 w-40 rounded-full bg-slate-200" />
                    <div className="h-4 w-full rounded-full bg-slate-200" />
                    <div className="h-4 w-3/4 rounded-full bg-slate-200" />
                  </div>
                </div>
              ))}
            </div>
          ) : recentOrdersQuery.isError ? (
            <InlineErrorState
              message="We could not load your recent Shopixy orders right now."
              onRetry={() => void recentOrdersQuery.refetch()}
            />
          ) : recentOrders.length === 0 ? (
            <EmptyState
              icon={<History className="h-6 w-6" />}
              title="No customer orders yet"
              description="Once you place a Shopixy order, it will appear here with backend-issued vendor sub-orders and item status tracking."
              actionLabel="Browse products"
              onAction={() => navigate("/products")}
            />
          ) : (
            <div className="grid gap-4 md:grid-cols-3">
              {recentOrders.map((order) => (
                <Card key={order.orderId} className="border-border/70 bg-slate-50/70 shadow-none">
                  <CardContent className="space-y-4 p-5">
                    <div className="flex items-start justify-between gap-3">
                      <div className="space-y-1">
                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Order</p>
                        <h3 className="text-xl font-black tracking-tight text-foreground">{order.orderNumber}</h3>
                        <p className="text-sm text-muted-foreground">{formatCustomerDate(order.createdAt)}</p>
                      </div>
                      <OrderStatusBadge status={order.status} />
                    </div>

                    <div className="space-y-3 rounded-[1.5rem] border border-white/80 bg-white/90 p-4">
                      {order.subOrders.slice(0, 2).map((subOrder) => (
                        <div key={subOrder.subOrderId} className="space-y-2">
                          <div className="flex items-center justify-between gap-3">
                            <p className="text-sm font-semibold text-foreground">{subOrder.vendorName}</p>
                            <p className="text-sm font-semibold text-foreground">{formatCurrency(subOrder.totalAmount, order.currency)}</p>
                          </div>
                          {subOrder.items[0] ? (
                            <div className="flex items-center justify-between gap-3 text-xs text-muted-foreground">
                              <span className="truncate">{subOrder.items[0].productName}</span>
                              <ItemStatusBadge status={subOrder.items[0].status} />
                            </div>
                          ) : null}
                        </div>
                      ))}
                    </div>

                    <div className="flex items-center justify-between gap-3">
                      <div className="space-y-1">
                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Total</p>
                        <p className="text-2xl font-black tracking-tight text-foreground">{formatCurrency(order.totalAmount, order.currency)}</p>
                      </div>

                      <Button asChild variant="outline" size="sm" className="gap-2">
                        <Link to={`/orders/${order.orderId}`}>
                          Details
                          <ChevronRight className="h-4 w-4" />
                        </Link>
                      </Button>
                    </div>
                  </CardContent>
                </Card>
              ))}
            </div>
          )}
        </CardContent>
      </Card> : null}

      <div className="grid gap-4 md:grid-cols-3">
        <Card className="border-white/70 bg-white/90">
          <CardContent className="space-y-3 p-5">
            <ReceiptText className="h-5 w-5 text-primary" />
            <h3 className="text-xl font-black tracking-tight text-foreground">Order ownership</h3>
            <p className="text-sm leading-6 text-muted-foreground">Every Shopixy order is now bound to your customer ID and validated on every request.</p>
          </CardContent>
        </Card>

        <Card className="border-white/70 bg-white/90">
          <CardContent className="space-y-3 p-5">
            <PackageCheck className="h-5 w-5 text-primary" />
            <h3 className="text-xl font-black tracking-tight text-foreground">Item-level visibility</h3>
            <p className="text-sm leading-6 text-muted-foreground">Vendor items show Pending, Shipped, Delivered, or Cancelled so you can see where each line stands.</p>
          </CardContent>
        </Card>

        <Card className="border-white/70 bg-white/90">
          <CardContent className="space-y-3 p-5">
            <History className="h-5 w-5 text-primary" />
            <h3 className="text-xl font-black tracking-tight text-foreground">Refund preparation</h3>
            <p className="text-sm leading-6 text-muted-foreground">Delivered items are already flagged with `canRefund` so the next phase can build on a clean foundation.</p>
          </CardContent>
        </Card>
      </div>
    </div>
  );
};

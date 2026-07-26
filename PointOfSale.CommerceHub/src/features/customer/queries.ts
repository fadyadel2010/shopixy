import { queryOptions } from "@tanstack/react-query";

import { customerAuthService, customerOrdersService, customerPayoutDetailsService, customerRefundsService } from "@/services";
import type { CustomerOrdersQuery } from "@/types";

export const customerQueryKeys = {
  all: ["customer"] as const,
  profile: () => [...customerQueryKeys.all, "profile"] as const,
  orders: () => [...customerQueryKeys.all, "orders"] as const,
  ordersList: (query: CustomerOrdersQuery) => [...customerQueryKeys.orders(), "list", query] as const,
  orderDetail: (orderId: string) => [...customerQueryKeys.orders(), "detail", orderId] as const,
  payoutDetails: () => [...customerQueryKeys.all, "payout-details"] as const,
  refunds: () => [...customerQueryKeys.all, "refunds"] as const
};

export const customerProfileQueryOptions = () =>
  queryOptions({
    queryKey: customerQueryKeys.profile(),
    queryFn: ({ signal }) => customerAuthService.getProfile(signal)
  });

export const customerOrdersQueryOptions = (query: CustomerOrdersQuery) =>
  queryOptions({
    queryKey: customerQueryKeys.ordersList(query),
    queryFn: ({ signal }) => customerOrdersService.getOrders(query, signal)
  });

export const customerOrderDetailsQueryOptions = (orderId: string) =>
  queryOptions({
    queryKey: customerQueryKeys.orderDetail(orderId),
    queryFn: ({ signal }) => customerOrdersService.getOrder(orderId, signal),
    enabled: Boolean(orderId)
  });

export const customerPayoutDetailsQueryOptions = () =>
  queryOptions({
    queryKey: customerQueryKeys.payoutDetails(),
    queryFn: ({ signal }) => customerPayoutDetailsService.getPayoutDetails(signal)
  });

export const customerRefundsQueryOptions = () =>
  queryOptions({
    queryKey: customerQueryKeys.refunds(),
    queryFn: ({ signal }) => customerRefundsService.getRefunds(signal)
  });

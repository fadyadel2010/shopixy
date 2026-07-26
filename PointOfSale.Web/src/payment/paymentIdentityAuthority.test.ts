import { describe, expect, it } from "vitest";
import {
  buildOrderConfirmationPath,
  requireCanonicalPaymentStatusForOrder
} from "../../../PointOfSale.Storefront/src/features/checkout/paymentFlow";

describe("payment identity authority", () => {
  const backendStatus = {
    orderId: "backend-order-1",
    orderNumber: "ORD-1001",
    status: "PAID" as const,
    orderStatus: "Confirmed",
    paymentStatus: "Paid",
    totalAmount: 100,
    currency: "EGP"
  };

  it("rejects a route candidate when the current backend identity does not match", () => {
    expect(() => requireCanonicalPaymentStatusForOrder("route-order-2", backendStatus)).toThrow(/did not prove/i);
  });

  it("refuses confirmation navigation without exact backend identity evidence", () => {
    expect(() => buildOrderConfirmationPath("route-order-2", backendStatus)).toThrow(/unverified/i);
  });

  it("does not allow a fallback order number to become payment identity", () => {
    expect(buildOrderConfirmationPath("backend-order-1", backendStatus, "forged-order-number"))
      .toContain("orderNumber=ORD-1001");
  });
});

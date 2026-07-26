export type ActivePaymentRecoveryContext = {
  orderId: string;
  orderNumber: string;
  createdAt: string;
};

export const readActivePaymentRecoveryContext = (): ActivePaymentRecoveryContext | null => {
  // Recovery identity can only be presented after the current backend route
  // query proves it. Browser storage is never payment identity authority.
  return null;
};

export const persistActivePaymentRecoveryContext = (order: { orderId: string; orderNumber: string }): void => {
  // Mutation/list data does not create a durable recovery identity.
  void order;
};

export const clearActivePaymentRecoveryContext = (): void => {
  // Compatibility boundary; there is no retained frontend identity.
};

export const clearActivePaymentRecoveryContextIfMatches = (orderId: string): void => {
  const activeOrder = readActivePaymentRecoveryContext();
  if (!activeOrder || activeOrder.orderId !== orderId) {
    return;
  }

  clearActivePaymentRecoveryContext();
};

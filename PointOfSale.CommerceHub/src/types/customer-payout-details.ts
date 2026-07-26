import type { RefundPayoutMethod } from "./customer-refunds";

export type CustomerPayoutDetails = {
  id: string;
  method: RefundPayoutMethod;
  maskedAccountIdentifier: string;
  accountHolderName?: string | null;
  isVerified: boolean;
  createdAt: string;
  updatedAt?: string | null;
};

export type CustomerUpsertPayoutDetailsRequest = {
  method: RefundPayoutMethod;
  accountIdentifier?: string | null;
  accountHolderName?: string | null;
};

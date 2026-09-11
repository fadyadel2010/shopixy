import type { CustomerPayoutDetails, CustomerRefundPayoutSnapshot, RefundPayoutMethod } from "@/types";

const egyptianMobilePattern = /^01[0125]\d{8}$/;
const instapayHandlePattern = /^[a-zA-Z][a-zA-Z0-9._-]{2,49}$/;
const ibanPattern = /^[A-Z]{2}\d{2}[A-Z0-9]{10,30}$/;
const bankAccountPattern = /^\d{10,34}$/;

export const REFUND_PAYOUT_METHOD_OPTIONS: Array<{
  value: RefundPayoutMethod;
  label: string;
  accountLabel: string;
  accountPlaceholder: string;
  helperText: string;
}> = [
  {
    value: "VodafoneCash",
    label: "Vodafone Cash",
    accountLabel: "Mobile number",
    accountPlaceholder: "01012345678",
    helperText: "Enter the Egyptian mobile number linked to your Vodafone Cash wallet."
  },
  {
    value: "InstaPay",
    label: "InstaPay",
    accountLabel: "Phone or handle",
    accountPlaceholder: "01012345678 or yourhandle",
    helperText: "Use your InstaPay phone number or username handle."
  },
  {
    value: "BankTransfer",
    label: "Bank Transfer",
    accountLabel: "IBAN or account number",
    accountPlaceholder: "EG800002000156789012345180002",
    helperText: "Paste a full IBAN or the bank account number that should receive the refund."
  }
];

export function formatRefundPayoutMethodLabel(method: RefundPayoutMethod): string {
  return REFUND_PAYOUT_METHOD_OPTIONS.find((option) => option.value === method)?.label ?? method;
}

export function isDevelopmentRefundSimulation(payout?: CustomerRefundPayoutSnapshot | null): boolean {
  return payout?.isDevelopmentSimulation === true;
}

export function formatRefundPayoutInput(method: RefundPayoutMethod, rawValue: string): string {
  if (method === "BankTransfer") {
    return rawValue.replace(/[\s-]+/g, "").toUpperCase();
  }

  if (method === "VodafoneCash") {
    return normalizeEgyptianPhone(rawValue);
  }

  const trimmed = rawValue.trim();
  if (/^[\d\s()+-]*$/.test(trimmed)) {
    return normalizeEgyptianPhone(trimmed);
  }

  return trimmed.startsWith("@") ? trimmed.slice(1) : trimmed;
}

export function validateRefundPayoutInput(
  method: RefundPayoutMethod,
  rawAccountIdentifier: string
): string | null {
  const accountIdentifier = formatRefundPayoutInput(method, rawAccountIdentifier);
  if (!accountIdentifier) {
    return "Refund payout details are required.";
  }

  switch (method) {
    case "VodafoneCash":
      return egyptianMobilePattern.test(accountIdentifier)
        ? null
        : "Vodafone Cash requires a valid Egyptian mobile number.";

    case "InstaPay":
      return egyptianMobilePattern.test(accountIdentifier) || instapayHandlePattern.test(accountIdentifier)
        ? null
        : "InstaPay must be a valid Egyptian mobile number or handle.";

    case "BankTransfer":
      return ibanPattern.test(accountIdentifier) || bankAccountPattern.test(accountIdentifier)
        ? null
        : "Bank transfer details must be a valid IBAN or account number.";

    default:
      return "Refund payout method is invalid.";
  }
}

export function getRefundPayoutMethodMeta(method: RefundPayoutMethod) {
  return REFUND_PAYOUT_METHOD_OPTIONS.find((option) => option.value === method) ?? REFUND_PAYOUT_METHOD_OPTIONS[0];
}

export function getSavedPayoutLabel(payoutDetails: CustomerPayoutDetails): string {
  const methodLabel = formatRefundPayoutMethodLabel(payoutDetails.method);
  return `${methodLabel} · ${payoutDetails.maskedAccountIdentifier}`;
}

function normalizeEgyptianPhone(rawValue: string): string {
  const digits = rawValue.replace(/\D/g, "");

  if (digits.startsWith("0020")) {
    return `0${digits.slice(4, 15)}`.slice(0, 11);
  }

  if (digits.startsWith("20")) {
    return `0${digits.slice(2, 13)}`.slice(0, 11);
  }

  if (digits.startsWith("1")) {
    return `0${digits}`.slice(0, 11);
  }

  return digits.slice(0, 11);
}

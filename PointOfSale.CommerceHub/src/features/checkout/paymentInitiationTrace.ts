type PaymentInitiationSurface = "customer-order-details" | "payment-recovery";

type PaymentInitiationTraceDetails = Record<string, unknown>;

type ActivePaymentInitiationTrace = {
  attemptId: string;
  mark: (stage: string, details?: PaymentInitiationTraceDetails) => void;
};

let activeTrace: ActivePaymentInitiationTrace | null = null;

const createAttemptId = () => {
  try {
    return window.crypto.randomUUID();
  } catch {
    return `payment-${Date.now()}`;
  }
};

export const beginPaymentInitiationTrace = (
  surface: PaymentInitiationSurface,
  renderedOrderId: string
) => {
  const attemptId = createAttemptId();
  const startedAt = performance.now();
  const mark = (stage: string, details: PaymentInitiationTraceDetails = {}) => {
    if (!import.meta.env.DEV) {
      return;
    }

    console.info("[CommerceHub payment initiation]", {
      attemptId,
      surface,
      stage,
      timestampUtc: new Date().toISOString(),
      elapsedMs: Math.round((performance.now() - startedAt) * 10) / 10,
      ...details
    });
  };

  activeTrace = { attemptId, mark };
  mark("handler-entered", { renderedOrderId });

  return {
    mark,
    finish(details: PaymentInitiationTraceDetails = {}) {
      mark("handler-finished", details);
      if (activeTrace?.attemptId === attemptId) {
        activeTrace = null;
      }
    }
  };
};

export const markActivePaymentInitiationTrace = (
  stage: string,
  details: PaymentInitiationTraceDetails = {}
) => {
  activeTrace?.mark(stage, details);
};

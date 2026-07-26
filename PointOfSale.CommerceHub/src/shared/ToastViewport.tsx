import { CheckCircle2, CircleAlert, X } from "lucide-react";
import { useEffect } from "react";

import { useToastStore } from "@/store";

const TOAST_DURATION_MS = 2600;

export const ToastViewport = () => {
  const toasts = useToastStore((state) => state.toasts);
  const dismissToast = useToastStore((state) => state.dismissToast);

  useEffect(() => {
    if (toasts.length === 0) {
      return;
    }

    const timeouts = toasts.map((toast) =>
      window.setTimeout(() => {
        dismissToast(toast.id);
      }, TOAST_DURATION_MS)
    );

    return () => {
      timeouts.forEach((timeout) => window.clearTimeout(timeout));
    };
  }, [dismissToast, toasts]);

  return (
    <div className="pointer-events-none fixed inset-x-0 top-4 z-[80] flex flex-col items-center gap-3 px-4">
      {toasts.map((toast) => (
        <div
          key={toast.id}
          className={[
            "pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-3xl border px-4 py-4 shadow-card backdrop-blur-xl",
            toast.tone === "success"
              ? "border-emerald-200 bg-white/95 text-emerald-950"
              : "border-rose-200 bg-white/95 text-rose-950"
          ].join(" ")}
          role="status"
          aria-live="polite"
        >
          <div
            className={[
              "mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-full",
              toast.tone === "success" ? "bg-emerald-100 text-emerald-700" : "bg-rose-100 text-rose-700"
            ].join(" ")}
          >
            {toast.tone === "success" ? <CheckCircle2 className="h-4 w-4" /> : <CircleAlert className="h-4 w-4" />}
          </div>

          <div className="min-w-0 flex-1">
            <p className="text-sm font-semibold">{toast.title}</p>
            {toast.description ? <p className="mt-1 text-sm text-slate-600">{toast.description}</p> : null}
          </div>

          <button
            type="button"
            onClick={() => dismissToast(toast.id)}
            className="rounded-full p-1 text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-900"
            aria-label="Dismiss notification"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
      ))}
    </div>
  );
};

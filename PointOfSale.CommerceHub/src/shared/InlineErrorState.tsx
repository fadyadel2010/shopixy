import { AlertCircle } from "lucide-react";

import { Button } from "@/ui/button";

type InlineErrorStateProps = {
  title?: string;
  message: string;
  actionLabel?: string;
  onRetry?: () => void;
};

export const InlineErrorState = ({
  title = "Something went wrong",
  message,
  actionLabel = "Try again",
  onRetry
}: InlineErrorStateProps) => {
  return (
    <div className="rounded-2xl border border-rose-200 bg-rose-50/90 p-4 text-left">
      <div className="flex items-start gap-3">
        <div className="mt-0.5 flex h-9 w-9 items-center justify-center rounded-full bg-white text-rose-600">
          <AlertCircle className="h-4 w-4" />
        </div>
        <div className="min-w-0 flex-1 space-y-1">
          <p className="text-sm font-semibold text-rose-900">{title}</p>
          <p className="text-sm leading-6 text-rose-800">{message}</p>
          {onRetry ? (
            <Button type="button" variant="outline" size="sm" className="mt-2 border-rose-200 bg-white" onClick={onRetry}>
              {actionLabel}
            </Button>
          ) : null}
        </div>
      </div>
    </div>
  );
};

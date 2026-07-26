import { FileImage, Film, X } from "lucide-react";

import type { RefundEvidenceMedia, RefundReasonCode } from "@/types";
import { Button } from "@/ui/button";

export const REFUND_REASON_OPTIONS: Array<{ value: RefundReasonCode; label: string }> = [
  { value: "DamagedItem", label: "Damaged item" },
  { value: "NotAsDescribed", label: "Not as described" },
  { value: "WrongItem", label: "Wrong item" },
  { value: "MissingParts", label: "Missing parts" },
  { value: "QualityIssue", label: "Quality issue" },
  { value: "Other", label: "Other" }
];

export const formatRefundReasonLabel = (reasonCode?: RefundReasonCode | null) =>
  REFUND_REASON_OPTIONS.find((option) => option.value === reasonCode)?.label ?? "Other";

export const formatRefundFileSize = (sizeBytes?: number) => {
  if (!sizeBytes || sizeBytes <= 0) {
    return "Pending size";
  }

  const megaBytes = sizeBytes / (1024 * 1024);
  if (megaBytes >= 1) {
    return `${megaBytes.toFixed(1)} MB`;
  }

  return `${Math.max(1, Math.round(sizeBytes / 1024))} KB`;
};

type RefundEvidenceGridProps = {
  media: RefundEvidenceMedia[];
  compact?: boolean;
};

export const RefundEvidenceGrid = ({ media, compact = false }: RefundEvidenceGridProps) => {
  if (media.length === 0) {
    return (
      <div className="rounded-[1.25rem] border border-dashed border-border/70 bg-slate-50/70 px-4 py-5 text-sm text-muted-foreground">
        No evidence attached.
      </div>
    );
  }

  return (
    <div className={`grid gap-3 ${compact ? "grid-cols-2 sm:grid-cols-3" : "grid-cols-1 sm:grid-cols-2"}`}>
      {media.map((item) => (
        <a
          key={item.mediaId}
          href={item.url}
          target="_blank"
          rel="noreferrer"
          className="group overflow-hidden rounded-[1.5rem] border border-border/70 bg-white shadow-sm transition hover:-translate-y-0.5 hover:shadow-md"
        >
          <div className="flex aspect-[4/3] items-center justify-center overflow-hidden bg-slate-100">
            {item.mediaType === "video" ? (
              <video
                src={item.url}
                className="h-full w-full object-cover"
                controls={!compact}
                preload="metadata"
              />
            ) : (
              <img src={item.url} alt={item.fileName} className="h-full w-full object-cover" />
            )}
          </div>

          <div className="flex items-start gap-3 px-4 py-3">
            <div className="rounded-full bg-slate-100 p-2 text-slate-600">
              {item.mediaType === "video" ? <Film className="h-4 w-4" /> : <FileImage className="h-4 w-4" />}
            </div>
            <div className="min-w-0">
              <p className="truncate text-sm font-semibold text-foreground">{item.fileName}</p>
              <p className="text-xs text-muted-foreground">
                {item.mediaType === "video" ? "Video" : "Image"} · {formatRefundFileSize(item.sizeBytes)}
              </p>
            </div>
          </div>
        </a>
      ))}
    </div>
  );
};

type RefundEvidenceDialogProps = {
  open: boolean;
  title: string;
  media: RefundEvidenceMedia[];
  reasonCode?: RefundReasonCode | null;
  reasonText?: string | null;
  onClose: () => void;
};

export const RefundEvidenceDialog = ({
  open,
  title,
  media,
  reasonCode,
  reasonText,
  onClose
}: RefundEvidenceDialogProps) => {
  if (!open) {
    return null;
  }

  return (
    <div className="fixed inset-0 z-[75] flex items-center justify-center bg-slate-950/60 px-4 py-8 backdrop-blur-sm" onClick={onClose}>
      <div
        className="w-full max-w-5xl rounded-[2rem] border border-white/70 bg-white p-6 shadow-2xl"
        onClick={(event) => event.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-2">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Refund evidence</p>
            <h2 className="text-2xl font-black tracking-tight text-foreground">{title}</h2>
            <p className="text-sm text-muted-foreground">
              {formatRefundReasonLabel(reasonCode)}
              {reasonText ? ` · ${reasonText}` : ""}
            </p>
          </div>

          <Button type="button" variant="ghost" size="icon" onClick={onClose} aria-label="Close evidence viewer">
            <X className="h-5 w-5" />
          </Button>
        </div>

        <div className="mt-6">
          <RefundEvidenceGrid media={media} />
        </div>
      </div>
    </div>
  );
};

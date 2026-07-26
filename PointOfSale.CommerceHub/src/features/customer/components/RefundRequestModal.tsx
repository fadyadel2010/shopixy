import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CircleAlert, ImagePlus, Loader2, RotateCcw, UploadCloud, X } from "lucide-react";

import {
  customerPayoutDetailsQueryOptions,
  customerQueryKeys
} from "@/features/customer/queries";
import {
  formatRefundPayoutInput,
  formatRefundPayoutMethodLabel,
  getRefundPayoutMethodMeta,
  getSavedPayoutLabel,
  REFUND_PAYOUT_METHOD_OPTIONS,
  validateRefundPayoutInput
} from "@/features/customer/payout-details";
import { REFUND_REASON_OPTIONS, formatRefundFileSize, formatRefundReasonLabel } from "@/features/customer/refund-evidence";
import { customerPayoutDetailsService, customerRefundsService } from "@/services";
import { useToastStore } from "@/store";
import type { CustomerOrderItem, RefundPayoutMethod, RefundReasonCode } from "@/types";
import { Button } from "@/ui/button";
import { formatCurrency } from "@/utils";

type RefundRequestModalProps = {
  currency: string;
  item: CustomerOrderItem | null;
  open: boolean;
  onClose: () => void;
  resolveCurrentItem: (orderItemId: string) => Promise<CustomerOrderItem | null>;
};

type UploadStatus = "uploading" | "uploaded" | "failed";

type UploadItem = {
  id: string;
  file: File;
  previewUrl: string;
  mediaType: "image" | "video";
  status: UploadStatus;
  progress: number;
  fileUrl?: string;
  error?: string;
};

type PayoutMode = "saved" | "manual";

const MAX_FILES = 5;
const MAX_IMAGE_BYTES = 10 * 1024 * 1024;
const MAX_VIDEO_BYTES = 50 * 1024 * 1024;
const ACCEPTED_TYPES = ["image/jpeg", "image/png", "video/mp4"];

export const RefundRequestModal = ({ currency, item, open, onClose, resolveCurrentItem }: RefundRequestModalProps) => {
  const queryClient = useQueryClient();
  const showToast = useToastStore((state) => state.showToast);
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const payoutDetailsQuery = useQuery({
    ...customerPayoutDetailsQueryOptions(),
    enabled: open
  });
  const [quantity, setQuantity] = useState(1);
  const [reasonCode, setReasonCode] = useState<RefundReasonCode | "">("");
  const [reasonText, setReasonText] = useState("");
  const [idempotencyKey, setIdempotencyKey] = useState("");
  const [uploads, setUploads] = useState<UploadItem[]>([]);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [isDragActive, setIsDragActive] = useState(false);
  const [payoutMode, setPayoutMode] = useState<PayoutMode>("manual");
  const [selectedPayoutDetailsId, setSelectedPayoutDetailsId] = useState("");
  const [payoutMethod, setPayoutMethod] = useState<RefundPayoutMethod>("VodafoneCash");
  const [payoutAccountIdentifier, setPayoutAccountIdentifier] = useState("");
  const [payoutAccountHolderName, setPayoutAccountHolderName] = useState("");
  const [savePayoutDetails, setSavePayoutDetails] = useState(false);

  useEffect(() => {
    if (!item || !open) {
      return;
    }

    setQuantity(item.maxRefundableQuantity > 0 ? 1 : 0);
    setReasonCode("");
    setReasonText("");
    setIdempotencyKey(createRefundIdempotencyKey());
    setUploadError(null);
    setPayoutMode("manual");
    setSelectedPayoutDetailsId("");
    setPayoutMethod("VodafoneCash");
    setPayoutAccountIdentifier("");
    setPayoutAccountHolderName("");
    setSavePayoutDetails(false);
    setUploads((current) => {
      current.forEach((upload) => URL.revokeObjectURL(upload.previewUrl));
      return [];
    });
  }, [item, open]);

  useEffect(() => {
    if (open) {
      return;
    }

    setUploads((current) => {
      current.forEach((upload) => URL.revokeObjectURL(upload.previewUrl));
      return [];
    });
  }, [open]);

  useEffect(() => {
    if (!open || payoutDetailsQuery.isLoading) {
      return;
    }

    const savedPayoutDetails = payoutDetailsQuery.data ?? [];
    if (savedPayoutDetails.length === 0) {
      setPayoutMode("manual");
      return;
    }

    setSelectedPayoutDetailsId((current) => current || savedPayoutDetails[0].id);
    setPayoutMode((current) => (current === "manual" ? "saved" : current));
  }, [open, payoutDetailsQuery.data, payoutDetailsQuery.isLoading]);

  const uploadedFiles = uploads.filter((upload) => upload.status === "uploaded" && upload.fileUrl);
  const hasUploadingFiles = uploads.some((upload) => upload.status === "uploading");
  const hasFailedFiles = uploads.some((upload) => upload.status === "failed");
  const savedPayoutDetails = payoutDetailsQuery.data ?? [];
  const selectedSavedPayoutDetails =
    savedPayoutDetails.find((payoutDetails) => payoutDetails.id === selectedPayoutDetailsId) ?? null;
  const manualPayoutValidationError = validateRefundPayoutInput(payoutMethod, payoutAccountIdentifier);
  const currentPayoutError = payoutMode === "saved"
    ? (!selectedPayoutDetailsId || !selectedSavedPayoutDetails ? "Select a saved payout method or enter one-time payout details." : null)
    : manualPayoutValidationError;
  const payoutMethodMeta = getRefundPayoutMethodMeta(payoutMethod);

  const createRefundMutation = useMutation({
    mutationFn: async () => {
      if (!item) {
        throw new Error("Refund item is required.");
      }

      if (!idempotencyKey) {
        throw new Error("Refund request token is missing. Please try again.");
      }

      if (!reasonCode) {
        throw new Error("Please select a reason.");
      }

      const currentItem = await resolveCurrentItem(item.orderItemId);
      if (!currentItem?.canRefund) {
        throw new Error("Refund is no longer authorized for this item.");
      }

      if (quantity > currentItem.maxRefundableQuantity) {
        throw new Error("Refund quantity is no longer authorized for this item.");
      }

      let savedPayoutDetailsId: string | undefined;
      let payoutDetails:
        | {
            method: RefundPayoutMethod;
            accountIdentifier: string;
            accountHolderName?: string;
          }
        | undefined;

      if (payoutMode === "saved") {
        const refreshedPayoutDetails = await payoutDetailsQuery.refetch();
        const currentSavedPayoutDetails = refreshedPayoutDetails.data ?? [];
        const currentSavedPayout = currentSavedPayoutDetails.find((payoutDetails) => payoutDetails.id === selectedPayoutDetailsId) ?? null;

        if (refreshedPayoutDetails.isError || !selectedPayoutDetailsId || !currentSavedPayout) {
          throw new Error("Select a saved payout method or enter one-time payout details.");
        }

        savedPayoutDetailsId = selectedPayoutDetailsId;
      } else {
        const payoutError = validateRefundPayoutInput(payoutMethod, payoutAccountIdentifier);
        if (payoutError) {
          throw new Error(payoutError);
        }

        const manualPayout = {
          method: payoutMethod,
          accountIdentifier: formatRefundPayoutInput(payoutMethod, payoutAccountIdentifier),
          accountHolderName: payoutAccountHolderName.trim() || undefined
        };

        if (savePayoutDetails) {
          const savedPayout = await customerPayoutDetailsService.createPayoutDetails(manualPayout);
          const refreshedPayoutDetails = await payoutDetailsQuery.refetch();
          const currentSavedPayoutDetails = refreshedPayoutDetails.data ?? [];
          const currentSavedPayout = currentSavedPayoutDetails.find((payoutDetails) => payoutDetails.id === savedPayout.id) ?? null;

          if (refreshedPayoutDetails.isError || !currentSavedPayout) {
            throw new Error("Saved payout method is not available from current backend authority. Please try again.");
          }

          savedPayoutDetailsId = currentSavedPayout.id;
        } else {
          payoutDetails = manualPayout;
        }
      }

      return customerRefundsService.createRefund(
        {
          orderItemId: currentItem.orderItemId,
          quantity,
          reasonCode,
          reasonText: reasonText.trim() || undefined,
          savedPayoutDetailsId,
          payoutDetails,
          mediaUrls: uploadedFiles.map((upload) => upload.fileUrl!)
        },
        idempotencyKey
      );
    },
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: customerQueryKeys.orders() }),
        queryClient.invalidateQueries({ queryKey: customerQueryKeys.refunds() }),
        queryClient.invalidateQueries({ queryKey: customerQueryKeys.payoutDetails() })
      ]);

      showToast({
        tone: "success",
        title: "Refund request submitted",
        description: "Your refund request and evidence were submitted successfully."
      });

      onClose();
    }
  });

  if (!open || !item) {
    return null;
  }

  const submitDisabled =
    createRefundMutation.isPending ||
    !item.canRefund ||
    !idempotencyKey ||
    quantity <= 0 ||
    quantity > item.maxRefundableQuantity ||
    !reasonCode ||
    Boolean(currentPayoutError) ||
    hasUploadingFiles ||
    hasFailedFiles;

  const handleFilesSelected = (files: FileList | null) => {
    if (!files) {
      return;
    }

    const remainingSlots = MAX_FILES - uploads.length;
    if (remainingSlots <= 0) {
      setUploadError(`You can attach up to ${MAX_FILES} files.`);
      return;
    }

    const pickedFiles = Array.from(files).slice(0, remainingSlots);
    const nextUploads: UploadItem[] = [];
    const validationErrors: string[] = [];

    pickedFiles.forEach((file) => {
      if (!ACCEPTED_TYPES.includes(file.type)) {
        validationErrors.push(`${file.name}: only JPG, PNG, and MP4 are supported.`);
        return;
      }

      const mediaType = file.type.startsWith("video/") ? "video" : "image";
      const maxBytes = mediaType === "video" ? MAX_VIDEO_BYTES : MAX_IMAGE_BYTES;
      if (file.size > maxBytes) {
        validationErrors.push(
          `${file.name}: ${mediaType === "video" ? "Video files must be 50MB or smaller." : "Image files must be 10MB or smaller."}`
        );
        return;
      }

      nextUploads.push({
        id: createRefundIdempotencyKey(),
        file,
        previewUrl: URL.createObjectURL(file),
        mediaType,
        status: "uploading",
        progress: 0
      });
    });

    if (validationErrors.length > 0) {
      setUploadError(validationErrors.join(" "));
    } else {
      setUploadError(null);
    }

    if (nextUploads.length === 0) {
      return;
    }

    setUploads((current) => [...current, ...nextUploads]);
    nextUploads.forEach((upload) => {
      void uploadFile(upload.id, upload.file);
    });
  };

  const updateUpload = (uploadId: string, patch: Partial<UploadItem>) => {
    setUploads((current) =>
      current.map((upload) => (upload.id === uploadId ? { ...upload, ...patch } : upload))
    );
  };

  const uploadFile = async (uploadId: string, file: File) => {
    updateUpload(uploadId, { status: "uploading", progress: 0, error: undefined });

    try {
      // Evidence upload is an intent surface too. Re-enter the current order-item
      // authority before creating an upload that could later be attached to a
      // refund request; the open modal's item is only a candidate.
      const currentItem = await resolveCurrentItem(item.orderItemId);
      if (!currentItem?.canRefund) {
        throw new Error("Refund evidence upload is no longer authorized for this item.");
      }

      const sha256 = await computeFileSha256(file);

      const uploadResult = await customerRefundsService.uploadEvidence(file, sha256, (percentage) => {
        updateUpload(uploadId, { progress: percentage });
      });

      updateUpload(uploadId, {
        status: "uploaded",
        progress: 100,
        fileUrl: uploadResult.fileUrl,
        error: undefined
      });
    } catch (error) {
      updateUpload(uploadId, {
        status: "failed",
        progress: 0,
        fileUrl: undefined,
        error: normalizeUploadError(error)
      });
    }
  };

  const removeUpload = (uploadId: string) => {
    setUploads((current) => {
      const upload = current.find((entry) => entry.id === uploadId);
      if (upload) {
        URL.revokeObjectURL(upload.previewUrl);
      }

      return current.filter((entry) => entry.id !== uploadId);
    });
  };

  return (
    <div className="fixed inset-0 z-[70] flex items-start justify-center overflow-y-auto bg-slate-950/55 px-3 py-4 backdrop-blur-sm sm:px-6 sm:py-6" onClick={onClose}>
      <div
        className="my-auto w-full max-w-[1180px] overflow-hidden rounded-[2rem] border border-white/70 bg-white shadow-2xl"
        onClick={(event) => event.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-labelledby="refund-request-title"
      >
        <div className="flex max-h-[calc(100vh-2rem)] flex-col sm:max-h-[calc(100vh-3rem)]">
          <div className="flex items-start justify-between gap-4 border-b border-border/60 px-5 py-5 sm:px-6">
          <div className="space-y-2">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Refund request</p>
            <h2 id="refund-request-title" className="text-2xl font-black tracking-tight text-foreground">
              {item.productName}
            </h2>
            {item.variant ? <p className="text-sm text-muted-foreground">{item.variant}</p> : null}
            <p className="max-w-2xl text-sm leading-6 text-muted-foreground">
              Review the item details, choose where the refund should be sent, and add evidence if it helps explain the issue.
            </p>
          </div>

          <Button type="button" variant="ghost" size="icon" onClick={onClose} aria-label="Close refund request">
            <X className="h-5 w-5" />
          </Button>
        </div>

          <div className="flex-1 overflow-y-auto px-5 py-5 sm:px-6">
            <div className="grid gap-4 md:grid-cols-3">
          <div className="rounded-[1.5rem] border border-border/70 bg-slate-50/80 px-4 py-4">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Unit price</p>
            <p className="mt-2 text-lg font-black tracking-tight text-foreground">{formatCurrency(item.price, currency)}</p>
          </div>

          <div className="rounded-[1.5rem] border border-border/70 bg-slate-50/80 px-4 py-4">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Max quantity</p>
            <p className="mt-2 text-lg font-black tracking-tight text-foreground">{item.maxRefundableQuantity}</p>
          </div>

          <div className="rounded-[1.5rem] border border-border/70 bg-slate-50/80 px-4 py-4">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Refund amount</p>
            <p className="mt-2 text-sm font-semibold leading-6 text-foreground">Confirmed by the backend after review</p>
          </div>
        </div>

            <div className="mt-6 grid gap-6 xl:grid-cols-[minmax(0,1.05fr)_minmax(360px,0.95fr)]">
              <div className="space-y-5 xl:pr-2">
            <label className="space-y-2">
              <span className="text-sm font-semibold text-foreground">Quantity</span>
              <select
                value={quantity}
                onChange={(event) => setQuantity(Number.parseInt(event.target.value, 10))}
                className="flex h-12 w-full rounded-2xl border border-input bg-card px-4 text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50"
                disabled={createRefundMutation.isPending}
              >
                {Array.from({ length: item.maxRefundableQuantity }, (_, index) => index + 1).map((value) => (
                  <option key={value} value={value}>
                    {value}
                  </option>
                ))}
              </select>
            </label>

            <label className="space-y-2">
              <span className="text-sm font-semibold text-foreground">Reason</span>
              <select
                value={reasonCode}
                onChange={(event) => setReasonCode(event.target.value as RefundReasonCode | "")}
                className="flex h-12 w-full rounded-2xl border border-input bg-card px-4 text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                disabled={createRefundMutation.isPending}
              >
                <option value="">Please select a reason</option>
                {REFUND_REASON_OPTIONS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
              {!reasonCode ? <p className="text-xs text-rose-600">Please select a reason.</p> : null}
            </label>

            <div className="space-y-4 rounded-[1.5rem] border border-border/70 bg-slate-50/80 px-4 py-4">
              <div className="space-y-1">
                <p className="text-sm font-semibold text-foreground">Refund method</p>
                <p className="text-xs leading-5 text-muted-foreground">
                  Choose where the approved refund should be sent. This snapshot is locked onto the refund request.
                </p>
              </div>

              {savedPayoutDetails.length > 0 ? (
                <div className="grid gap-2 sm:grid-cols-2">
                  <button
                    type="button"
                    className={[
                      "rounded-[1.25rem] border px-4 py-3 text-left text-sm transition",
                      payoutMode === "saved"
                        ? "border-primary bg-white text-foreground shadow-sm"
                        : "border-border/70 bg-white/70 text-muted-foreground hover:bg-white"
                    ].join(" ")}
                    onClick={() => setPayoutMode("saved")}
                    disabled={createRefundMutation.isPending}
                  >
                    <span className="block font-semibold">Use saved method</span>
                    <span className="mt-1 block text-xs">Reuse a masked payout method you already stored.</span>
                  </button>

                  <button
                    type="button"
                    className={[
                      "rounded-[1.25rem] border px-4 py-3 text-left text-sm transition",
                      payoutMode === "manual"
                        ? "border-primary bg-white text-foreground shadow-sm"
                        : "border-border/70 bg-white/70 text-muted-foreground hover:bg-white"
                    ].join(" ")}
                    onClick={() => setPayoutMode("manual")}
                    disabled={createRefundMutation.isPending}
                  >
                    <span className="block font-semibold">Enter one-time details</span>
                    <span className="mt-1 block text-xs">Use a different payout destination for this refund only.</span>
                  </button>
                </div>
              ) : null}

              {payoutMode === "saved" && savedPayoutDetails.length > 0 ? (
                <label className="space-y-2">
                  <span className="text-sm font-semibold text-foreground">Saved payout method</span>
                  <select
                    value={selectedPayoutDetailsId}
                    onChange={(event) => setSelectedPayoutDetailsId(event.target.value)}
                    className="flex h-12 w-full rounded-2xl border border-input bg-white px-4 text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50"
                    disabled={createRefundMutation.isPending || payoutDetailsQuery.isLoading}
                  >
                    <option value="">Select a saved payout method</option>
                    {savedPayoutDetails.map((payoutDetails) => (
                      <option key={payoutDetails.id} value={payoutDetails.id}>
                        {getSavedPayoutLabel(payoutDetails)}
                      </option>
                    ))}
                  </select>
                </label>
              ) : (
                <div className="space-y-4">
                  <label className="space-y-2">
                    <span className="text-sm font-semibold text-foreground">Refund method</span>
                    <select
                      value={payoutMethod}
                      onChange={(event) => {
                        const nextMethod = event.target.value as RefundPayoutMethod;
                        setPayoutMethod(nextMethod);
                        setPayoutAccountIdentifier((current) => formatRefundPayoutInput(nextMethod, current));
                      }}
                      className="flex h-12 w-full rounded-2xl border border-input bg-white px-4 text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50"
                      disabled={createRefundMutation.isPending}
                    >
                      {REFUND_PAYOUT_METHOD_OPTIONS.map((option) => (
                        <option key={option.value} value={option.value}>
                          {option.label}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label className="space-y-2">
                    <span className="text-sm font-semibold text-foreground">{payoutMethodMeta.accountLabel}</span>
                    <input
                      value={payoutAccountIdentifier}
                      onChange={(event) => setPayoutAccountIdentifier(formatRefundPayoutInput(payoutMethod, event.target.value))}
                      placeholder={payoutMethodMeta.accountPlaceholder}
                      disabled={createRefundMutation.isPending}
                      className="flex h-12 w-full rounded-2xl border border-input bg-white px-4 text-sm transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50"
                    />
                    <p className="text-xs text-muted-foreground">{payoutMethodMeta.helperText}</p>
                  </label>

                  <label className="space-y-2">
                    <span className="text-sm font-semibold text-foreground">Account holder name</span>
                    <input
                      value={payoutAccountHolderName}
                      onChange={(event) => setPayoutAccountHolderName(event.target.value)}
                      placeholder={payoutMethod === "BankTransfer" ? "Optional but recommended for bank transfers" : "Optional"}
                      maxLength={200}
                      disabled={createRefundMutation.isPending}
                      className="flex h-12 w-full rounded-2xl border border-input bg-white px-4 text-sm transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50"
                    />
                  </label>

                  <label className="flex items-start gap-3 rounded-[1.25rem] border border-border/70 bg-white px-4 py-3 text-sm text-foreground">
                    <input
                      type="checkbox"
                      checked={savePayoutDetails}
                      onChange={(event) => setSavePayoutDetails(event.target.checked)}
                      disabled={createRefundMutation.isPending}
                      className="mt-1 h-4 w-4 rounded border-border"
                    />
                    <span>
                      <span className="block font-semibold">Save this payout method</span>
                      <span className="mt-1 block text-xs text-muted-foreground">
                        Reuse these masked payout details for future refunds without affecting this refund snapshot.
                      </span>
                    </span>
                  </label>
                </div>
              )}

              {payoutDetailsQuery.isError ? (
                <p className="text-xs text-amber-700">
                  Saved payout methods are temporarily unavailable, but you can still enter one-time details below.
                </p>
              ) : null}

              {currentPayoutError ? <p className="text-xs text-rose-600">{currentPayoutError}</p> : null}

              {!currentPayoutError ? (
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                  {payoutMode === "saved" && selectedPayoutDetailsId
                    ? selectedSavedPayoutDetails
                      ? getSavedPayoutLabel(selectedSavedPayoutDetails)
                      : "Saved payout method selected"
                    : `${formatRefundPayoutMethodLabel(payoutMethod)} ready`}
                </p>
              ) : null}
            </div>

            <label className="space-y-2">
              <span className="text-sm font-semibold text-foreground">Additional details</span>
              <textarea
                value={reasonText}
                onChange={(event) => setReasonText(event.target.value)}
                placeholder="Describe the issue in more detail if it will help the review team."
                rows={5}
                maxLength={1000}
                disabled={createRefundMutation.isPending}
                className="flex w-full rounded-[1.5rem] border border-input bg-card px-4 py-3 text-sm transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50"
              />
              <p className="text-xs text-muted-foreground">{reasonText.trim().length}/1000 characters</p>
            </label>
          </div>

              <div className="space-y-4 xl:sticky xl:top-0 xl:self-start">
            <div
              className={[
                "rounded-[1.75rem] border border-dashed bg-white px-5 py-6 transition-colors shadow-sm",
                isDragActive ? "border-primary bg-primary/5" : "border-border/80"
              ].join(" ")}
              onDragEnter={(event) => {
                event.preventDefault();
                setIsDragActive(true);
              }}
              onDragOver={(event) => {
                event.preventDefault();
                setIsDragActive(true);
              }}
              onDragLeave={(event) => {
                event.preventDefault();
                setIsDragActive(false);
              }}
              onDrop={(event) => {
                event.preventDefault();
                setIsDragActive(false);
                handleFilesSelected(event.dataTransfer.files);
              }}
            >
              <input
                ref={fileInputRef}
                type="file"
                accept=".jpg,.jpeg,.png,.mp4,image/jpeg,image/png,video/mp4"
                multiple
                className="hidden"
                onChange={(event) => {
                  handleFilesSelected(event.target.files);
                  event.currentTarget.value = "";
                }}
              />

              <div className="flex flex-col items-center text-center">
                <div className="rounded-full bg-white p-3 text-primary shadow-sm">
                  <UploadCloud className="h-6 w-6" />
                </div>
                <h3 className="mt-4 text-lg font-black tracking-tight text-foreground">Upload photos or video</h3>
                <p className="mt-2 max-w-md text-sm leading-6 text-muted-foreground">
                  JPG and PNG up to 10MB. MP4 up to 50MB. Up to {MAX_FILES} files total.
                </p>
                <div className="mt-4 flex flex-wrap justify-center gap-3">
                  <Button type="button" variant="outline" onClick={() => fileInputRef.current?.click()}>
                    <ImagePlus className="mr-2 h-4 w-4" />
                    Choose files
                  </Button>
                  <span className="rounded-full border border-border/70 bg-white px-3 py-2 text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                    {uploads.length}/{MAX_FILES} used
                  </span>
                </div>
              </div>
            </div>

            {uploadError ? (
              <div className="rounded-[1.5rem] border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
                <div className="flex items-start gap-2">
                  <CircleAlert className="mt-0.5 h-4 w-4 shrink-0" />
                  <p>{uploadError}</p>
                </div>
              </div>
            ) : null}

            <div className="space-y-3 xl:max-h-[360px] xl:overflow-y-auto xl:pr-1">
              {uploads.length === 0 ? (
                <div className="rounded-[1.5rem] border border-border/70 bg-slate-50/80 px-4 py-5 text-sm text-muted-foreground">
                  Evidence is optional, but adding photos or a short video can speed up review.
                </div>
              ) : (
                uploads.map((upload) => (
                  <div key={upload.id} className="rounded-[1.5rem] border border-border/70 bg-white p-4 shadow-sm">
                    <div className="flex gap-4">
                      <div className="flex h-20 w-20 shrink-0 items-center justify-center overflow-hidden rounded-[1.25rem] bg-slate-100">
                        {upload.mediaType === "video" ? (
                          <video src={upload.previewUrl} className="h-full w-full object-cover" muted />
                        ) : (
                          <img src={upload.previewUrl} alt={upload.file.name} className="h-full w-full object-cover" />
                        )}
                      </div>

                      <div className="min-w-0 flex-1">
                        <div className="flex items-start justify-between gap-3">
                          <div>
                            <p className="truncate text-sm font-semibold text-foreground">{upload.file.name}</p>
                            <p className="text-xs text-muted-foreground">
                              {upload.mediaType === "video" ? "Video" : "Image"} · {formatRefundFileSize(upload.file.size)}
                            </p>
                          </div>

                          <Button type="button" variant="ghost" size="icon" onClick={() => removeUpload(upload.id)}>
                            <X className="h-4 w-4" />
                          </Button>
                        </div>

                        <div className="mt-3 space-y-2">
                          <div className="h-2 rounded-full bg-slate-100">
                            <div
                              className={[
                                "h-2 rounded-full transition-all",
                                upload.status === "failed" ? "bg-rose-500" : "bg-primary"
                              ].join(" ")}
                              style={{ width: `${upload.status === "failed" ? 100 : upload.progress}%` }}
                            />
                          </div>

                          <div className="flex flex-wrap items-center justify-between gap-2 text-xs">
                            <span className="font-semibold text-foreground">
                              {upload.status === "uploaded"
                                ? "Uploaded"
                                : upload.status === "failed"
                                  ? "Upload failed"
                                  : `Uploading ${upload.progress}%`}
                            </span>

                            {upload.status === "failed" ? (
                              <Button
                                type="button"
                                variant="outline"
                                size="sm"
                                onClick={() => void uploadFile(upload.id, upload.file)}
                              >
                                <RotateCcw className="mr-2 h-3.5 w-3.5" />
                                Retry
                              </Button>
                            ) : upload.status === "uploading" ? (
                              <span className="inline-flex items-center gap-2 text-muted-foreground">
                                <Loader2 className="h-3.5 w-3.5 animate-spin" />
                                Processing
                              </span>
                            ) : null}
                          </div>

                          {upload.error ? <p className="text-xs text-rose-600">{upload.error}</p> : null}
                        </div>
                      </div>
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>
        </div>

          </div>

          <div className="border-t border-border/60 bg-white px-5 py-4 sm:px-6">
            {createRefundMutation.isError ? (
              <div className="mb-4 rounded-[1.5rem] border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
                <div className="flex items-start gap-2">
                  <CircleAlert className="mt-0.5 h-4 w-4 shrink-0" />
                  <p>{createRefundMutation.error.message}</p>
                </div>
              </div>
            ) : null}

            <div className="flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
              <div className="mr-auto flex items-center text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                {reasonCode ? `${formatRefundReasonLabel(reasonCode)} selected` : "Reason required"}
              </div>
              <Button type="button" variant="outline" onClick={onClose} disabled={createRefundMutation.isPending}>
                Cancel
              </Button>
              <Button type="button" onClick={() => createRefundMutation.mutate()} disabled={submitDisabled}>
                {createRefundMutation.isPending ? "Submitting..." : "Submit refund request"}
              </Button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

const createRefundIdempotencyKey = () =>
  typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
    ? crypto.randomUUID()
    : `refund-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;

const normalizeUploadError = (error: unknown) => {
  if (error instanceof Error && error.message) {
    return error.message;
  }

  return "Upload failed. Please retry.";
};

const computeFileSha256 = async (file: File) => {
  if (typeof crypto === "undefined" || !crypto.subtle) {
    throw new Error("This browser does not support secure file fingerprinting.");
  }

  const buffer = await file.arrayBuffer();
  const digest = await crypto.subtle.digest("SHA-256", buffer);
  return Array.from(new Uint8Array(digest))
    .map((value) => value.toString(16).padStart(2, "0"))
    .join("");
};

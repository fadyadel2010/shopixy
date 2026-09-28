/**
 * Manual Wallet Fields Component
 *
 * Displays wallet type selector, merchant wallet details, and proof upload.
 * Used inside PaymentStep when Manual Wallet is selected.
 */

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from '@/i18n';
import { httpClient, normalizeError } from '@/services/api';
import {
    selectCheckoutShowAllErrors,
    selectManualWalletConfig,
    selectManualWalletConfigError,
    useCheckoutStore,
    validateManualWalletFields,
} from '@/store/checkoutStore';

const DEFAULT_MAX_FILE_BYTES = 5 * 1024 * 1024;

type ProofPreviewKind = 'image' | 'pdf' | null;

function formatMaxFileSize(maxFileBytes: number): string {
    const sizeInMb = maxFileBytes / (1024 * 1024);
    return Number.isInteger(sizeInMb) ? `${sizeInMb} MB` : `${sizeInMb.toFixed(1)} MB`;
}

function isPdfFile(file: File): boolean {
    return file.type === 'application/pdf' || file.name.toLowerCase().endsWith('.pdf');
}

export const ManualWalletFields: React.FC = () => {
    const { t, isRTL } = useTranslation();
    const isAr = isRTL;

    const manualWalletType = useCheckoutStore((state) => state.manualWalletType);
    const manualWalletPhoneNumber = useCheckoutStore((state) => state.manualWalletPhoneNumber);
    const merchantWalletNumber = useCheckoutStore((state) => state.merchantWalletNumber);
    const manualWalletConfig = useCheckoutStore(selectManualWalletConfig);
    const manualWalletConfigError = useCheckoutStore(selectManualWalletConfigError);
    const proofFileKey = useCheckoutStore((state) => state.manualWalletProofFileKey);
    const showAllErrors = useCheckoutStore(selectCheckoutShowAllErrors);

    const setManualWalletType = useCheckoutStore((state) => state.setManualWalletType);
    const setManualWalletPhoneNumber = useCheckoutStore((state) => state.setManualWalletPhoneNumber);
    const setManualWalletProofFileKey = useCheckoutStore((state) => state.setManualWalletProofFileKey);
    const fetchConfig = useCheckoutStore((state) => state.fetchManualWalletConfig);
    const initCheckoutSession = useCheckoutStore((state) => state.initCheckoutSession);

    const [uploading, setUploading] = useState(false);
    const [uploadError, setUploadError] = useState<string | null>(null);
    const [clearMessage, setClearMessage] = useState<string | null>(null);
    const [previewUrl, setPreviewUrl] = useState<string | null>(null);
    const [previewKind, setPreviewKind] = useState<ProofPreviewKind>(null);

    const walletTypes = useMemo(
        () => manualWalletConfig?.walletTypes?.filter((wallet) => typeof wallet.code === 'string' && wallet.code.trim()) ?? [],
        [manualWalletConfig?.walletTypes],
    );

    const validationErrors = useMemo(() => validateManualWalletFields({
        walletType: manualWalletType,
        walletPhoneNumber: manualWalletPhoneNumber,
        proofFileKey,
        walletTypes,
        requireKnownWalletType: true,
    }), [manualWalletPhoneNumber, manualWalletType, proofFileKey, walletTypes]);

    const maxFileBytes = manualWalletConfig?.maxFileBytes ?? DEFAULT_MAX_FILE_BYTES;
    const allowedMimeTypes = manualWalletConfig?.allowedMimeTypes?.length
        ? manualWalletConfig.allowedMimeTypes
        : ['image/jpeg', 'image/jpg', 'image/png', 'application/pdf'];
    const allowedExtensions = manualWalletConfig?.allowedExtensions?.length
        ? manualWalletConfig.allowedExtensions
        : ['.jpg', '.jpeg', '.png', '.pdf'];

    useEffect(() => {
        void fetchConfig();
    }, [fetchConfig]);

    useEffect(() => () => {
        if (previewUrl) {
            URL.revokeObjectURL(previewUrl);
        }
    }, [previewUrl]);

    const setLocalPreview = useCallback((file: File) => {
        if (previewUrl) {
            URL.revokeObjectURL(previewUrl);
        }

        setPreviewUrl(URL.createObjectURL(file));
        setPreviewKind(isPdfFile(file) ? 'pdf' : 'image');
    }, [previewUrl]);

    const clearLocalPreview = useCallback(() => {
        if (previewUrl) {
            URL.revokeObjectURL(previewUrl);
        }

        setPreviewUrl(null);
        setPreviewKind(null);
    }, [previewUrl]);

    const attemptUpload = useCallback(async (file: File, retryAuth = false) => {
        if (retryAuth) {
            useCheckoutStore.setState({
                manualWalletUploadToken: null,
            });
        }

        await initCheckoutSession();

        const uploadContext = useCheckoutStore.getState();
        if (!uploadContext.checkoutSessionId || !uploadContext.manualWalletUploadToken) {
            throw new Error('Checkout session upload context is missing.');
        }

        const formData = new FormData();
        formData.append('file', file);
        formData.append('checkoutSessionId', uploadContext.checkoutSessionId);

        return httpClient.post<{ fileKey: string }>(
            '/api/storefront/payments/manual-wallet/upload-proof',
            formData,
            {
                headers: {
                    'Content-Type': undefined,
                    'X-Manual-Wallet-Upload-Token': uploadContext.manualWalletUploadToken,
                },
            },
        );
    }, [initCheckoutSession]);

    const handleFileChange = useCallback(async (event: React.ChangeEvent<HTMLInputElement>) => {
        const file = event.target.files?.[0];
        if (!file) {
            return;
        }

        setUploadError(null);
        setClearMessage(null);

        // Clear input value immediately to allow re-selecting the same file
        event.target.value = '';

        if (file.size > maxFileBytes) {
            setUploadError(t('checkout.payment.manualWallet.fileTooLarge', {
                size: formatMaxFileSize(maxFileBytes),
            }));
            return;
        }

        // Validate file type: check MIME type if available, otherwise fall back to extension
        const isValidMimeType = file.type && allowedMimeTypes.includes(file.type);
        const fileExtension = file.name.substring(file.name.lastIndexOf('.')).toLowerCase();
        const isValidExtension = allowedExtensions.some(ext => ext.toLowerCase() === fileExtension);

        if (!isValidMimeType && !isValidExtension) {
            setUploadError(t('checkout.payment.manualWallet.unsupportedFileType', {
                formats: allowedExtensions.join(', '),
            }));
            return;
        }

        setUploading(true);
        setLocalPreview(file);

        try {
            const response = await attemptUpload(file, false);
            setManualWalletProofFileKey(response.data.fileKey);
        } catch (error: any) {
            let finalError = error;
            const isAuthError = error?.response?.status === 401 || error?.response?.status === 403;

            if (isAuthError) {
                try {
                    const retryResponse = await attemptUpload(file, true);
                    setManualWalletProofFileKey(retryResponse.data.fileKey);
                    return;
                } catch (retryError) {
                    finalError = retryError;
                }
            }

            const normalized = normalizeError(finalError as any);
            clearLocalPreview();
            setManualWalletProofFileKey(null);
            setUploadError(normalized.message || t('checkout.payment.manualWallet.uploadFailed'));
        } finally {
            setUploading(false);
            event.target.value = '';
        }
    }, [
        allowedExtensions,
        allowedMimeTypes,
        attemptUpload,
        clearLocalPreview,
        maxFileBytes,
        setLocalPreview,
        setManualWalletProofFileKey,
        t,
    ]);

    const clearProof = useCallback(async (retryAuth = false) => {
        if (retryAuth) {
            useCheckoutStore.setState({
                manualWalletUploadToken: null,
            });
        }

        await initCheckoutSession();

        const checkoutState = useCheckoutStore.getState();
        if (!checkoutState.checkoutSessionId || !checkoutState.manualWalletUploadToken) {
            throw new Error('Checkout session upload context is missing.');
        }

        return httpClient.delete<{
            cleared: boolean;
            warningCode?: string | null;
            warningMessage?: string | null;
        }>(
            `/api/storefront/payments/manual-wallet/clear-proof?checkoutSessionId=${checkoutState.checkoutSessionId}`,
            {
                headers: {
                    'X-Manual-Wallet-Upload-Token': checkoutState.manualWalletUploadToken,
                },
            },
        );
    }, [initCheckoutSession]);

    const handleClearProof = useCallback(async () => {
        setUploadError(null);
        setClearMessage(null);

        try {
            const response = await clearProof(false);
            clearLocalPreview();
            setManualWalletProofFileKey(null);
            if (response.data.warningMessage) {
                setClearMessage(response.data.warningMessage);
            }
        } catch (error: any) {
            let finalError = error;
            const isAuthError = error?.response?.status === 401 || error?.response?.status === 403;

            if (isAuthError) {
                try {
                    const retryResponse = await clearProof(true);
                    clearLocalPreview();
                    setManualWalletProofFileKey(null);
                    if (retryResponse.data.warningMessage) {
                        setClearMessage(retryResponse.data.warningMessage);
                    }
                    return;
                } catch (retryError) {
                    finalError = retryError;
                }
            }

            const normalized = normalizeError(finalError as any);
            setUploadError(normalized.message || t('checkout.payment.manualWallet.removalFailed'));
        }
    }, [clearLocalPreview, clearProof, setManualWalletProofFileKey, t]);

    const acceptValue = allowedMimeTypes.join(', ');
    const fileRulesLabel = t('checkout.payment.manualWallet.fileRules', {
        formats: allowedExtensions.join(', '),
        size: formatMaxFileSize(maxFileBytes),
    });

    return (
        <div className="mt-4 space-y-6 rounded-xl border border-muted/20 bg-muted/5 p-4">
            {manualWalletConfigError && (
                <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-3 text-sm text-red-700" role="alert">
                    {manualWalletConfigError}
                </div>
            )}

            <div>
                <label className="mb-2 block text-sm font-medium text-text">
                    {t('checkout.payment.manualWallet.selectWallet')}
                </label>
                {walletTypes.length === 0 ? (
                    <div className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-3 text-sm text-amber-800" role="alert">
                        {t('checkout.manualWalletUnavailable') || 'Manual wallet options are unavailable. Please choose another payment method or retry later.'}
                    </div>
                ) : null}
                <div className="flex gap-2">
                    {walletTypes.map((wallet) => (
                        <button
                            key={wallet.code}
                            type="button"
                            onClick={() => {
                                setManualWalletType(wallet.code);
                                setUploadError(null);
                                setClearMessage(null);
                            }}
                            className={`
                                flex flex-1 flex-col items-center gap-2 rounded-lg border p-3 text-sm font-medium transition-all
                                ${manualWalletType === wallet.code
                                    ? 'border-primary bg-primary/10 text-primary'
                                    : 'border-muted/30 text-muted hover:bg-muted/5'
                                }
                                ${showAllErrors && validationErrors.walletType ? 'border-red-300' : ''}
                            `}
                        >
                            <span className="flex h-9 w-9 items-center justify-center rounded-full bg-background text-xs font-bold">
                                {wallet.icon || wallet.code.slice(0, 2).toUpperCase()}
                            </span>
                            <span>{isAr ? (wallet.nameAr || wallet.name || wallet.code) : (wallet.name || wallet.nameAr || wallet.code)}</span>
                        </button>
                    ))}
                </div>
                {showAllErrors && validationErrors.walletType && (
                    <p className="mt-2 text-xs text-red-600">{t(`checkout.payment.manualWalletErrors.${validationErrors.walletType.code}`)}</p>
                )}
            </div>

            <div className="flex items-center justify-between rounded-lg border border-border bg-surface p-4">
                <div>
                    <p className="mb-1 text-xs text-muted">
                        {t('checkout.payment.manualWallet.transferExactAmount')}
                    </p>
                    <p className="font-mono text-xl font-bold tracking-wider text-primary">
                        {merchantWalletNumber || '---'}
                    </p>
                </div>
                <button
                    type="button"
                    onClick={() => merchantWalletNumber && navigator.clipboard.writeText(merchantWalletNumber)}
                    disabled={!merchantWalletNumber}
                    className="rounded p-2 text-primary hover:bg-primary/5 disabled:cursor-not-allowed disabled:text-muted"
                    title={t('checkout.payment.manualWallet.copy')}
                >
                    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z" />
                    </svg>
                </button>
            </div>

            <div>
                <label className="mb-1 block text-sm font-medium text-text">
                    {t('checkout.payment.manualWallet.senderNumber')}
                </label>
                <input
                    type="tel"
                    value={manualWalletPhoneNumber}
                    onChange={(event) => setManualWalletPhoneNumber(event.target.value)}
                    placeholder="01xxxxxxxxx"
                    className={`w-full rounded-lg bg-background text-sm focus:border-primary focus:ring-1 focus:ring-primary ${
                        showAllErrors && validationErrors.walletPhoneNumber
                            ? 'border-red-300'
                            : 'border-muted/30'
                    }`}
                    dir="ltr"
                />
                <p className="mt-1 text-xs text-muted">
                    {t('checkout.payment.manualWallet.senderNumberHelp')}
                </p>
                {showAllErrors && validationErrors.walletPhoneNumber && (
                    <p className="mt-2 text-xs text-red-600">{t(`checkout.payment.manualWalletErrors.${validationErrors.walletPhoneNumber.code}`)}</p>
                )}
            </div>

            <div>
                <label className="mb-2 block text-sm font-medium text-text">
                    {t('checkout.payment.manualWallet.transferProof')}
                </label>

                {!proofFileKey ? (
                    <div className={`rounded-lg border-2 border-dashed bg-surface p-6 text-center transition-colors hover:border-primary/50 ${
                        showAllErrors && validationErrors.proofFileKey
                            ? 'border-red-300'
                            : 'border-muted/30'
                    }`}>
                        <input
                            id="proof-upload"
                            type="file"
                            accept={acceptValue}
                            onChange={handleFileChange}
                            className="hidden"
                            disabled={uploading}
                        />
                        <label htmlFor="proof-upload" className="cursor-pointer">
                            {uploading ? (
                                <div className="flex flex-col items-center">
                                    <svg className="mb-2 h-8 w-8 animate-spin text-primary" fill="none" viewBox="0 0 24 24">
                                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                                    </svg>
                                    <span className="text-sm text-muted">{t('checkout.payment.manualWallet.uploadingProof')}</span>
                                </div>
                            ) : (
                                <div className="flex flex-col items-center">
                                    <svg className="mb-2 h-8 w-8 text-muted" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
                                    </svg>
                                    <span className="text-sm font-medium text-primary">
                                        {t('checkout.payment.manualWallet.uploadProof')}
                                    </span>
                                    <span className="mt-1 text-xs text-muted">{fileRulesLabel}</span>
                                </div>
                            )}
                        </label>
                    </div>
                ) : (
                    <div className="group relative overflow-hidden rounded-lg border border-border">
                        {previewUrl && previewKind === 'image' ? (
                            <img
                                src={previewUrl}
                                alt={t('checkout.payment.manualWallet.proofAlt')}
                                className="h-32 w-full object-cover"
                            />
                        ) : previewUrl && previewKind === 'pdf' ? (
                            <div className="flex h-32 w-full flex-col items-center justify-center bg-blue-50 text-blue-700">
                                <svg className="mb-2 h-10 w-10" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M7 7V3h8l4 4v14a2 2 0 01-2 2H7a2 2 0 01-2-2V5a2 2 0 012-2z" />
                                </svg>
                                <span className="text-sm font-medium">{t('checkout.payment.manualWallet.pdfSelected')}</span>
                            </div>
                        ) : (
                            <div className="flex h-32 w-full flex-col items-center justify-center bg-green-50">
                                <svg className="mb-2 h-10 w-10 text-green-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                                </svg>
                                <span className="text-sm font-medium text-green-700">
                                    {t('checkout.payment.manualWallet.proofUploaded')}
                                </span>
                            </div>
                        )}
                        <div className="absolute inset-0 flex items-center justify-center bg-black/40 opacity-0 transition-opacity group-hover:opacity-100">
                            <button
                                type="button"
                                onClick={() => void handleClearProof()}
                                className="rounded-full bg-red-500 p-2 text-white hover:bg-red-600"
                            >
                                <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                                </svg>
                            </button>
                        </div>
                        <div className="absolute bottom-2 right-2 flex items-center gap-1 rounded-full bg-green-500 px-2 py-1 text-xs text-white">
                            <svg className="h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                            </svg>
                            {t('checkout.payment.manualWallet.uploaded')}
                        </div>
                    </div>
                )}

                <p className="mt-1 text-xs text-muted">
                    {t('checkout.payment.manualWallet.acceptedFormats', {
                        formats: allowedExtensions.join(', '),
                    })}
                </p>
                {showAllErrors && validationErrors.proofFileKey && (
                    <p className="mt-2 text-xs text-red-600">{t(`checkout.payment.manualWalletErrors.${validationErrors.proofFileKey.code}`)}</p>
                )}
                {uploadError && (
                    <p className="mt-2 text-xs text-red-600">{uploadError}</p>
                )}
                {clearMessage && (
                    <p className="mt-2 text-xs text-amber-700">{clearMessage}</p>
                )}
            </div>
        </div>
    );
};

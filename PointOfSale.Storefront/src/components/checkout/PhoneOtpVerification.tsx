import React, { useState, useEffect, useRef, useCallback } from 'react';
import { httpClient } from '@/services/api/httpClient';
import type { PhoneVerificationStatus, PhoneOtpResult } from '@/types/checkout';

interface PhoneOtpVerificationProps {
    phone: string;
    onVerified: () => void;
    onSkip?: () => void;
    disabled?: boolean;
}

const OTP_LENGTH = 6;

function isAbortError(error: unknown): boolean {
    if (error instanceof DOMException && error.name === 'AbortError') {
        return true;
    }

    if (typeof error === 'object' && error !== null) {
        const candidate = error as { code?: string; name?: string };
        return candidate.code === 'ERR_CANCELED' || candidate.name === 'CanceledError';
    }

    return false;
}

export const PhoneOtpVerification: React.FC<PhoneOtpVerificationProps> = ({
    phone,
    onVerified,
    onSkip: _onSkip,
    disabled = false,
}) => {
    const [status, setStatus] = useState<PhoneVerificationStatus | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [statusError, setStatusError] = useState<string | null>(null);
    const [isRequestingOtp, setIsRequestingOtp] = useState(false);
    const [isVerifying, setIsVerifying] = useState(false);
    const [otpSent, setOtpSent] = useState(false);
    const [otpCode, setOtpCode] = useState('');
    const [error, setError] = useState<string | null>(null);
    const [successMessage, setSuccessMessage] = useState<string | null>(null);
    const [countdown, setCountdown] = useState(0);
    const [expiresIn, setExpiresIn] = useState(0);

    const otpInputRef = useRef<HTMLInputElement>(null);
    const countdownRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const expiryRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const autoVerifyRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const statusAbortRef = useRef<AbortController | null>(null);
    const requestAbortRef = useRef<AbortController | null>(null);
    const verifyAbortRef = useRef<AbortController | null>(null);

    const clearAsyncWork = useCallback(() => {
        statusAbortRef.current?.abort();
        requestAbortRef.current?.abort();
        verifyAbortRef.current?.abort();

        if (autoVerifyRef.current) {
            clearTimeout(autoVerifyRef.current);
            autoVerifyRef.current = null;
        }

        // Reset loading flags to prevent UI from being stuck in disabled state
        setIsRequestingOtp(false);
        setIsVerifying(false);
    }, []);

    const loadStatus = useCallback(async () => {
        if (!phone) {
            setStatus(null);
            setStatusError(null);
            setIsLoading(false);
            return;
        }

        statusAbortRef.current?.abort();
        const controller = new AbortController();
        statusAbortRef.current = controller;

        try {
            setIsLoading(true);
            setStatusError(null);
            setError(null);

            const response = await httpClient.get<PhoneVerificationStatus>(
                `/api/storefront/checkout/phone-verification-status?phone=${encodeURIComponent(phone)}`,
                { signal: controller.signal }
            );

            if (controller.signal.aborted) {
                return;
            }

            setStatus(response.data);

            if (!response.data.isRequired || response.data.isVerified) {
                onVerified();
            }
        } catch (err) {
            if (isAbortError(err)) {
                return;
            }

            console.error('[PhoneOtp] Status check failed:', err);
            setStatus(null);
            setStatusError("We couldn't confirm your phone verification status. Please retry.");
        } finally {
            if (statusAbortRef.current === controller) {
                statusAbortRef.current = null;
            }

            if (!controller.signal.aborted) {
                setIsLoading(false);
            }
        }
    }, [phone, onVerified]);

    useEffect(() => {
        setStatus(null);
        setStatusError(null);
        setOtpSent(false);
        setOtpCode('');
        setError(null);
        setSuccessMessage(null);
        setCountdown(0);
        setExpiresIn(0);
        clearAsyncWork();

        void loadStatus();

        return () => {
            clearAsyncWork();
        };
    }, [phone, clearAsyncWork, loadStatus]);

    useEffect(() => {
        if (countdown > 0) {
            countdownRef.current = setTimeout(() => {
                setCountdown((current) => current - 1);
            }, 1000);
        }

        return () => {
            if (countdownRef.current) {
                clearTimeout(countdownRef.current);
            }
        };
    }, [countdown]);

    useEffect(() => {
        if (expiresIn > 0) {
            expiryRef.current = setTimeout(() => {
                setExpiresIn((current) => current - 1);
            }, 1000);
        }

        return () => {
            if (expiryRef.current) {
                clearTimeout(expiryRef.current);
            }
        };
    }, [expiresIn]);

    const handleRequestOtp = useCallback(async () => {
        requestAbortRef.current?.abort();
        const controller = new AbortController();
        requestAbortRef.current = controller;

        try {
            setIsRequestingOtp(true);
            setError(null);
            setSuccessMessage(null);
            setStatusError(null);

            const response = await httpClient.post<PhoneOtpResult>(
                '/api/storefront/checkout/request-phone-otp',
                { phone },
                { signal: controller.signal }
            );

            if (controller.signal.aborted) {
                return;
            }

            if (!response.data.success) {
                setError(response.data.message || 'Failed to send OTP. Please try again.');
                return;
            }

            setOtpSent(true);
            setOtpCode('');
            setSuccessMessage(response.data.message || 'Verification code sent to your WhatsApp.');
            setCountdown(response.data.retryAfterSeconds ?? 60);
            setExpiresIn(response.data.expiresInSeconds || 0);
            setTimeout(() => {
                otpInputRef.current?.focus();
            }, 100);
        } catch (err: any) {
            if (isAbortError(err)) {
                return;
            }

            console.error('[PhoneOtp] Request failed:', err);
            setError(err.response?.data?.message || 'Failed to send OTP. Please try again.');
        } finally {
            if (requestAbortRef.current === controller) {
                requestAbortRef.current = null;
            }

            if (!controller.signal.aborted) {
                setIsRequestingOtp(false);
            }
        }
    }, [phone]);

    const handleVerifyOtp = useCallback(async (codeOverride?: string) => {
        const codeToVerify = codeOverride ?? otpCode;

        if (codeToVerify.length !== OTP_LENGTH) {
            setError('Please enter the complete 6-digit code');
            return;
        }

        verifyAbortRef.current?.abort();
        const controller = new AbortController();
        verifyAbortRef.current = controller;

        try {
            setIsVerifying(true);
            setError(null);
            setSuccessMessage(null);

            const response = await httpClient.post<PhoneOtpResult>(
                '/api/storefront/checkout/verify-phone-otp',
                { phone, otpCode: codeToVerify },
                { signal: controller.signal }
            );

            if (controller.signal.aborted) {
                return;
            }

            if (!response.data.success) {
                setError(response.data.message || 'Verification failed. Please try again.');
                return;
            }

            setStatus((current) => current ? { ...current, isVerified: true } : current);
            onVerified();
        } catch (err: any) {
            if (isAbortError(err)) {
                return;
            }

            console.error('[PhoneOtp] Verify failed:', err);
            setError(err.response?.data?.message || 'Verification failed. Please try again.');
        } finally {
            if (verifyAbortRef.current === controller) {
                verifyAbortRef.current = null;
            }

            if (!controller.signal.aborted) {
                setIsVerifying(false);
            }
        }
    }, [otpCode, phone, onVerified]);

    const handleOtpChange = (event: React.ChangeEvent<HTMLInputElement>) => {
        const value = event.target.value.replace(/\D/g, '').slice(0, OTP_LENGTH);

        setOtpCode(value);
        setError(null);

        if (autoVerifyRef.current) {
            clearTimeout(autoVerifyRef.current);
            autoVerifyRef.current = null;
        }

        if (value.length === OTP_LENGTH) {
            autoVerifyRef.current = setTimeout(() => {
                void handleVerifyOtp(value);
            }, 300);
        }
    };

    const formatTime = (seconds: number) => {
        const minutes = Math.floor(seconds / 60);
        const remainingSeconds = seconds % 60;
        return `${minutes}:${remainingSeconds.toString().padStart(2, '0')}`;
    };

    if (isLoading) {
        return (
            <div className="p-6 bg-muted/5 rounded-xl border border-muted/10 animate-pulse">
                <div className="flex items-center gap-4">
                    <div className="w-12 h-12 rounded-full bg-muted/20" />
                    <div className="flex-1 space-y-2">
                        <div className="h-4 bg-muted/20 rounded w-48" />
                        <div className="h-3 bg-muted/10 rounded w-32" />
                    </div>
                </div>
            </div>
        );
    }

    if (statusError) {
        return (
            <div className="p-6 bg-red-50 rounded-xl border border-red-200">
                <div className="flex items-start gap-3">
                    <svg className="w-5 h-5 text-red-500 mt-0.5 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                    <div className="flex-1">
                        <h3 className="font-semibold text-red-900">Phone verification check failed</h3>
                        <p className="mt-1 text-sm text-red-700">{statusError}</p>
                        <button
                            onClick={() => void loadStatus()}
                            disabled={disabled || isLoading}
                            className="mt-4 inline-flex items-center gap-2 rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-red-700 disabled:cursor-not-allowed disabled:opacity-60"
                        >
                            Retry verification check
                        </button>
                    </div>
                </div>
            </div>
        );
    }

    if (!status?.isRequired || status.isVerified) {
        return null;
    }

    return (
        <div className="p-6 bg-gradient-to-br from-green-50/50 to-emerald-50/50 rounded-xl border border-green-100">
            <div className="flex items-start gap-4 mb-4">
                <div className="w-12 h-12 rounded-full bg-green-500 flex items-center justify-center flex-shrink-0">
                    <svg className="w-6 h-6 text-white" viewBox="0 0 24 24" fill="currentColor">
                        <path d="M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347m-5.421 7.403h-.004a9.87 9.87 0 01-5.031-1.378l-.361-.214-3.741.982.998-3.648-.235-.374a9.86 9.86 0 01-1.51-5.26c.001-5.45 4.436-9.884 9.888-9.884 2.64 0 5.122 1.03 6.988 2.898a9.825 9.825 0 012.893 6.994c-.003 5.45-4.437 9.884-9.885 9.884m8.413-18.297A11.815 11.815 0 0012.05 0C5.495 0 .16 5.335.157 11.892c0 2.096.547 4.142 1.588 5.945L.057 24l6.305-1.654a11.882 11.882 0 005.683 1.448h.005c6.554 0 11.89-5.335 11.893-11.893a11.821 11.821 0 00-3.48-8.413z" />
                    </svg>
                </div>

                <div className="flex-1">
                    <h3 className="font-semibold text-text text-lg">
                        Verify Your Phone via WhatsApp
                    </h3>
                    <p className="text-sm text-muted mt-1">
                        We'll send a verification code to{' '}
                        <strong className="text-text font-mono">
                            {status.formattedPhone || phone}
                        </strong>
                        {status.formattedPhone && status.formattedPhone !== phone && (
                            <span className="ml-1 text-xs text-green-600">(formatted)</span>
                        )}
                    </p>
                </div>
            </div>

            {error && (
                <div className="mb-4 p-3 bg-red-50 border border-red-100 rounded-lg flex items-center gap-2 text-sm text-red-700">
                    <svg className="w-4 h-4 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                    <span>{error}</span>
                </div>
            )}

            {!otpSent && (
                <div className="space-y-4">
                    <p className="text-sm text-muted">
                        Click below to receive a 6-digit verification code on WhatsApp.
                    </p>
                    <button
                        onClick={() => void handleRequestOtp()}
                        disabled={disabled || isRequestingOtp}
                        className="
                            w-full py-3 px-4 rounded-xl font-medium
                            bg-green-500 text-white
                            hover:bg-green-600
                            disabled:opacity-50 disabled:cursor-not-allowed
                            transition-colors
                            flex items-center justify-center gap-2
                        "
                    >
                        {isRequestingOtp ? (
                            <>
                                <svg className="w-5 h-5 animate-spin" fill="none" viewBox="0 0 24 24">
                                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                                </svg>
                                Sending...
                            </>
                        ) : (
                            <>
                                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 19l9 2-9-18-9 18 9-2zm0 0v-8" />
                                </svg>
                                Send Verification Code
                            </>
                        )}
                    </button>
                </div>
            )}

            {otpSent && (
                <div className="space-y-4">
                    <div className="p-3 bg-green-100/50 border border-green-200 rounded-lg flex items-center gap-2 text-sm text-green-700">
                        <svg className="w-4 h-4 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                        </svg>
                        <span>{successMessage || 'Verification code sent. Check your WhatsApp messages.'}</span>
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-text mb-2">
                            Enter 6-digit code
                        </label>
                        <input
                            ref={otpInputRef}
                            type="text"
                            inputMode="numeric"
                            autoComplete="one-time-code"
                            value={otpCode}
                            onChange={handleOtpChange}
                            disabled={disabled || isVerifying}
                            placeholder="o o o o o o"
                            className="
                                w-full px-4 py-3 text-center text-2xl tracking-[0.5em] font-mono
                                border border-muted/20 rounded-xl
                                bg-white
                                focus:outline-none focus:ring-2 focus:ring-green-500 focus:border-transparent
                                disabled:opacity-50 disabled:cursor-not-allowed
                                placeholder:tracking-[0.3em] placeholder:text-muted/30
                            "
                            maxLength={OTP_LENGTH}
                        />
                    </div>

                    <div className="flex items-center justify-between text-sm">
                        {expiresIn > 0 && (
                            <span className="text-muted">
                                Code expires in <strong className="text-text">{formatTime(expiresIn)}</strong>
                            </span>
                        )}

                        {countdown > 0 ? (
                            <span className="text-muted">
                                Resend in {countdown}s
                            </span>
                        ) : (
                            <button
                                onClick={() => void handleRequestOtp()}
                                disabled={disabled || isRequestingOtp}
                                className="text-green-600 hover:text-green-700 font-medium disabled:opacity-50"
                            >
                                Resend Code
                            </button>
                        )}
                    </div>

                    <button
                        onClick={() => void handleVerifyOtp()}
                        disabled={disabled || isVerifying || otpCode.length !== OTP_LENGTH}
                        className="
                            w-full py-3 px-4 rounded-xl font-medium
                            bg-green-500 text-white
                            hover:bg-green-600
                            disabled:opacity-50 disabled:cursor-not-allowed
                            transition-colors
                            flex items-center justify-center gap-2
                        "
                    >
                        {isVerifying ? (
                            <>
                                <svg className="w-5 h-5 animate-spin" fill="none" viewBox="0 0 24 24">
                                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                                </svg>
                                Verifying...
                            </>
                        ) : (
                            <>
                                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                                </svg>
                                Verify and Continue
                            </>
                        )}
                    </button>
                </div>
            )}

            <p className="mt-4 text-xs text-muted text-center">
                <svg className="inline w-3 h-3 mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
                </svg>
                Your phone number is used only for order verification
            </p>
        </div>
    );
};

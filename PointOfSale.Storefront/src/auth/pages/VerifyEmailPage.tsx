/**
 * Verify Email Page
 * Phase F1: Storefront Auth UI
 * 
 * Handles email verification from verification links.
 * Calls the Storefront auth API to verify the token.
 */

import React, { useEffect, useState, useCallback } from 'react';
import { useSearchParams, useNavigate, Link } from 'react-router-dom';
import { Layout } from '@/components/layout';
import { useCategories } from '@/hooks';
import { useTranslation } from '@/i18n';
import { httpClient } from '@/services/api/httpClient';
import { useAuth } from '@/auth/hooks';

type VerificationStatus = 'verifying' | 'success' | 'error' | 'already_verified';

interface VerificationResult {
    success: boolean;
    error?: string;
    message?: string;
}

export const VerifyEmailPage: React.FC = () => {
    const { t, direction } = useTranslation();
    const navigate = useNavigate();
    const { categories } = useCategories();
    const { checkAuth } = useAuth();
    const [searchParams] = useSearchParams();

    const [status, setStatus] = useState<VerificationStatus>('verifying');
    const [message, setMessage] = useState<string>('');

    const token = searchParams.get('token');
    const tenantId = searchParams.get('tenant');

    const verifyEmail = useCallback(async () => {
        if (!token) {
            setStatus('error');
            setMessage(t('auth.verifyEmail.noToken', {
                defaultValue: 'No verification token provided.'
            }));
            return;
        }

        try {
            // Call the Storefront auth endpoint (not POS)
            const response = await httpClient.post<VerificationResult>(
                '/api/storefront/auth/verify-email',
                { token },
                {
                    params: tenantId ? { tenant: tenantId } : undefined
                }
            );

            const result = response.data;

            if (result.success) {
                if (result.error === 'ALREADY_VERIFIED') {
                    setStatus('already_verified');
                    setMessage(result.message || t('auth.verifyEmail.alreadyVerified', {
                        defaultValue: 'Your email is already verified!'
                    }));
                } else {
                    setStatus('success');
                    setMessage(result.message || t('auth.verifyEmail.success', {
                        defaultValue: 'Your email has been verified successfully!'
                    }));
                    // Refresh auth state to get updated emailVerified status
                    await checkAuth();
                }
            } else {
                setStatus('error');
                setMessage(result.message || t('auth.verifyEmail.invalidLink', {
                    defaultValue: 'This verification link is invalid.'
                }));
            }
        } catch (error: unknown) {
            setStatus('error');

            // Handle specific error responses
            const axiosError = error as { response?: { data?: { error?: string; message?: string } } };
            const errorData = axiosError?.response?.data;

            if (errorData?.error === 'TOKEN_EXPIRED') {
                setMessage(t('auth.verifyEmail.expired', {
                    defaultValue: 'This verification link has expired. Please request a new one.'
                }));
            } else if (errorData?.error === 'TOKEN_USED') {
                setMessage(t('auth.verifyEmail.alreadyUsed', {
                    defaultValue: 'This verification link has already been used.'
                }));
            } else {
                setMessage(errorData?.message || t('auth.verifyEmail.invalidLink', {
                    defaultValue: 'This verification link is invalid.'
                }));
            }
        }
    }, [token, tenantId, t, checkAuth]);

    useEffect(() => {
        verifyEmail();
    }, [verifyEmail]);

    const getStatusIcon = () => {
        switch (status) {
            case 'verifying':
                return (
                    <div className="w-20 h-20 rounded-full bg-primary/10 flex items-center justify-center mx-auto mb-6 animate-pulse">
                        <svg className="w-10 h-10 text-primary animate-spin" fill="none" viewBox="0 0 24 24">
                            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                        </svg>
                    </div>
                );
            case 'success':
            case 'already_verified':
                return (
                    <div className="w-20 h-20 rounded-full bg-green-100 flex items-center justify-center mx-auto mb-6 animate-bounce-once">
                        <svg className="w-10 h-10 text-green-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                        </svg>
                    </div>
                );
            case 'error':
                return (
                    <div className="w-20 h-20 rounded-full bg-red-100 flex items-center justify-center mx-auto mb-6">
                        <svg className="w-10 h-10 text-red-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                        </svg>
                    </div>
                );
        }
    };

    const getTitle = () => {
        switch (status) {
            case 'verifying':
                return t('auth.verifyEmail.verifying', { defaultValue: 'Verifying your email...' });
            case 'success':
                return t('auth.verifyEmail.verified', { defaultValue: 'Email Verified!' });
            case 'already_verified':
                return t('auth.verifyEmail.alreadyVerifiedTitle', { defaultValue: 'Already Verified' });
            case 'error':
                return t('auth.verifyEmail.invalidLinkTitle', { defaultValue: 'Invalid Link' });
        }
    };

    return (
        <Layout categories={categories}>
            <div dir={direction} className="min-h-[60vh] bg-background flex items-center justify-center py-12 px-4">
                <div className="max-w-md w-full">
                    <div className="bg-secondary/5 border border-muted/10 rounded-xl p-8 shadow-sm text-center">
                        {/* Status Icon */}
                        {getStatusIcon()}

                        {/* Title */}
                        <h1 className={`text-2xl font-bold mb-3 ${status === 'error' ? 'text-red-600' :
                            status === 'success' || status === 'already_verified' ? 'text-green-600' :
                                'text-text'
                            }`}>
                            {getTitle()}
                        </h1>

                        {/* Message */}
                        <p className="text-muted mb-8">
                            {status === 'verifying'
                                ? t('auth.verifyEmail.pleaseWait', { defaultValue: 'Please wait while we verify your email address...' })
                                : message
                            }
                        </p>

                        {/* Actions */}
                        {status !== 'verifying' && (
                            <div className="space-y-3">
                                {(status === 'success' || status === 'already_verified') && (
                                    <>
                                        <button
                                            onClick={() => navigate('/account')}
                                            className="w-full px-6 py-3 bg-primary text-white font-medium rounded-lg hover:bg-primary/90 transition-colors"
                                        >
                                            {t('auth.verifyEmail.goToAccount', { defaultValue: 'Go to My Account' })}
                                        </button>
                                        <Link
                                            to="/"
                                            className="block w-full px-6 py-3 border border-muted/20 text-text font-medium rounded-lg hover:bg-muted/5 transition-colors"
                                        >
                                            {t('auth.verifyEmail.continueShopping', { defaultValue: 'Continue Shopping' })}
                                        </Link>
                                    </>
                                )}

                                {status === 'error' && (
                                    <>
                                        <Link
                                            to="/account"
                                            className="block w-full px-6 py-3 bg-primary text-white font-medium rounded-lg hover:bg-primary/90 transition-colors"
                                        >
                                            {t('auth.verifyEmail.requestNew', { defaultValue: 'Request New Verification' })}
                                        </Link>
                                        <Link
                                            to="/"
                                            className="block w-full px-6 py-3 border border-muted/20 text-text font-medium rounded-lg hover:bg-muted/5 transition-colors"
                                        >
                                            {t('common.backToStore', { defaultValue: 'Back to Store' })}
                                        </Link>
                                    </>
                                )}
                            </div>
                        )}
                    </div>
                </div>
            </div>
        </Layout>
    );
};

export default VerifyEmailPage;

/**
 * Security Section Component
 * Phase F2: Storefront Account Area
 * 
 * Security settings section with:
 * - Password change (local accounts only)
 * - Session management
 * - Social login indicator
 */

import React, { useState, useEffect } from 'react';
import { useTranslation } from '@/i18n';
import { useAuth } from '@/auth/hooks';
import { useAccount } from '@/auth/hooks/useAccount';
import { ChangePasswordModal } from './ChangePasswordModal';

export const SecuritySection: React.FC = () => {
    const { t, direction } = useTranslation();
    const { customer, emailVerified } = useAuth();
    const {
        hasPassword,
        provider,
        checkHasPassword,
        isLoading
    } = useAccount();

    const [showPasswordModal, setShowPasswordModal] = useState(false);

    // Check if user has password on mount
    useEffect(() => {
        checkHasPassword();
    }, [checkHasPassword]);

    // Determine if password change is available
    const canChangePassword = hasPassword === true && provider === 'local';
    const isSocialOnly = hasPassword === false && provider !== 'local';

    return (
        <div dir={direction} className="bg-background border border-muted/20 rounded-xl p-6">
            <div className="flex items-center gap-4 mb-6">
                <div className="w-12 h-12 bg-green-100 rounded-full flex items-center justify-center">
                    <svg className="w-6 h-6 text-green-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                            d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
                    </svg>
                </div>
                <div>
                    <h2 className="text-lg font-semibold text-text">
                        {t('account.security.title', { defaultValue: 'Security' })}
                    </h2>
                    <p className="text-sm text-muted">
                        {t('account.security.description', { defaultValue: 'Password & sessions' })}
                    </p>
                </div>
            </div>

            <div className="space-y-4">
                {/* Login Method */}
                <div className="flex justify-between items-center py-3 border-b border-muted/10">
                    <span className="text-muted">
                        {t('account.security.provider', { defaultValue: 'Login method' })}
                    </span>
                    <span className="text-text font-medium capitalize">
                        {provider || customer?.provider || 'Email'}
                    </span>
                </div>

                {/* Email Verification Status */}
                <div className="flex justify-between items-center py-3 border-b border-muted/10">
                    <span className="text-muted">
                        {t('account.security.emailVerified', { defaultValue: 'Email verified' })}
                    </span>
                    {emailVerified ? (
                        <span className="flex items-center text-green-600">
                            <svg className="w-4 h-4 mr-1" fill="currentColor" viewBox="0 0 20 20">
                                <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
                            </svg>
                            {t('common.yes', { defaultValue: 'Yes' })}
                        </span>
                    ) : (
                        <span className="flex items-center text-amber-600">
                            <svg className="w-4 h-4 mr-1" fill="currentColor" viewBox="0 0 20 20">
                                <path fillRule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
                            </svg>
                            {t('common.no', { defaultValue: 'No' })}
                        </span>
                    )}
                </div>

                {/* Password Section */}
                <div className="py-3">
                    <div className="flex justify-between items-center">
                        <span className="text-muted">
                            {t('account.security.password', { defaultValue: 'Password' })}
                        </span>

                        {isLoading ? (
                            <span className="text-muted text-sm">
                                {t('common.loading', { defaultValue: 'Loading...' })}
                            </span>
                        ) : canChangePassword ? (
                            <button
                                onClick={() => setShowPasswordModal(true)}
                                className="px-4 py-2 text-sm text-primary hover:text-primary/80 transition-colors"
                            >
                                {t('account.security.changePassword', { defaultValue: 'Change Password' })}
                            </button>
                        ) : isSocialOnly ? (
                            <span className="text-sm text-muted italic">
                                {t('account.security.socialLoginOnly', {
                                    defaultValue: 'This account uses {provider} login.',
                                    provider: (provider || 'social').charAt(0).toUpperCase() + (provider || 'social').slice(1)
                                })}
                            </span>
                        ) : (
                            <span className="text-sm text-muted">
                                {t('common.notAvailable', { defaultValue: 'Not available' })}
                            </span>
                        )}
                    </div>

                    {/* Social login info message */}
                    {isSocialOnly && (
                        <div className="mt-3 bg-blue-50 border border-blue-200 rounded-lg p-3">
                            <p className="text-sm text-blue-800">
                                {t('account.security.socialLoginInfo', {
                                    defaultValue: 'Your account uses {provider} for authentication. Password management is handled by your social login provider.',
                                    provider: (provider || 'social').charAt(0).toUpperCase() + (provider || 'social').slice(1)
                                })}
                            </p>
                        </div>
                    )}
                </div>
            </div>

            {/* Change Password Modal */}
            <ChangePasswordModal
                isOpen={showPasswordModal}
                onClose={() => setShowPasswordModal(false)}
            />
        </div>
    );
};

export default SecuritySection;

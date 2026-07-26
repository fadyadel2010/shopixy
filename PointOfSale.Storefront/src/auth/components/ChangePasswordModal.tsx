/**
 * Change Password Modal Component
 * Phase F2: Storefront Account Area - Password Change with Confirmation
 * 
 * SECURITY:
 * - Shows warning that password change logs out all devices
 * - Requires confirmation before API call
 * - Forces logout on success
 */

import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from '@/i18n';
import { useAuthStore } from '@/auth/store/authStore';
import { useAccount } from '@/auth/hooks/useAccount';

interface ChangePasswordModalProps {
    isOpen: boolean;
    onClose: () => void;
}

export const ChangePasswordModal: React.FC<ChangePasswordModalProps> = ({
    isOpen,
    onClose,
}) => {
    const { t, direction } = useTranslation();
    const navigate = useNavigate();
    const { logout } = useAuthStore();
    const { changePassword, isLoading, error, clearError } = useAccount();

    // Form state
    const [currentPassword, setCurrentPassword] = useState('');
    const [newPassword, setNewPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');
    const [showConfirmation, setShowConfirmation] = useState(false);
    const [validationError, setValidationError] = useState<string | null>(null);

    const resetForm = () => {
        setCurrentPassword('');
        setNewPassword('');
        setConfirmPassword('');
        setShowConfirmation(false);
        setValidationError(null);
        clearError();
    };

    const handleClose = () => {
        resetForm();
        onClose();
    };

    const validateForm = (): boolean => {
        setValidationError(null);

        if (!currentPassword) {
            setValidationError(t('account.security.currentPassword', { defaultValue: 'Current password is required' }));
            return false;
        }

        if (!newPassword || newPassword.length < 8) {
            setValidationError(t('account.security.passwordRequirements', {
                defaultValue: 'Password must be at least 8 characters'
            }));
            return false;
        }

        if (newPassword !== confirmPassword) {
            setValidationError(t('auth.errors.passwordMismatch', {
                defaultValue: 'Passwords do not match'
            }));
            return false;
        }

        return true;
    };

    const handleSubmitRequest = () => {
        if (!validateForm()) return;
        // Show confirmation modal BEFORE making API call
        setShowConfirmation(true);
    };

    const handleConfirmAndSubmit = async () => {
        // CRITICAL: Only called after user confirms warning
        setShowConfirmation(false);

        const result = await changePassword({
            currentPassword,
            newPassword,
            confirmPassword,
        });

        if (result.success) {
            // CRITICAL: Force logout - password change invalidates all sessions
            await logout();
            navigate('/login', {
                state: {
                    message: t('account.security.passwordChanged', {
                        defaultValue: 'Password changed successfully. You have been logged out from all devices.'
                    })
                }
            });
        }
    };

    if (!isOpen) return null;

    return (
        <div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
            dir={direction}
        >
            {/* Confirmation Warning Modal */}
            {showConfirmation && (
                <div className="bg-background rounded-xl shadow-2xl max-w-md w-full mx-4 p-6">
                    <div className="flex items-center gap-3 mb-4">
                        <div className="w-12 h-12 bg-amber-100 rounded-full flex items-center justify-center">
                            <svg className="w-6 h-6 text-amber-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                    d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                            </svg>
                        </div>
                        <div>
                            <h3 className="text-lg font-semibold text-text">
                                {t('common.confirm', { defaultValue: 'Confirm Action' })}
                            </h3>
                        </div>
                    </div>

                    <p className="text-muted mb-6">
                        {t('account.security.passwordChangeWarning', {
                            defaultValue: 'Changing your password will log you out from all devices. You will need to log in again on all your devices.'
                        })}
                    </p>

                    <div className="flex gap-3 justify-end">
                        <button
                            onClick={() => setShowConfirmation(false)}
                            className="px-4 py-2 text-muted hover:text-text transition-colors"
                        >
                            {t('common.cancel', { defaultValue: 'Cancel' })}
                        </button>
                        <button
                            onClick={handleConfirmAndSubmit}
                            disabled={isLoading}
                            className="px-4 py-2 bg-primary text-white rounded-lg hover:bg-primary/90 transition-colors disabled:opacity-50"
                        >
                            {isLoading
                                ? t('common.loading', { defaultValue: 'Processing...' })
                                : t('account.security.changePassword', { defaultValue: 'Change Password' })
                            }
                        </button>
                    </div>
                </div>
            )}

            {/* Main Change Password Form */}
            {!showConfirmation && (
                <div className="bg-background rounded-xl shadow-2xl max-w-md w-full mx-4 p-6">
                    <div className="flex items-center justify-between mb-6">
                        <h2 className="text-xl font-semibold text-text">
                            {t('account.security.changePassword', { defaultValue: 'Change Password' })}
                        </h2>
                        <button
                            onClick={handleClose}
                            className="text-muted hover:text-text transition-colors"
                            aria-label={t('common.close', { defaultValue: 'Close' })}
                        >
                            <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    </div>

                    {/* Warning Banner */}
                    <div className="bg-amber-50 border border-amber-200 rounded-lg p-4 mb-6">
                        <div className="flex items-start gap-3">
                            <svg className="w-5 h-5 text-amber-600 mt-0.5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                    d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                            </svg>
                            <p className="text-sm text-amber-800">
                                {t('account.security.passwordChangeWarning', {
                                    defaultValue: 'Changing your password will log you out from all devices.'
                                })}
                            </p>
                        </div>
                    </div>

                    {/* Error Display */}
                    {(validationError || error) && (
                        <div className="bg-red-50 border border-red-200 rounded-lg p-4 mb-4">
                            <p className="text-sm text-red-600">{validationError || error}</p>
                        </div>
                    )}

                    <form onSubmit={(e) => { e.preventDefault(); handleSubmitRequest(); }}>
                        <div className="space-y-4">
                            <div>
                                <label className="block text-sm font-medium text-text mb-1">
                                    {t('account.security.currentPassword', { defaultValue: 'Current Password' })}
                                </label>
                                <input
                                    type="password"
                                    value={currentPassword}
                                    onChange={(e) => setCurrentPassword(e.target.value)}
                                    className="w-full px-4 py-2 border border-muted/30 rounded-lg focus:outline-none focus:border-primary"
                                    autoComplete="current-password"
                                />
                            </div>

                            <div>
                                <label className="block text-sm font-medium text-text mb-1">
                                    {t('account.security.newPassword', { defaultValue: 'New Password' })}
                                </label>
                                <input
                                    type="password"
                                    value={newPassword}
                                    onChange={(e) => setNewPassword(e.target.value)}
                                    className="w-full px-4 py-2 border border-muted/30 rounded-lg focus:outline-none focus:border-primary"
                                    autoComplete="new-password"
                                />
                                <p className="text-xs text-muted mt-1">
                                    {t('account.security.passwordRequirements', {
                                        defaultValue: 'At least 8 characters with uppercase, lowercase, and number'
                                    })}
                                </p>
                            </div>

                            <div>
                                <label className="block text-sm font-medium text-text mb-1">
                                    {t('account.security.confirmPassword', { defaultValue: 'Confirm Password' })}
                                </label>
                                <input
                                    type="password"
                                    value={confirmPassword}
                                    onChange={(e) => setConfirmPassword(e.target.value)}
                                    className="w-full px-4 py-2 border border-muted/30 rounded-lg focus:outline-none focus:border-primary"
                                    autoComplete="new-password"
                                />
                            </div>
                        </div>

                        <div className="flex gap-3 justify-end mt-6">
                            <button
                                type="button"
                                onClick={handleClose}
                                className="px-4 py-2 text-muted hover:text-text transition-colors"
                            >
                                {t('common.cancel', { defaultValue: 'Cancel' })}
                            </button>
                            <button
                                type="submit"
                                disabled={isLoading}
                                className="px-4 py-2 bg-primary text-white rounded-lg hover:bg-primary/90 transition-colors disabled:opacity-50"
                            >
                                {t('common.continue', { defaultValue: 'Continue' })}
                            </button>
                        </div>
                    </form>
                </div>
            )}
        </div>
    );
};

export default ChangePasswordModal;

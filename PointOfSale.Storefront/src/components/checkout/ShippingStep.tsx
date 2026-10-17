/**
 * Shipping Step Component
 * 
 * First step of checkout: Address form + Shipping method selection.
 * Features:
 * - Real-time validation
 * - Labels above inputs
 * - Inline field errors
 * - Explicit shipping selection; never substitute a failed or stale selection
 * 
 * Phase 7.7: Checkout UI Implementation
 */

import React, { useEffect, useRef, useState } from 'react';
import { useTranslation } from '@/i18n';
import {
    useCheckoutStore,
    selectShippingMethods,
    selectShippingMethodId,
    selectShippingMethodsStatus,
    selectShippingMethodsLoading,
    selectShippingSelectionLoading,
    selectShippingMethodsError,
    selectShippingSelectionStatus,
    selectStaleShippingSelection,
    selectCheckoutFields,
    selectCheckoutFieldsStatus,
    selectCheckoutFieldsLoading,
    selectCheckoutFieldsError,
    selectCheckoutFieldsTotalCount,
    selectCustomFieldValues,
} from '@/store/checkoutStore';
import type { CheckoutAttachment, CheckoutFieldDefinition, ShippingAddress } from '@/types/checkout';
import { useCurrency } from '@/hooks';
import { httpClient } from '@/services/api';
import { useCartStore } from '@/store/cartStore';
import { useStoreConfigStore } from '@/store/storeConfigStore';
import { getEffectiveCheckoutFields } from '@/utils/checkoutFieldRules';
import { normalizeCountryCode } from '@/utils/country';
import { formatPhoneForLocalInput, getCallingCodeForCountry, normalizePhoneForSubmission } from '@/utils/phone';

interface ShippingStepProps {
    onContinue: () => void;
}

const checkoutFieldGridClassName = 'grid grid-cols-1 gap-5 md:grid-cols-2 md:gap-x-5 md:gap-y-6';
const checkoutControlClassName = (hasError: boolean) => `
    min-h-12 w-full rounded-lg border bg-background px-3.5 py-3
    text-base text-text placeholder:text-muted/60 shadow-sm outline-none
    transition-colors duration-200 sm:text-sm
    focus:border-primary focus:ring-2 focus:ring-primary/20
    disabled:cursor-not-allowed disabled:bg-muted/10 disabled:text-muted
    ${hasError
        ? 'border-red-300 focus:border-red-500 focus:ring-red-500/20'
        : 'border-muted/25 hover:border-muted/40'
    }
`;
const checkoutTextareaClassName = (hasError: boolean) => `${checkoutControlClassName(hasError)} min-h-28 resize-y leading-6`;
const checkoutLabelClassName = 'block text-sm font-semibold text-text';
const checkoutHelpClassName = 'text-xs leading-5 text-muted';
const checkoutErrorClassName = 'flex items-start gap-1.5 text-sm leading-5 text-red-500';

const requiredIndicator = <span className="text-red-500 ltr:ml-0.5 rtl:mr-0.5" aria-hidden="true">*</span>;

const getFieldHelpId = (inputId: string, hasHelpText: boolean) => hasHelpText ? `${inputId}-help` : undefined;
const getFieldDescribedBy = (inputId: string, hasHelpText: boolean, hasError: boolean) => {
    if (hasError) return `${inputId}-error`;
    return getFieldHelpId(inputId, hasHelpText);
};

/**
 * Input Field Component
 */
interface InputFieldProps {
    label: string;
    name: keyof ShippingAddress;
    value: string;
    onChange: (value: string) => void;
    onBlur: () => void;
    error?: string;
    placeholder?: string;
    helpText?: string | null;
    type?: string;
    required?: boolean;
    multiline?: boolean;
}

const InputField: React.FC<InputFieldProps> = ({
    label,
    name,
    value,
    onChange,
    onBlur,
    error,
    placeholder,
    helpText,
    type = 'text',
    required = false,
    multiline = false,
}) => {
    const inputId = `checkout-${name}`;
    const errorId = `${inputId}-error`;
    const helpId = getFieldHelpId(inputId, Boolean(helpText));
    const hasError = !!error;

    return (
        <div className="space-y-2">
            <label htmlFor={inputId} className={checkoutLabelClassName}>
                {label}
                {required && requiredIndicator}
            </label>

            {multiline ? (
                <textarea
                    id={inputId}
                    name={name}
                    value={value}
                    onChange={(e) => onChange(e.target.value)}
                    onBlur={onBlur}
                    placeholder={placeholder}
                    rows={3}
                    className={checkoutTextareaClassName(hasError)}
                    aria-invalid={hasError}
                    aria-describedby={getFieldDescribedBy(inputId, Boolean(helpText), hasError)}
                    required={required}
                />
            ) : (
                <input
                    id={inputId}
                    type={type}
                    name={name}
                    value={value}
                    onChange={(e) => onChange(e.target.value)}
                    onBlur={onBlur}
                    placeholder={placeholder}
                    className={checkoutControlClassName(hasError)}
                    aria-invalid={hasError}
                    aria-describedby={getFieldDescribedBy(inputId, Boolean(helpText), hasError)}
                    required={required}
                />
            )}

            {helpText && !hasError && (
                <p id={helpId} className={checkoutHelpClassName}>{helpText}</p>
            )}

            {hasError && (
                <p id={errorId} className={checkoutErrorClassName} role="alert">
                    <svg className="w-4 h-4 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                    {error}
                </p>
            )}
        </div>
    );
};

interface PhoneInputFieldProps extends Omit<InputFieldProps, 'name' | 'type' | 'multiline'> {
    countryCode?: string | null;
}

const PhoneInputField: React.FC<PhoneInputFieldProps> = ({
    label,
    value,
    onChange,
    onBlur,
    error,
    placeholder,
    helpText,
    required = false,
    countryCode,
}) => {
    const inputId = 'checkout-phone';
    const errorId = `${inputId}-error`;
    const helpId = getFieldHelpId(inputId, Boolean(helpText));
    const hasError = !!error;
    const localValue = formatPhoneForLocalInput(value, countryCode);
    const callingCode = localValue.trim().startsWith('+') ? null : getCallingCodeForCountry(countryCode);

    return (
        <div className="space-y-2">
            <label htmlFor={inputId} className={checkoutLabelClassName}>
                {label}
                {required && requiredIndicator}
            </label>

            <div className="flex" dir="ltr">
                {callingCode && (
                    <span className={`inline-flex min-h-12 items-center rounded-l-lg border border-r-0 bg-muted/10 px-3.5 text-sm font-semibold text-text ${hasError ? 'border-red-300' : 'border-muted/25'}`}>
                        {callingCode}
                    </span>
                )}
                <input
                    id={inputId}
                    type="tel"
                    inputMode="tel"
                    autoComplete="tel-national"
                    name="phone"
                    value={localValue}
                    onChange={(event) => onChange(event.target.value)}
                    onBlur={() => {
                        onChange(normalizePhoneForSubmission(value, countryCode));
                        onBlur?.();
                    }}
                    placeholder={placeholder}
                    className={`${checkoutControlClassName(hasError)} ${callingCode ? 'rounded-l-none' : ''}`}
                    aria-invalid={hasError}
                    aria-describedby={getFieldDescribedBy(inputId, Boolean(helpText), hasError)}
                    required={required}
                />
            </div>

            {helpText && !hasError && (
                <p id={helpId} className={checkoutHelpClassName}>{helpText}</p>
            )}

            {hasError && (
                <p id={errorId} className={checkoutErrorClassName} role="alert">
                    <svg className="w-4 h-4 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                    {error}
                </p>
            )}
        </div>
    );
};

const SYSTEM_FIELD_ADDRESS_KEYS: Record<string, keyof ShippingAddress> = {
    full_name: 'fullName',
    phone: 'phone',
    email: 'email',
    city: 'city',
    area: 'area',
    street_address: 'addressLine',
    building: 'building',
    apartment: 'apartment',
    postal_code: 'postalCode',
    order_notes: 'notes',
};

const SYSTEM_FIELD_TRANSLATION_KEYS: Record<string, string> = {
    full_name: 'fullName',
    phone: 'phoneNumber',
    email: 'emailAddress',
    city: 'city',
    area: 'area',
    street_address: 'streetAddress',
    building: 'building',
    apartment: 'apartment',
    postal_code: 'postalCode',
    order_notes: 'orderNotes',
};

const getSystemInputType = (field: CheckoutFieldDefinition) => {
    if (field.fieldType === 'EMAIL') return 'email';
    if (field.fieldType === 'PHONE') return 'tel';
    if (field.fieldType === 'NUMBER') return 'number';
    if (field.fieldType === 'DATE') return 'date';
    return 'text';
};

const isUploadField = (field: CheckoutFieldDefinition) =>
    field.fieldType === 'IMAGE' || field.fieldType === 'FILE' || field.fieldType === 'MULTI_IMAGE';

export const getCheckoutFieldLayoutClass = (field: CheckoutFieldDefinition): string => {
    if (isUploadField(field)) return 'md:col-span-2';
    if (field.fieldType === 'TEXTAREA' || field.fieldType === 'RADIO' || field.fieldType === 'CHECKBOX') return 'md:col-span-2';
    if (field.fieldKind === 'SYSTEM' && (field.systemFieldKey === 'street_address' || field.systemFieldKey === 'order_notes')) return 'md:col-span-2';
    return '';
};

const parseAttachments = (value: string): CheckoutAttachment[] => {
    if (!value.trim()) return [];
    try {
        const parsed = JSON.parse(value);
        return Array.isArray(parsed) ? parsed.filter((item) => item?.attachmentId) : [];
    } catch {
        return [];
    }
};

const formatFileSize = (bytes: number, bytesLabel = 'bytes') => {
    if (bytes >= 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
    if (bytes >= 1024) return `${Math.round(bytes / 1024)} KB`;
    return `${bytes} ${bytesLabel}`;
};

const uploadFileToLease = (
    uploadUrl: string,
    uploadMethod: string,
    contentType: string,
    file: File,
    signal: AbortSignal,
    onProgress: (percentage: number) => void,
) => new Promise<void>((resolve, reject) => {
    const request = new XMLHttpRequest();
    const abort = () => request.abort();
    const cleanup = () => signal.removeEventListener('abort', abort);

    request.open(uploadMethod || 'PUT', uploadUrl);
    request.setRequestHeader('Content-Type', contentType);
    request.upload.addEventListener('progress', (event) => {
        if (event.lengthComputable && event.total > 0) {
            onProgress(Math.min(100, Math.round((event.loaded / event.total) * 100)));
        }
    });
    request.addEventListener('load', () => {
        cleanup();
        if (request.status >= 200 && request.status < 300) resolve();
        else reject(new Error('CHECKOUT_ATTACHMENT_STORAGE_UPLOAD_FAILED'));
    });
    request.addEventListener('error', () => {
        cleanup();
        reject(new Error('CHECKOUT_ATTACHMENT_STORAGE_UPLOAD_FAILED'));
    });
    request.addEventListener('abort', () => {
        cleanup();
        reject(new DOMException('Upload aborted', 'AbortError'));
    });

    signal.addEventListener('abort', abort, { once: true });
    if (signal.aborted) {
        request.abort();
        return;
    }

    request.send(file);
});

interface DynamicCustomFieldProps {
    field: CheckoutFieldDefinition;
    value: string;
    error?: string;
    onChange: (value: string) => void;
    onBlur: () => void;
}

const DynamicCustomField: React.FC<DynamicCustomFieldProps> = ({ field, value, error, onChange, onBlur }) => {
    const { t } = useTranslation();
    const inputId = `checkout-custom-${field.id}`;
    const errorId = `${inputId}-error`;
    const helpId = getFieldHelpId(inputId, Boolean(field.helpText));
    const hasError = !!error;
    const options = field.configuration.options ?? [];

    const label = field.fieldType === 'RADIO'
        ? (
            <span id={`${inputId}-label`} className={checkoutLabelClassName}>
                {field.label}
                {field.isRequired && requiredIndicator}
            </span>
        )
        : (
            <label htmlFor={inputId} className={checkoutLabelClassName}>
                {field.label}
                {field.isRequired && requiredIndicator}
            </label>
        );

    if (isUploadField(field)) {
        return <DynamicUploadField field={field} value={value} error={error} onChange={onChange} onBlur={onBlur} />;
    }

    return (
        <div className="space-y-2">
            {label}
            {field.fieldType === 'TEXTAREA' ? (
                <textarea
                    id={inputId}
                    value={value}
                    onChange={(event) => onChange(event.target.value)}
                    onBlur={onBlur}
                    rows={3}
                    className={checkoutTextareaClassName(hasError)}
                    aria-invalid={hasError}
                    aria-describedby={getFieldDescribedBy(inputId, Boolean(field.helpText), hasError)}
                    required={field.isRequired}
                />
            ) : field.fieldType === 'SELECT' ? (
                <select
                    id={inputId}
                    value={value}
                    onChange={(event) => onChange(event.target.value)}
                    onBlur={onBlur}
                    className={checkoutControlClassName(hasError)}
                    aria-invalid={hasError}
                    aria-describedby={getFieldDescribedBy(inputId, Boolean(field.helpText), hasError)}
                    required={field.isRequired}
                >
                    <option value="">{t('checkout.fields.selectOption')}</option>
                    {options.map((option) => (
                        <option key={option.value} value={option.value}>{option.label}</option>
                    ))}
                </select>
            ) : field.fieldType === 'RADIO' ? (
                <fieldset className="space-y-2" aria-labelledby={`${inputId}-label`} aria-describedby={getFieldDescribedBy(inputId, Boolean(field.helpText), hasError)}>
                    {options.map((option) => (
                        <label key={option.value} className="flex min-h-12 items-center gap-3 rounded-lg border border-muted/25 bg-background px-3.5 py-2.5 text-sm text-text shadow-sm transition-colors hover:border-muted/40">
                            <input
                                type="radio"
                                name={inputId}
                                value={option.value}
                                checked={value === option.value}
                                onChange={() => onChange(option.value)}
                                onBlur={onBlur}
                                className="h-4 w-4 border-muted/30 text-primary focus:ring-primary"
                            />
                            {option.label}
                        </label>
                    ))}
                </fieldset>
            ) : field.fieldType === 'CHECKBOX' ? (
                <label className="flex min-h-12 items-start gap-3 rounded-lg border border-muted/25 bg-background px-3.5 py-3 text-sm text-text shadow-sm transition-colors hover:border-muted/40">
                    <input
                        id={inputId}
                        type="checkbox"
                        checked={value === 'true'}
                        onChange={(event) => onChange(event.target.checked ? 'true' : 'false')}
                        onBlur={onBlur}
                        className="mt-0.5 h-4 w-4 rounded border-muted/30 text-primary focus:ring-primary"
                    />
                    <span>{field.helpText || field.label}</span>
                </label>
            ) : (
                <input
                    id={inputId}
                    type={getSystemInputType(field)}
                    value={value}
                    onChange={(event) => onChange(event.target.value)}
                    onBlur={onBlur}
                    className={checkoutControlClassName(hasError)}
                    aria-invalid={hasError}
                    aria-describedby={getFieldDescribedBy(inputId, Boolean(field.helpText), hasError)}
                    required={field.isRequired}
                />
            )}

            {field.helpText && field.fieldType !== 'CHECKBOX' && !hasError && (
                <p id={helpId} className={checkoutHelpClassName}>{field.helpText}</p>
            )}

            {hasError && (
                <p id={errorId} className={checkoutErrorClassName} role="alert">
                    {error}
                </p>
            )}
        </div>
    );
};

const DynamicUploadField: React.FC<DynamicCustomFieldProps> = ({ field, value, error, onChange, onBlur }) => {
    const { t } = useTranslation();
    const inputId = `checkout-custom-${field.id}`;
    const errorId = `${inputId}-error`;
    const [uploadError, setUploadError] = useState<string | null>(null);
    const [uploadingFileName, setUploadingFileName] = useState<string | null>(null);
    const [uploadProgress, setUploadProgress] = useState<number | null>(null);
    const [failedFile, setFailedFile] = useState<File | null>(null);
    const fileInputRef = useRef<HTMLInputElement>(null);
    const uploadAbortControllerRef = useRef<AbortController | null>(null);
    const cartId = useCartStore((state) => state.cart?.cartId ?? null);
    const beginAttachmentUpload = useCheckoutStore((state) => state.beginAttachmentUpload);
    const endAttachmentUpload = useCheckoutStore((state) => state.endAttachmentUpload);
    const attachments = parseAttachments(value);
    const maxFiles = field.fieldType === 'MULTI_IMAGE' ? (field.configuration.maxFiles ?? 5) : 1;
    const accept = field.configuration.allowedMimeTypes?.join(',') || (field.fieldType === 'FILE' ? undefined : 'image/jpeg,image/png,image/webp');
    const hasError = Boolean(error || uploadError);

    useEffect(() => () => {
        uploadAbortControllerRef.current?.abort();
        uploadAbortControllerRef.current = null;
    }, [cartId]);

    const updateAttachments = (next: CheckoutAttachment[]) => {
        onChange(JSON.stringify(next));
        onBlur();
    };

    const uploadFiles = async (files: FileList | File[] | null) => {
        if (!files || files.length === 0) return;
        if (!cartId) {
            setUploadError(t('checkout.fields.cartNotReady'));
            return;
        }

        setUploadError(null);
        setFailedFile(null);
        const controller = new AbortController();
        uploadAbortControllerRef.current = controller;
        beginAttachmentUpload();
        const next = [...attachments];
        let activeFile: File | null = null;
        try {
            for (const file of Array.from(files)) {
                activeFile = file;
                if (next.length >= maxFiles) {
                    setUploadError(t('checkout.fields.maxFiles', { label: field.label, count: maxFiles }));
                    break;
                }

                const maxFileSize = field.configuration.maxFileSize;
                if (maxFileSize && file.size > maxFileSize) {
                    setUploadError(t('checkout.fields.maxFileSize', { fileName: file.name, size: formatFileSize(maxFileSize, t('checkout.fields.bytes')) }));
                    continue;
                }

                setUploadingFileName(file.name);
                setUploadProgress(0);
                const lease = await httpClient.post('/api/storefront/checkout/attachments/upload-lease', {
                    cartId,
                    fieldId: field.id,
                    fileName: file.name,
                    contentType: file.type,
                    fileSizeBytes: file.size,
                }, { signal: controller.signal });

                await uploadFileToLease(
                    lease.data.uploadUrl,
                    lease.data.uploadMethod || 'PUT',
                    lease.data.contentType,
                    file,
                    controller.signal,
                    setUploadProgress,
                );

                const confirmed = await httpClient.post<CheckoutAttachment>('/api/storefront/checkout/attachments/confirm', {
                    cartId,
                    fieldId: field.id,
                    attachmentId: lease.data.attachmentId,
                }, { signal: controller.signal });
                next.push(confirmed.data);
                updateAttachments(next);
                setFailedFile(null);
                setUploadingFileName(null);
                setUploadProgress(null);
            }
        } catch (err) {
            if (!controller.signal.aborted) {
                const apiError = err as { response?: { data?: { message?: string } }; message?: string };
                setFailedFile(activeFile);
                setUploadError(
                    apiError.response?.data?.message
                    || (apiError.message === 'CHECKOUT_ATTACHMENT_STORAGE_UPLOAD_FAILED'
                        ? t('checkout.fields.storageUploadFailed')
                        : apiError.message)
                    || t('checkout.fields.uploadFailed'),
                );
            }
        } finally {
            if (uploadAbortControllerRef.current === controller) {
                uploadAbortControllerRef.current = null;
            }
            endAttachmentUpload();
            setUploadingFileName(null);
            setUploadProgress(null);
            if (fileInputRef.current) fileInputRef.current.value = '';
        }
    };

    const removeAttachment = async (attachment: CheckoutAttachment) => {
        const next = attachments.filter((item) => item.attachmentId !== attachment.attachmentId);
        updateAttachments(next);
        if (!cartId) return;
        try {
            await httpClient.delete(`/api/storefront/checkout/attachments/${attachment.attachmentId}`, {
                params: { cartId, fieldId: field.id },
            });
        } catch {
            // Orphan cleanup will expire unreconciled pending objects.
        }
    };

    return (
        <div className="space-y-3 rounded-xl border border-muted/20 bg-muted/5 p-4">
            <label htmlFor={inputId} className={checkoutLabelClassName}>
                {field.label}
                {field.isRequired && requiredIndicator}
            </label>
            <input
                id={inputId}
                ref={fileInputRef}
                type="file"
                accept={accept}
                multiple={field.fieldType === 'MULTI_IMAGE'}
                disabled={uploadingFileName != null || attachments.length >= maxFiles}
                onChange={(event) => uploadFiles(event.target.files)}
                onBlur={onBlur}
                className="block min-h-12 w-full rounded-lg border border-muted/25 bg-background px-3.5 py-3 text-sm text-text shadow-sm file:rounded-md file:border-0 file:bg-primary file:px-3 file:py-2 file:text-sm file:font-medium file:text-white ltr:file:mr-3 rtl:file:ml-3 focus:outline-none focus:ring-2 focus:ring-primary/20 disabled:opacity-60"
                aria-invalid={hasError}
                aria-describedby={hasError ? errorId : undefined}
            />
            {field.helpText && !hasError && <p className={checkoutHelpClassName}>{field.helpText}</p>}
            {uploadingFileName && (
                <div className="space-y-1" role="status" aria-live="polite">
                    <p className="text-sm text-muted">
                        {t('checkout.fields.uploading', { fileName: uploadingFileName, progress: uploadProgress ?? 0 })}
                    </p>
                    <progress className="h-2 w-full accent-primary" max="100" value={uploadProgress ?? 0}>
                        {uploadProgress ?? 0}%
                    </progress>
                </div>
            )}
            {attachments.length > 0 && (
                <ul className="space-y-2" aria-label={t('checkout.fields.uploadedFiles', { label: field.label })}>
                    {attachments.map((attachment) => (
                        <li key={attachment.attachmentId} className="flex flex-col gap-2 rounded-lg border border-muted/20 bg-background px-3 py-2 text-sm text-text sm:flex-row sm:items-center sm:justify-between">
                            <span className="min-w-0 truncate">{attachment.fileName} ({formatFileSize(attachment.sizeBytes, t('checkout.fields.bytes'))})</span>
                            <button
                                type="button"
                                onClick={() => removeAttachment(attachment)}
                                disabled={uploadingFileName != null}
                                className="self-start rounded-md border border-red-200 px-3 py-1.5 text-sm font-medium text-red-600 focus:outline-none focus-visible:ring-2 focus-visible:ring-red-500 disabled:opacity-60 sm:self-auto"
                            >
                                {t('checkout.fields.remove')}
                            </button>
                        </li>
                    ))}
                </ul>
            )}
            {hasError && (
                <p id={errorId} className="text-sm leading-5 text-red-500" role="alert">
                    {uploadError || error}
                </p>
            )}
            {failedFile && !uploadingFileName ? (
                <button
                    type="button"
                    onClick={() => void uploadFiles([failedFile])}
                    className="rounded-md border border-primary/30 px-3 py-2 text-sm font-medium text-primary focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/30"
                >
                    {t('checkout.fields.retryUpload')}
                </button>
            ) : null}
        </div>
    );
};

/**
 * Shipping Method Card
 */
interface ShippingMethodCardProps {
    id: string;
    name: string;
    description?: string;
    price: number;
    formatPrice: (amount: number) => string;
    estimatedDays?: number;
    isSelected: boolean;
    isDisabled: boolean;
    onSelect: () => void;
}

const ShippingMethodCard: React.FC<ShippingMethodCardProps> = ({
    id,
    name,
    description,
    price,
    formatPrice,
    estimatedDays,
    isSelected,
    isDisabled,
    onSelect,
}) => {
    const { t } = useTranslation();

    return (
        <label
            className={`
                flex items-start gap-3 p-4
                border rounded-lg
                transition-all duration-200
                ${isDisabled ? 'cursor-not-allowed opacity-60' : 'cursor-pointer'}
                ${isSelected
                    ? 'border-primary bg-primary/5 ring-2 ring-primary/20'
                    : 'border-muted/20 hover:border-muted/40 hover:bg-muted/5'
                }
            `}
        >
            <input
                type="radio"
                name="shippingMethod"
                value={id}
                checked={isSelected}
                disabled={isDisabled}
                onChange={onSelect}
                className="mt-1 w-4 h-4 text-primary border-muted/30 focus:ring-primary"
            />

            <div className="flex-1">
                <div className="flex items-center justify-between">
                    <span className="font-medium text-text">{name}</span>
                    <span className="font-semibold text-text">
                        {price > 0 ? formatPrice(price) : t('checkout.shipping.free')}
                    </span>
                </div>

                {description && (
                    <p className="text-sm text-muted mt-0.5">{description}</p>
                )}

                {estimatedDays && (
                    <p className="text-xs text-muted mt-1">
                        {t(
                            estimatedDays === 1
                                ? 'checkout.shipping.estimatedDeliveryDay'
                                : 'checkout.shipping.estimatedDeliveryDays',
                            { count: estimatedDays },
                        )}
                    </p>
                )}
            </div>
        </label>
    );
};

/**
 * Shipping Step Component
 */
export const ShippingStep: React.FC<ShippingStepProps> = ({ onContinue }) => {
    const { t, language } = useTranslation();
    
    // Currency formatting from store config
    const { formatPrice } = useCurrency();

    // Store state
    const address = useCheckoutStore((state) => state.address);
    const defaultCountryCode = useStoreConfigStore((state) => state.config.defaultCountryCode);
    const phoneCountryCode = normalizeCountryCode(address.country) ?? normalizeCountryCode(defaultCountryCode);
    const shippingMethods = useCheckoutStore(selectShippingMethods);
    const selectedMethodId = useCheckoutStore(selectShippingMethodId);
    const shippingMethodsStatus = useCheckoutStore(selectShippingMethodsStatus);
    const isLoadingMethods = useCheckoutStore(selectShippingMethodsLoading);
    const isSelectingMethod = useCheckoutStore(selectShippingSelectionLoading);
    const shippingMethodsError = useCheckoutStore(selectShippingMethodsError);
    const shippingSelectionStatus = useCheckoutStore(selectShippingSelectionStatus);
    const staleShippingSelection = useCheckoutStore(selectStaleShippingSelection);
    const checkoutFields = useCheckoutStore(selectCheckoutFields);
    const checkoutFieldsStatus = useCheckoutStore(selectCheckoutFieldsStatus);
    const checkoutFieldsLoading = useCheckoutStore(selectCheckoutFieldsLoading);
    const checkoutFieldsError = useCheckoutStore(selectCheckoutFieldsError);
    const checkoutFieldsTotalCount = useCheckoutStore(selectCheckoutFieldsTotalCount);
    const customFieldValues = useCheckoutStore(selectCustomFieldValues);
    // Store actions
    const setAddressField = useCheckoutStore((state) => state.setAddressField);
    const touchField = useCheckoutStore((state) => state.touchField);
    const setShippingMethod = useCheckoutStore((state) => state.setShippingMethod);
    const fetchShippingMethods = useCheckoutStore((state) => state.fetchShippingMethods);
    const fetchCheckoutFields = useCheckoutStore((state) => state.fetchCheckoutFields);
    const getFieldError = useCheckoutStore((state) => state.getFieldError);
    const setCustomFieldValue = useCheckoutStore((state) => state.setCustomFieldValue);
    const touchCustomField = useCheckoutStore((state) => state.touchCustomField);
    const getCustomFieldError = useCheckoutStore((state) => state.getCustomFieldError);
    const isStepValid = useCheckoutStore((state) => state.isStepValid);
    const setShowAllErrors = useCheckoutStore((state) => state.setShowAllErrors);

    // Handle continue button
    const handleContinue = () => {
        const canonicalPhone = normalizePhoneForSubmission(address.phone, phoneCountryCode);
        if (canonicalPhone !== address.phone) setAddressField('phone', canonicalPhone);
        if (isStepValid('shipping')) {
            onContinue();
        } else {
            setShowAllErrors(true);
        }
    };

    const canContinue = isStepValid('shipping');
    const configuredCheckoutFields = checkoutFieldsTotalCount > 0;
    const useDynamicFields = configuredCheckoutFields && checkoutFieldsStatus === 'AVAILABLE';
    const effectiveCheckoutFields = useDynamicFields
        ? getEffectiveCheckoutFields(checkoutFields, address, customFieldValues)
        : [];

    const renderDynamicField = (field: CheckoutFieldDefinition) => {
        if (field.fieldKind === 'SYSTEM') {
            const addressKey = field.systemFieldKey ? SYSTEM_FIELD_ADDRESS_KEYS[field.systemFieldKey] : undefined;
            if (!addressKey) {
                return null;
            }

            const translationKey = field.systemFieldKey ? SYSTEM_FIELD_TRANSLATION_KEYS[field.systemFieldKey] : undefined;
            const label = translationKey ? t(`checkout.shipping.${translationKey}`) : field.label;

            if (field.systemFieldKey === 'phone') {
                return (
                    <div key={field.id} className={getCheckoutFieldLayoutClass(field)}>
                        <PhoneInputField
                            label={label}
                            value={address.phone || ''}
                            onChange={(value) => setAddressField('phone', value)}
                            onBlur={() => touchField('phone')}
                            error={getFieldError('phone')}
                            placeholder={t('checkout.shipping.phonePlaceholder')}
                            helpText={field.helpText || t('checkout.shipping.phoneHelp')}
                            required={field.isRequired}
                            countryCode={phoneCountryCode}
                        />
                    </div>
                );
            }

            return (
                <div key={field.id} className={getCheckoutFieldLayoutClass(field)}>
                    <InputField
                        label={label}
                        name={addressKey}
                        type={getSystemInputType(field)}
                        value={(address[addressKey] as string | undefined) || ''}
                        onChange={(value) => setAddressField(addressKey, value)}
                        onBlur={() => touchField(addressKey)}
                        error={getFieldError(addressKey)}
                        placeholder={label}
                        helpText={field.helpText}
                        multiline={field.fieldType === 'TEXTAREA'}
                        required={field.isRequired}
                    />
                </div>
            );
        }

        return (
            <div key={field.id} className={getCheckoutFieldLayoutClass(field)}>
                <DynamicCustomField
                    field={field}
                    value={customFieldValues[field.id] ?? ''}
                    error={getCustomFieldError(field.id)}
                    onChange={(value) => setCustomFieldValue(field.id, value)}
                    onBlur={() => touchCustomField(field.id)}
                />
            </div>
        );
    };

    return (
        <div className="space-y-8">
            {/* Shipping Address */}
            <section className="space-y-5">
                <div>
                    <h2 className="text-lg font-semibold text-text">{t('checkout.shipping.addressTitle')}</h2>
                </div>

                {checkoutFieldsLoading ? (
                    <div className={checkoutFieldGridClassName} role="status" aria-label={t('checkout.shipping.loadingFields')}>
                        {[1, 2, 3, 4].map((item) => (
                            <div key={item} className="h-24 rounded-lg bg-muted/10 skeleton" />
                        ))}
                    </div>
                ) : checkoutFieldsStatus === 'UNAVAILABLE' || checkoutFieldsStatus === 'ERROR' ? (
                    <div className="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700" role="alert">
                        <p>{checkoutFieldsError || t('checkout.shipping.fieldsUnavailable')}</p>
                        <p className="mt-1">{t('checkout.shipping.fieldsUnavailableHelp')}</p>
                        <button
                            type="button"
                            onClick={() => void fetchCheckoutFields(true)}
                            className="mt-3 inline-flex items-center rounded-lg border border-red-300 px-3 py-2 font-medium text-red-700 hover:bg-red-100"
                        >
                            {t('checkout.shipping.retryFields')}
                        </button>
                    </div>
                ) : configuredCheckoutFields && checkoutFieldsStatus === 'EMPTY' ? (
                    <p className="rounded-lg border border-muted/20 bg-muted/5 p-4 text-sm text-muted" role="status">
                        {t('checkout.shipping.noAdditionalFields')}
                    </p>
                ) : useDynamicFields ? (
                    <div className={checkoutFieldGridClassName}>
                        {effectiveCheckoutFields.map(renderDynamicField)}
                    </div>
                ) : (
                <div className={checkoutFieldGridClassName}>
                    <InputField
                        label={t('checkout.shipping.fullName')}
                        name="fullName"
                        value={address.fullName}
                        onChange={(value) => setAddressField('fullName', value)}
                        onBlur={() => touchField('fullName')}
                        error={getFieldError('fullName')}
                        placeholder={t('checkout.shipping.fullNamePlaceholder')}
                        required
                    />

                    <PhoneInputField
                        label={t('checkout.shipping.phoneNumber')}
                        value={address.phone}
                        onChange={(value) => setAddressField('phone', value)}
                        onBlur={() => touchField('phone')}
                        error={getFieldError('phone')}
                        placeholder={t('checkout.shipping.phonePlaceholder')}
                        helpText={t('checkout.shipping.phoneHelp')}
                        required
                        countryCode={phoneCountryCode}
                    />

                    <InputField
                        label={t('checkout.shipping.city')}
                        name="city"
                        value={address.city}
                        onChange={(value) => setAddressField('city', value)}
                        onBlur={() => touchField('city')}
                        error={getFieldError('city')}
                        placeholder={t('checkout.shipping.cityPlaceholder')}
                        required
                    />

                    <InputField
                        label={t('checkout.shipping.area')}
                        name="area"
                        value={address.area}
                        onChange={(value) => setAddressField('area', value)}
                        onBlur={() => touchField('area')}
                        error={getFieldError('area')}
                        placeholder={t('checkout.shipping.areaPlaceholder')}
                        required
                    />

                    <div className="md:col-span-2">
                        <InputField
                            label={t('checkout.shipping.streetAddress')}
                            name="addressLine"
                            value={address.addressLine}
                            onChange={(value) => setAddressField('addressLine', value)}
                            onBlur={() => touchField('addressLine')}
                            error={getFieldError('addressLine')}
                            placeholder={t('checkout.shipping.streetAddressPlaceholder')}
                            multiline
                            required
                        />
                    </div>

                    <div className="md:col-span-2">
                        <InputField
                            label={t('checkout.shipping.deliveryNotes')}
                            name="notes"
                            value={address.notes || ''}
                            onChange={(value) => setAddressField('notes', value)}
                            onBlur={() => touchField('notes')}
                            placeholder={t('checkout.shipping.deliveryNotesPlaceholder')}
                            multiline
                        />
                    </div>
                </div>
                )}
            </section>

            {/* Shipping Method */}
            <fieldset className="min-w-0 border-0 p-0">
                <legend className="mb-4 text-lg font-semibold text-text">{t('checkout.shipping.methodTitle')}</legend>

                {isLoadingMethods ? (
                    <div className="space-y-3" role="status" aria-label={t('checkout.shipping.loadingMethods')}>
                        {[1, 2].map((i) => (
                            <div key={i} className="h-20 bg-muted/10 rounded-lg skeleton" />
                        ))}
                    </div>
                ) : shippingMethodsStatus === 'EMPTY' ? (
                    <div className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800" role="status">
                        <p className="font-medium">{t('checkout.shipping.noEnabledMethods')}</p>
                        <p className="mt-1 text-amber-700">
                            {t('checkout.shipping.noEnabledMethodsHelp')}
                        </p>
                    </div>
                ) : shippingMethodsStatus === 'NOT_APPLICABLE' ? (
                    <div className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800" role="status">
                        <p className="font-medium">{t('checkout.shipping.notApplicable')}</p>
                        <p className="mt-1 text-amber-700">
                            {t('checkout.shipping.notApplicableHelp')}
                        </p>
                    </div>
                ) : shippingMethodsStatus === 'UNAVAILABLE' || shippingMethodsStatus === 'ERROR' ? (
                    <div className="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700" role="alert">
                        <p>{shippingMethodsError || t('checkout.shipping.unavailable')}</p>
                        <p className="mt-1 text-red-600">
                            {t('checkout.shipping.unavailableHelp')}
                        </p>
                        <button
                            type="button"
                            onClick={() => void fetchShippingMethods({ force: true })}
                            className="mt-3 inline-flex items-center rounded-lg border border-red-300 px-3 py-2 font-medium text-red-700 hover:bg-red-100"
                        >
                            {t('checkout.shipping.retryMethods')}
                        </button>
                    </div>
                ) : shippingMethodsStatus === 'VALIDATION_REQUIRED' ? (
                    <div className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800" role="status">
                        <p className="font-medium">{t('checkout.shipping.contextRequired')}</p>
                        <p className="mt-1 text-amber-700">
                            {shippingMethodsError || t('checkout.shipping.contextRequiredHelp')}
                        </p>
                    </div>
                ) : shippingMethods.length === 0 ? (
                    <div className="p-4 bg-amber-50 border border-amber-200 rounded-lg text-amber-800 text-sm">
                        {t('checkout.shipping.noMethods')}
                    </div>
                ) : (
                    <div className="space-y-3">
                        {isSelectingMethod && (
                            <p className="text-sm text-muted" role="status" aria-live="polite">
                                {t('checkout.shipping.confirmingAvailability')}
                            </p>
                        )}
                        {shippingSelectionStatus === 'stale' && staleShippingSelection && (
                            <div 
                                className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800"
                                role="status"
                                aria-live="polite"
                                aria-atomic="true"
                            >
                                <p className="font-medium">
                                    {t(staleShippingSelection.message) === staleShippingSelection.message
                                        ? t('checkout.staleSelection')
                                        : t(staleShippingSelection.message)}
                                </p>
                                <p className="mt-1 text-amber-700">
                                    {t('checkout.previousSelection')}: {staleShippingSelection.label}
                                </p>
                            </div>
                        )}
                        {shippingMethods.map((method) => (
                            <ShippingMethodCard
                                key={method.id}
                                id={method.id}
                                name={language === 'ar' && method.nameAr?.trim() ? method.nameAr : method.name}
                                description={method.description}
                                price={method.price}
                                formatPrice={formatPrice}
                                estimatedDays={method.estimatedDays}
                                isSelected={selectedMethodId === method.id}
                                isDisabled={isSelectingMethod}
                                onSelect={() => { void setShippingMethod(method.id).catch(() => undefined); }}
                            />
                        ))}
                    </div>
                )}
            </fieldset>

            {/* Continue Button */}
            <div className="pt-4">
                <button
                    onClick={handleContinue}
                    disabled={!canContinue}
                    className={`
                        w-full py-3.5 px-6
                        rounded-xl font-medium
                        transition-all duration-200
                        focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                        ${canContinue
                            ? 'bg-primary text-white hover:bg-primary/90 shadow-md hover:shadow-lg'
                            : 'bg-muted/20 text-muted cursor-not-allowed'
                        }
                    `}
                    aria-disabled={!canContinue}
                >
                    {t('checkout.shipping.continueToPayment')}
                </button>
            </div>
        </div>
    );
};

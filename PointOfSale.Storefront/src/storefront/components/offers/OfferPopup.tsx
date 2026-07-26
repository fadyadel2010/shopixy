import React, { useState } from 'react';
import type { AxiosError } from 'axios';
import type { ApiErrorResponse, SmartOffer } from '@/types';
import { redeemSmartOffer } from '@/services/api/storefrontApi';
import { normalizeError } from '@/services/api/httpClient';
import { setPendingSmartOfferRedeemToken } from '../../services/smartOfferCouponBridge';

interface OfferPopupProps {
    offer: SmartOffer;
    isRTL: boolean;
    onClose: () => void;
}

const fallbackBackground = '#f97316';

const OfferPopup: React.FC<OfferPopupProps> = ({ offer, isRTL, onClose }) => {
    const [isRedeeming, setIsRedeeming] = useState(false);
    const [redeemError, setRedeemError] = useState<string | null>(null);

    const handlePrimaryAction = async () => {
        if (isRedeeming) {
            return;
        }

        setIsRedeeming(true);
        setRedeemError(null);

        try {
            const redemption = await redeemSmartOffer(offer.id);
            setPendingSmartOfferRedeemToken(redemption.redeemToken);
            onClose();
        } catch (error) {
            const normalized = error && typeof error === 'object'
                ? normalizeError(error as AxiosError<ApiErrorResponse>)
                : {
                    status: undefined,
                    code: undefined,
                    message: error instanceof Error ? error.message : undefined,
                    isNetworkError: false,
                    isServerError: false,
                    isClientError: false,
                    retryable: false,
                    isAuthError: false,
                };
            setRedeemError(
                normalized.code === 'COUPON_AUTH_REQUIRED'
                    ? 'Sign in to unlock this offer.'
                    : normalized.message || 'This offer is not available right now.'
            );
        } finally {
            setIsRedeeming(false);
        }
    };

    return (
        <>
            <div
                className="fixed inset-0 z-[120] bg-slate-950/65 backdrop-blur-sm"
                aria-hidden="true"
                onClick={onClose}
            />
            <div className="fixed inset-0 z-[121] flex items-center justify-center p-4">
                <div
                    className="offer-popup-grid relative w-full max-w-4xl overflow-hidden rounded-[32px] border border-white/10 bg-white shadow-[0_35px_120px_rgba(15,23,42,0.4)]"
                    dir={isRTL ? 'rtl' : 'ltr'}
                    role="dialog"
                    aria-modal="true"
                    aria-labelledby="smart-offer-popup-title"
                >
                    <button
                        type="button"
                        onClick={onClose}
                        className={`absolute top-4 z-10 flex h-11 w-11 items-center justify-center rounded-full bg-white/85 text-xl text-slate-500 shadow-lg transition hover:bg-white ${isRTL ? 'left-4' : 'right-4'}`}
                        aria-label="Close offer"
                    >
                        ×
                    </button>

                    <div
                        className="relative overflow-hidden px-6 py-8 sm:px-8 sm:py-10"
                        style={{
                            background:
                                `radial-gradient(circle at top, rgba(255,255,255,0.68), rgba(255,255,255,0.14)), ${offer.backgroundColor || fallbackBackground}`,
                        }}
                    >
                        <div className="absolute inset-0 bg-[linear-gradient(135deg,rgba(255,255,255,0.3),transparent_55%)]" />
                        <div className="relative space-y-5 text-slate-950">
                            <div className="inline-flex w-fit items-center rounded-full bg-slate-950 px-3 py-1 text-xs font-semibold uppercase tracking-[0.32em] text-white">
                                Smart Offer
                            </div>

                            <div className="space-y-3">
                                <h2 id="smart-offer-popup-title" className="text-3xl font-semibold leading-tight sm:text-4xl">
                                    {offer.popupTitle}
                                </h2>
                                {offer.popupDescription ? (
                                    <p className="max-w-xl text-sm leading-7 text-slate-800 sm:text-base">
                                        {offer.popupDescription}
                                    </p>
                                ) : null}
                            </div>

                            <div className="inline-flex w-fit items-center rounded-[24px] bg-white/80 px-4 py-3 text-xs font-semibold uppercase tracking-[0.24em] text-slate-600 shadow-sm">
                                Server-verified offer
                            </div>

                            <div className="flex flex-wrap items-center gap-3 pt-2">
                                <button
                                    type="button"
                                    onClick={handlePrimaryAction}
                                    disabled={isRedeeming}
                                    className="rounded-2xl bg-slate-950 px-6 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-70"
                                >
                                    {isRedeeming ? 'Applying...' : offer.buttonText}
                                </button>
                                <button
                                    type="button"
                                    onClick={onClose}
                                    className="rounded-2xl border border-slate-300 bg-white/70 px-6 py-3 text-sm font-medium text-slate-700 transition hover:bg-white"
                                >
                                    {isRTL ? 'اغلاق' : 'Close'}
                                </button>
                            </div>

                            {redeemError ? (
                                <p className="text-sm font-medium text-rose-700">{redeemError}</p>
                            ) : null}
                        </div>
                    </div>

                    {offer.imageUrl ? (
                        <div className="min-h-[260px] bg-slate-100">
                            <img
                                src={offer.imageUrl}
                                alt={offer.popupTitle}
                                className="h-full w-full object-cover"
                                loading="lazy"
                            />
                        </div>
                    ) : (
                        <div className="offer-popup-visual hidden bg-[radial-gradient(circle_at_top,_rgba(249,115,22,0.35),_rgba(15,23,42,0.08)_55%,_rgba(255,255,255,0.9))] lg:flex lg:flex-col lg:justify-between">
                            <div className="p-8">
                                <div className="h-40 rounded-[28px] border border-white/50 bg-white/50 shadow-[0_20px_60px_rgba(15,23,42,0.12)] backdrop-blur" />
                            </div>
                            <div className="space-y-3 px-8 pb-8">
                                <div className="h-3 w-24 rounded-full bg-slate-300/70" />
                                <div className="h-3 w-40 rounded-full bg-slate-300/60" />
                                <div className="h-3 w-32 rounded-full bg-slate-300/50" />
                            </div>
                        </div>
                    )}
                </div>
            </div>

            <style>{`
                .offer-popup-grid {
                    display: grid;
                    grid-template-columns: minmax(0, 1.15fr);
                }

                @media (min-width: 1024px) {
                    .offer-popup-grid {
                        grid-template-columns: minmax(0, 1.1fr) minmax(320px, 0.9fr);
                    }
                }
            `}</style>
        </>
    );
};

export default OfferPopup;

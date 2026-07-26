const popupBaseStyle = {
    backgroundImage: 'linear-gradient(145deg, rgba(255,255,255,0.94), rgba(255,255,255,0.78))',
}

export default function SmartOfferPreview({ offer, dir = 'ltr' }) {
    const backgroundColor = offer.backgroundColor?.trim() || '#f97316'

    return (
        <div
            className="rounded-3xl border border-slate-200 bg-white/80 p-5 shadow-2xl backdrop-blur"
            dir={dir}
        >
            <div
                className="overflow-hidden rounded-[28px] border border-white/60 shadow-xl"
                style={{
                    ...popupBaseStyle,
                    backgroundColor,
                }}
            >
                <div className="grid gap-0 md:grid-cols-[1.1fr_0.9fr]">
                    <div className="flex flex-col justify-between p-6 text-slate-900">
                        <div className="space-y-3">
                            <div className="inline-flex w-fit items-center rounded-full bg-white/75 px-3 py-1 text-xs font-semibold uppercase tracking-[0.28em] text-slate-600">
                                Smart Offer Preview
                            </div>
                            <div className="space-y-2">
                                <h3 className="text-2xl font-bold leading-tight">
                                    {offer.popupTitle || 'Give shoppers a reason to stay'}
                                </h3>
                                <p className="text-sm leading-6 text-slate-700">
                                    {offer.popupDescription || 'Use this space for a short conversion-focused message.'}
                                </p>
                            </div>
                            {offer.couponCode ? (
                                <div className="inline-flex w-fit items-center gap-2 rounded-2xl bg-slate-950 px-4 py-2 text-sm font-semibold text-white">
                                    <span>Coupon</span>
                                    <span className="rounded-full bg-white/15 px-3 py-1 font-mono tracking-[0.2em]">
                                        {offer.couponCode}
                                    </span>
                                </div>
                            ) : null}
                        </div>

                        <div className="mt-6 flex items-center justify-between gap-4">
                            <button
                                type="button"
                                className="rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white shadow-lg transition hover:translate-y-[-1px]"
                            >
                                {offer.buttonText || 'Claim offer'}
                            </button>
                            <button
                                type="button"
                                className="h-11 w-11 rounded-full border border-slate-300 bg-white/80 text-lg text-slate-500"
                            >
                                ×
                            </button>
                        </div>
                    </div>

                    <div className="relative min-h-[220px] bg-slate-950/10">
                        {offer.imageUrl ? (
                            <img
                                src={offer.imageUrl}
                                alt={offer.popupTitle || 'Offer'}
                                className="h-full w-full object-cover"
                            />
                        ) : (
                            <div className="flex h-full items-center justify-center bg-[radial-gradient(circle_at_top,_rgba(255,255,255,0.75),_rgba(15,23,42,0.08))] p-8 text-center text-sm font-medium text-slate-600">
                                Optional product or campaign artwork appears here.
                            </div>
                        )}
                    </div>
                </div>
            </div>
        </div>
    )
}

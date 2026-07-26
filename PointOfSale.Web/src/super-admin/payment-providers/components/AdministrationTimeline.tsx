import { Clock3 } from 'lucide-react'
import type { PaymentProviderAdministrationActivity } from '../paymentProviderTypes'

function formatDate(value: string) {
    const parsed = new Date(value)
    return Number.isNaN(parsed.getTime()) ? 'Unavailable' : parsed.toLocaleString()
}

export function AdministrationTimeline({ activity }: { activity: PaymentProviderAdministrationActivity[] }) {
    return (
        <section className="rounded-lg border border-slate-200 bg-white p-5 shadow-sm">
            <div className="flex items-center justify-between gap-3">
                <div>
                    <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Recent Administrative Activity</p>
                    <h3 className="mt-1 text-lg font-semibold text-slate-950">Audit timeline</h3>
                </div>
                <Clock3 className="h-4 w-4 text-slate-500" />
            </div>
            <div className="mt-4 space-y-3">
                {activity.length === 0 ? (
                    <div className="rounded-md border border-dashed border-slate-300 bg-slate-50 p-4 text-sm text-slate-600">
                        No administrative events recorded.
                    </div>
                ) : activity.map((item) => (
                    <article key={item.activityId} className="rounded-md border border-slate-200 bg-slate-50 p-3">
                        <div className="flex flex-wrap items-center justify-between gap-2">
                            <p className="font-semibold text-slate-950">{item.displayName}</p>
                            <span className={item.success ? 'text-xs font-semibold text-emerald-700' : 'text-xs font-semibold text-rose-700'}>
                                {item.success ? 'Success' : 'Failed'}
                            </span>
                        </div>
                        <p className="mt-1 text-sm text-slate-600">{item.description}</p>
                        <p className="mt-2 text-xs font-medium text-slate-500">
                            {[item.providerCode || 'All providers', item.actor || 'System', formatDate(item.occurredAtUtc)].join(' / ')}
                        </p>
                    </article>
                ))}
            </div>
        </section>
    )
}

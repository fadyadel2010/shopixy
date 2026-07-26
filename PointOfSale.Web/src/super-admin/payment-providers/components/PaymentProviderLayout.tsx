import type { ReactNode } from 'react'

export function PaymentProviderLayout({
    header,
    list,
    details,
}: {
    header: ReactNode
    list: ReactNode
    details: ReactNode
}) {
    return (
        <div className="space-y-6">
            {header}
            <div className="grid gap-6 xl:grid-cols-[0.38fr_0.62fr]">
                <aside className="space-y-3">{list}</aside>
                <section className="space-y-4">{details}</section>
            </div>
        </div>
    )
}

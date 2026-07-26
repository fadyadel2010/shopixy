import { useMemo, useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { ArrowRight, LockKeyhole, ShieldCheck } from 'lucide-react'
import { getSuperAdminSession, loginSuperAdmin } from './superAdminAuthService'
import { useSuperAdminAuthStore } from './superAdminAuthStore'
import { useSuperAdminSessionBootstrap } from './useSuperAdminSessionBootstrap'

function resolveRedirect(search: string): string {
    const params = new URLSearchParams(search)
    const redirect = params.get('redirect')

    if (!redirect || !redirect.startsWith('/') || redirect.startsWith('//') || redirect.includes('\\')) {
        return '/super-admin/finance'
    }

    try {
        const url = new URL(redirect, window.location.origin)
        if (
            url.origin !== window.location.origin ||
            !url.pathname.startsWith('/super-admin') ||
            url.pathname.startsWith('/super-admin/login')
        ) {
            return '/super-admin/finance'
        }

        return `${url.pathname}${url.search}${url.hash}`
    } catch {
        return '/super-admin/finance'
    }
}

export default function SuperAdminLoginPage() {
    const location = useLocation()
    const navigate = useNavigate()
    const { user, isAuthenticated, setSession } = useSuperAdminAuthStore()
    const hasHydrated = useSuperAdminSessionBootstrap()
    const redirectTarget = useMemo(() => resolveRedirect(location.search), [location.search])

    const [email, setEmail] = useState('')
    const [password, setPassword] = useState('')
    const [error, setError] = useState('')
    const [isSubmitting, setIsSubmitting] = useState(false)

    const hasSession =
        hasHydrated &&
        isAuthenticated &&
        user?.role === 'SuperAdmin' &&
        user?.authType === 'super_admin'

    if (!hasHydrated) {
        return (
            <div className="flex min-h-screen items-center justify-center bg-slate-950 text-sm text-slate-300">
                Checking secure Super Admin session...
            </div>
        )
    }

    if (hasSession) {
        return <Navigate to={redirectTarget} replace />
    }

    async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault()

        setError('')
        setIsSubmitting(true)

        try {
            await loginSuperAdmin({
                email,
                password,
            })

            const session = await getSuperAdminSession()

            setSession(session)
            navigate(redirectTarget, { replace: true })
        } catch (submissionError) {
            const message =
                submissionError instanceof Error
                    ? submissionError.message
                    : 'Unable to sign in right now.'

            setError(message)
        } finally {
            setIsSubmitting(false)
        }
    }

    return (
        <div className="relative min-h-screen overflow-hidden bg-slate-950 text-white">
            <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,_rgba(16,185,129,0.18),_transparent_30%),radial-gradient(circle_at_bottom_right,_rgba(251,191,36,0.16),_transparent_30%)]" />
            <div className="relative mx-auto flex min-h-screen max-w-7xl items-center px-6 py-12 lg:px-10">
                <div className="grid w-full gap-10 lg:grid-cols-[1.1fr_0.9fr]">
                    <section className="hidden rounded-[32px] border border-white/10 bg-white/5 p-10 shadow-2xl backdrop-blur lg:block">
                        <div className="inline-flex items-center gap-2 rounded-full border border-emerald-400/30 bg-emerald-400/10 px-4 py-2 text-sm font-semibold text-emerald-200">
                            <ShieldCheck className="h-4 w-4" />
                            Platform-wide owner access
                        </div>

                        <h1 className="mt-8 max-w-xl text-5xl font-bold leading-tight text-white">
                            CommerceHub Finance Command
                        </h1>
                        <p className="mt-5 max-w-xl text-lg leading-8 text-slate-300">
                            Monitor payments, earnings, payouts, and reconciliation health from one secure platform view. This console is isolated from tenant administration.
                        </p>

                        <div className="mt-10 grid gap-4 sm:grid-cols-3">
                            {[
                                'Platform-wide financial visibility',
                                'Request-level observability and audit trails',
                                'Cookie-backed session with CSRF protection',
                            ].map((item) => (
                                <div
                                    key={item}
                                    className="rounded-2xl border border-white/10 bg-slate-900/60 p-4 text-sm text-slate-200"
                                >
                                    {item}
                                </div>
                            ))}
                        </div>
                    </section>

                    <section className="rounded-[32px] border border-slate-200 bg-white p-8 text-slate-900 shadow-[0_32px_80px_-40px_rgba(15,23,42,0.75)] sm:p-10">
                        <div className="mx-auto max-w-md">
                            <div className="flex items-center gap-3">
                                <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-900 text-white shadow-lg">
                                    <LockKeyhole className="h-6 w-6" />
                                </div>
                                <div>
                                    <p className="text-sm font-semibold uppercase tracking-[0.24em] text-emerald-600">
                                        Super Admin
                                    </p>
                                    <h2 className="text-3xl font-bold text-slate-950">
                                        Sign in
                                    </h2>
                                </div>
                            </div>

                            <p className="mt-6 text-sm leading-6 text-slate-500">
                                Use your isolated Super Admin credentials to access the platform finance dashboard.
                            </p>

                            {error ? (
                                <div className="mt-6 rounded-2xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
                                    {error}
                                </div>
                            ) : null}

                            <form className="mt-8 space-y-5" onSubmit={handleSubmit}>
                                <label className="block">
                                    <span className="mb-2 block text-sm font-medium text-slate-700">
                                        Email
                                    </span>
                                    <input
                                        autoComplete="email"
                                        className="w-full rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-base outline-none transition focus:border-emerald-500 focus:bg-white focus:ring-4 focus:ring-emerald-500/10"
                                        onChange={(event) => setEmail(event.target.value)}
                                        placeholder="owner@commercehub.com"
                                        required
                                        type="email"
                                        value={email}
                                    />
                                </label>

                                <label className="block">
                                    <span className="mb-2 block text-sm font-medium text-slate-700">
                                        Password
                                    </span>
                                    <input
                                        autoComplete="current-password"
                                        className="w-full rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-base outline-none transition focus:border-emerald-500 focus:bg-white focus:ring-4 focus:ring-emerald-500/10"
                                        onChange={(event) => setPassword(event.target.value)}
                                        placeholder="Enter your secure password"
                                        required
                                        type="password"
                                        value={password}
                                    />
                                </label>

                                <button
                                    className="inline-flex w-full items-center justify-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
                                    disabled={isSubmitting}
                                    type="submit"
                                >
                                    {isSubmitting ? 'Signing in...' : 'Enter Super Admin Console'}
                                    <ArrowRight className="h-4 w-4" />
                                </button>
                            </form>
                        </div>
                    </section>
                </div>
            </div>
        </div>
    )
}

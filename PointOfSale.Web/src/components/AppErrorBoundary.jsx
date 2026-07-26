import { Component } from 'react'
import { trackException } from '../services/telemetry'

export default class AppErrorBoundary extends Component {
    constructor(props) {
        super(props)
        this.state = { hasError: false }
    }

    static getDerivedStateFromError() {
        return { hasError: true }
    }

    componentDidCatch(error, errorInfo) {
        // Keep console.error for local development
        console.error('Unhandled application error', error, errorInfo)
        
        // Track exception with telemetry service
        trackException(error, {
            componentStack: errorInfo.componentStack,
            errorBoundary: 'AppErrorBoundary',
            errorInfo: errorInfo,
        })
    }

    handleReload = () => {
        window.location.reload()
    }

    handleRetry = () => {
        this.setState({ hasError: false })
    }

    render() {
        if (!this.state.hasError) {
            return this.props.children
        }

        return (
            <div className="min-h-screen bg-slate-100 px-6 py-16">
                <div className="mx-auto max-w-lg rounded-2xl border border-slate-200 bg-white p-8 shadow-sm">
                    <p className="text-sm font-semibold uppercase tracking-[0.2em] text-slate-500">
                        Application Error
                    </p>
                    <h1 className="mt-3 text-2xl font-bold text-slate-900">
                        Something went wrong
                    </h1>
                    <p className="mt-3 text-sm leading-6 text-slate-600">
                        The page hit an unexpected error. Reload the app to recover safely.
                    </p>
                    <div className="mt-6 flex gap-3">
                        <button
                            type="button"
                            onClick={this.handleRetry}
                            className="rounded-lg bg-slate-900 px-4 py-2 text-sm font-medium text-white transition hover:bg-slate-700"
                        >
                            Try again
                        </button>
                        <button
                            type="button"
                            onClick={this.handleReload}
                            className="rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-50"
                        >
                            Reload application
                        </button>
                    </div>
                </div>
            </div>
        )
    }
}

import { Component, type ErrorInfo, type ReactNode } from 'react';
import { SectionErrorFallback } from './SectionErrorFallback';

interface SectionErrorBoundaryProps {
    children: ReactNode;
    sectionType: string;
    resetKey?: string;
}

interface SectionErrorBoundaryState {
    hasError: boolean;
}

export class SectionErrorBoundary extends Component<
    SectionErrorBoundaryProps,
    SectionErrorBoundaryState
> {
    public state: SectionErrorBoundaryState = {
        hasError: false,
    };

    public static getDerivedStateFromError(): SectionErrorBoundaryState {
        return { hasError: true };
    }

    public componentDidCatch(error: Error, errorInfo: ErrorInfo) {
        console.error('Section crashed:', error, errorInfo);

        if (import.meta.env.DEV) {
            console.error('Section error:', this.props.sectionType);
        }
    }

    public componentDidUpdate(prevProps: SectionErrorBoundaryProps) {
        if (this.state.hasError && prevProps.resetKey !== this.props.resetKey) {
            this.setState({ hasError: false });
        }
    }

    public render() {
        if (this.state.hasError) {
            return <SectionErrorFallback />;
        }

        return this.props.children;
    }
}

export default SectionErrorBoundary;

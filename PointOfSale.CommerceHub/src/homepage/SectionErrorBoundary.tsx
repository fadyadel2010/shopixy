import { Component, type ErrorInfo, type ReactNode } from "react";

type SectionErrorBoundaryProps = {
  children: ReactNode;
  sectionId: string;
  sectionType: string;
};

type SectionErrorBoundaryState = {
  hasError: boolean;
};

export class SectionErrorBoundary extends Component<
  SectionErrorBoundaryProps,
  SectionErrorBoundaryState
> {
  public state: SectionErrorBoundaryState = {
    hasError: false
  };

  public static getDerivedStateFromError(): SectionErrorBoundaryState {
    return { hasError: true };
  }

  public componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error("[Homepage] Section render failed.", {
      sectionId: this.props.sectionId,
      sectionType: this.props.sectionType,
      error,
      errorInfo
    });
  }

  public render() {
    if (this.state.hasError) {
      if (import.meta.env.DEV) {
        return (
          <div className="rounded-3xl border border-amber-300 bg-amber-50 px-4 py-5 text-sm text-amber-700">
            Skipped broken section: {this.props.sectionType}
          </div>
        );
      }

      return null;
    }

    return this.props.children;
  }
}

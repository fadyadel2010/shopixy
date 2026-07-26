/**
 * Order Status Timeline Component
 * 
 * Visual timeline showing order progress through stages.
 * Accessible, keyboard-navigable, and screen-reader friendly.
 * 
 * Phase 7.8: Order Confirmation & Tracking UI
 */

import React from 'react';
import type { TimelineStep } from '@/types/order';

// ============================================================================
// Types
// ============================================================================

interface OrderStatusTimelineProps {
    steps: TimelineStep[];
    className?: string;
}

// ============================================================================
// Status Icon Component
// ============================================================================

interface StatusIconProps {
    step: TimelineStep;
    isLast: boolean;
}

const StatusIcon: React.FC<StatusIconProps> = ({ step }) => {
    const baseClasses = 'w-12 h-12 rounded-full flex items-center justify-center text-xl transition-all duration-300';

    let statusClasses: string;
    switch (step.status) {
        case 'completed':
            statusClasses = 'bg-green-100 text-green-600 border-2 border-green-300';
            break;
        case 'current':
            statusClasses = 'bg-primary/10 text-primary border-2 border-primary shadow-lg shadow-primary/20';
            break;
        case 'upcoming':
        default:
            statusClasses = 'bg-muted/5 text-muted/50 border-2 border-muted/20';
            break;
    }

    return (
        <div className={`${baseClasses} ${statusClasses}`}>
            {step.status === 'completed' ? (
                <svg
                    className="w-6 h-6"
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                    aria-hidden="true"
                >
                    <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        strokeWidth={2.5}
                        d="M5 13l4 4L19 7"
                    />
                </svg>
            ) : (
                <span aria-hidden="true">{step.icon}</span>
            )}
        </div>
    );
};

// ============================================================================
// Timeline Connector
// ============================================================================

interface TimelineConnectorProps {
    isCompleted: boolean;
}

const TimelineConnector: React.FC<TimelineConnectorProps> = ({ isCompleted }) => {
    return (
        <div
            className={`
                flex-1 h-1 mx-2 rounded-full transition-all duration-300
                ${isCompleted ? 'bg-green-300' : 'bg-muted/20'}
            `}
            aria-hidden="true"
        />
    );
};

// ============================================================================
// Mobile Timeline Step
// ============================================================================

interface MobileTimelineStepProps {
    step: TimelineStep;
    isLast: boolean;
}

const MobileTimelineStep: React.FC<MobileTimelineStepProps> = ({ step, isLast }) => {
    return (
        <li
            className="relative pb-8 last:pb-0"
            aria-current={step.status === 'current' ? 'step' : undefined}
        >
            {/* Vertical connector line */}
            {!isLast && (
                <div
                    className={`
                        absolute top-12 left-6 w-0.5 h-full -ml-px
                        ${step.status === 'completed' ? 'bg-green-300' : 'bg-muted/20'}
                    `}
                    aria-hidden="true"
                />
            )}

            <div className="relative flex items-start gap-4">
                <StatusIcon step={step} isLast={isLast} />

                <div className="flex-1 min-w-0 pt-2">
                    <p
                        className={`
                            font-semibold text-base
                            ${step.status === 'completed' ? 'text-green-700' : ''}
                            ${step.status === 'current' ? 'text-primary' : ''}
                            ${step.status === 'upcoming' ? 'text-muted/60' : ''}
                        `}
                    >
                        {step.label}
                        {step.status === 'current' && (
                            <span className="sr-only"> (Current step)</span>
                        )}
                        {step.status === 'completed' && (
                            <span className="sr-only"> (Completed)</span>
                        )}
                    </p>
                    {step.description && (
                        <p
                            className={`
                                text-sm mt-0.5
                                ${step.status === 'upcoming' ? 'text-muted/40' : 'text-muted'}
                            `}
                        >
                            {step.description}
                        </p>
                    )}
                    {step.timestamp && (
                        <p className="text-xs text-muted mt-1">
                            {new Date(step.timestamp).toLocaleString()}
                        </p>
                    )}
                </div>
            </div>
        </li>
    );
};

// ============================================================================
// Desktop Timeline Step
// ============================================================================

interface DesktopTimelineStepProps {
    step: TimelineStep;
    isLast: boolean;
}

const DesktopTimelineStep: React.FC<DesktopTimelineStepProps> = ({ step, isLast }) => {
    return (
        <>
            <div
                className="flex flex-col items-center"
                role="listitem"
                aria-current={step.status === 'current' ? 'step' : undefined}
            >
                <StatusIcon step={step} isLast={isLast} />

                <div className="text-center mt-3 max-w-[100px]">
                    <p
                        className={`
                            text-sm font-semibold leading-tight
                            ${step.status === 'completed' ? 'text-green-700' : ''}
                            ${step.status === 'current' ? 'text-primary' : ''}
                            ${step.status === 'upcoming' ? 'text-muted/60' : ''}
                        `}
                    >
                        {step.label}
                    </p>
                    {step.status === 'current' && step.description && (
                        <p className="text-xs text-muted mt-1 hidden lg:block">
                            {step.description}
                        </p>
                    )}
                </div>
            </div>

            {!isLast && (
                <TimelineConnector isCompleted={step.status === 'completed'} />
            )}
        </>
    );
};

// ============================================================================
// Main Component
// ============================================================================

export const OrderStatusTimeline: React.FC<OrderStatusTimelineProps> = ({
    steps,
    className = '',
}) => {
    if (!steps || steps.length === 0) {
        return null;
    }

    // Get current step for aria-live announcement
    const currentStep = steps.find(s => s.status === 'current');

    return (
        <div className={className}>
            {/* Screen reader announcement */}
            <div
                className="sr-only"
                role="status"
                aria-live="polite"
                aria-atomic="true"
            >
                {currentStep && (
                    <>
                        Current order status: {currentStep.label}.
                        {currentStep.description}
                    </>
                )}
            </div>

            {/* Desktop Timeline - Horizontal */}
            <nav
                aria-label="Order progress"
                className="hidden md:block"
            >
                <div
                    className="flex items-start justify-between"
                    role="list"
                >
                    {steps.map((step, index) => (
                        <DesktopTimelineStep
                            key={step.id}
                            step={step}
                            isLast={index === steps.length - 1}
                        />
                    ))}
                </div>
            </nav>

            {/* Mobile Timeline - Vertical */}
            <nav
                aria-label="Order progress"
                className="md:hidden"
            >
                <ol className="relative">
                    {steps.map((step, index) => (
                        <MobileTimelineStep
                            key={step.id}
                            step={step}
                            isLast={index === steps.length - 1}
                        />
                    ))}
                </ol>
            </nav>
        </div>
    );
};

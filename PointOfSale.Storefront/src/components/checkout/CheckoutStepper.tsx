/**
 * Checkout Stepper Component
 * 
 * Visual step indicator for checkout flow.
 * Shows: Shipping → Payment → Review
 * 
 * Phase 7.7: Checkout UI Implementation
 */

import React from 'react';
import type { CheckoutStep } from '@/types/checkout';

interface CheckoutStepperProps {
    currentStep: CheckoutStep;
    onStepClick?: (step: CheckoutStep) => void;
    /** Whether navigation is allowed (e.g., disabled during submission) */
    disabled?: boolean;
}

interface StepConfig {
    id: CheckoutStep;
    label: string;
    number: number;
}

const STEPS: StepConfig[] = [
    { id: 'shipping', label: 'Shipping', number: 1 },
    { id: 'payment', label: 'Payment', number: 2 },
    { id: 'review', label: 'Review', number: 3 },
];

/**
 * Get step status
 */
function getStepStatus(
    step: CheckoutStep,
    currentStep: CheckoutStep
): 'completed' | 'current' | 'upcoming' {
    const currentIndex = STEPS.findIndex(s => s.id === currentStep);
    const stepIndex = STEPS.findIndex(s => s.id === step);

    if (stepIndex < currentIndex) return 'completed';
    if (stepIndex === currentIndex) return 'current';
    return 'upcoming';
}

/**
 * Checkout Stepper Component
 */
export const CheckoutStepper: React.FC<CheckoutStepperProps> = ({
    currentStep,
    onStepClick,
    disabled = false,
}) => {
    return (
        <nav aria-label="Checkout steps" className="mb-8">
            <ol className="flex items-center justify-center gap-2 sm:gap-4">
                {STEPS.map((step, index) => {
                    const status = getStepStatus(step.id, currentStep);
                    const isClickable = !disabled && status === 'completed' && onStepClick;

                    return (
                        <li key={step.id} className="flex items-center">
                            {/* Step indicator */}
                            <button
                                onClick={() => isClickable && onStepClick(step.id)}
                                disabled={!isClickable}
                                className={`
                                    flex items-center gap-2 sm:gap-3
                                    px-3 py-2 sm:px-4 sm:py-2.5
                                    rounded-lg
                                    transition-all duration-200
                                    focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                                    ${status === 'current'
                                        ? 'bg-primary text-white shadow-md'
                                        : status === 'completed'
                                            ? 'bg-primary/10 text-primary hover:bg-primary/20 cursor-pointer'
                                            : 'bg-muted/10 text-muted cursor-not-allowed'
                                    }
                                    ${!isClickable && status !== 'current' ? 'cursor-not-allowed' : ''}
                                `}
                                aria-current={status === 'current' ? 'step' : undefined}
                                aria-disabled={!isClickable}
                            >
                                {/* Step number / Check icon */}
                                <span
                                    className={`
                                        w-6 h-6 sm:w-7 sm:h-7
                                        flex items-center justify-center
                                        rounded-full
                                        text-xs sm:text-sm font-semibold
                                        ${status === 'current'
                                            ? 'bg-white/20'
                                            : status === 'completed'
                                                ? 'bg-primary text-white'
                                                : 'bg-muted/20'
                                        }
                                    `}
                                >
                                    {status === 'completed' ? (
                                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                                        </svg>
                                    ) : (
                                        step.number
                                    )}
                                </span>

                                {/* Step label */}
                                <span className="hidden sm:inline font-medium text-sm">
                                    {step.label}
                                </span>
                            </button>

                            {/* Connector line */}
                            {index < STEPS.length - 1 && (
                                <div
                                    className={`
                                        w-8 sm:w-12 h-0.5 mx-2
                                        ${status === 'completed' ? 'bg-primary' : 'bg-muted/20'}
                                    `}
                                    aria-hidden="true"
                                />
                            )}
                        </li>
                    );
                })}
            </ol>
        </nav>
    );
};

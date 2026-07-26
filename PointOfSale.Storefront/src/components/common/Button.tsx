/**
 * Button Component
 * 
 * Config-driven button with solid/outline variants.
 * Respects storeConfig.ui.buttonStyle for default styling.
 * Uses CSS variables for all theming.
 */

import React from 'react';
import { useStoreConfigStore, selectTheme } from '@/store';

type ButtonVariant = 'primary' | 'secondary' | 'ghost';
type ButtonSize = 'sm' | 'md' | 'lg';

interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
    variant?: ButtonVariant;
    size?: ButtonSize;
    /** Override the default button style from StoreConfig */
    styleOverride?: 'solid' | 'outline';
    /** Full width button */
    fullWidth?: boolean;
    /** Loading state */
    loading?: boolean;
    /** Left icon */
    leftIcon?: React.ReactNode;
    /** Right icon */
    rightIcon?: React.ReactNode;
    children: React.ReactNode;
}

/**
 * Size class mappings
 */
const SIZE_CLASSES: Record<ButtonSize, string> = {
    sm: 'px-3 py-1.5 text-sm gap-1.5',
    md: 'px-5 py-2.5 text-base gap-2',
    lg: 'px-7 py-3.5 text-lg gap-2.5',
};

/**
 * Get variant classes based on button style (solid/outline)
 */
const getVariantClasses = (variant: ButtonVariant, buttonStyle: 'solid' | 'outline'): string => {
    const variants: Record<ButtonVariant, Record<'solid' | 'outline', string>> = {
        primary: {
            solid: 'bg-primary text-white hover:bg-primary/90 focus:ring-primary/50',
            outline: 'border-2 border-primary text-primary hover:bg-primary hover:text-white focus:ring-primary/50',
        },
        secondary: {
            solid: 'bg-secondary text-white hover:bg-secondary/90 focus:ring-secondary/50',
            outline: 'border-2 border-secondary text-secondary hover:bg-secondary hover:text-white focus:ring-secondary/50',
        },
        ghost: {
            solid: 'bg-transparent text-text hover:bg-muted/10 focus:ring-muted/30',
            outline: 'bg-transparent text-text hover:bg-muted/10 focus:ring-muted/30',
        },
    };

    return variants[variant][buttonStyle];
};

/**
 * Button Component
 */
export const Button: React.FC<ButtonProps> = ({
    variant = 'primary',
    size = 'md',
    styleOverride,
    fullWidth = false,
    loading = false,
    leftIcon,
    rightIcon,
    disabled,
    className = '',
    children,
    ...props
}) => {
    const theme = useStoreConfigStore(selectTheme);
    const buttonStyle = styleOverride || theme.ui.buttonStyle;

    const baseClasses = [
        // Base styles
        'inline-flex items-center justify-center',
        'font-medium',
        'rounded-md',
        'transition-all duration-200',
        'focus:outline-none focus:ring-2 focus:ring-offset-2',
        // Disabled state
        'disabled:opacity-50 disabled:cursor-not-allowed disabled:pointer-events-none',
        // Size
        SIZE_CLASSES[size],
        // Variant
        getVariantClasses(variant, buttonStyle),
        // Full width
        fullWidth ? 'w-full' : '',
        // Custom classes
        className,
    ].filter(Boolean).join(' ');

    return (
        <button
            className={baseClasses}
            disabled={disabled || loading}
            {...props}
        >
            {/* Loading Spinner */}
            {loading && (
                <svg
                    className="animate-spin h-4 w-4 mr-2"
                    fill="none"
                    viewBox="0 0 24 24"
                >
                    <circle
                        className="opacity-25"
                        cx="12"
                        cy="12"
                        r="10"
                        stroke="currentColor"
                        strokeWidth="4"
                    />
                    <path
                        className="opacity-75"
                        fill="currentColor"
                        d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
                    />
                </svg>
            )}

            {/* Left Icon */}
            {!loading && leftIcon && (
                <span className="flex-shrink-0">{leftIcon}</span>
            )}

            {/* Content */}
            <span>{children}</span>

            {/* Right Icon */}
            {rightIcon && (
                <span className="flex-shrink-0">{rightIcon}</span>
            )}
        </button>
    );
};

/**
 * Icon Button - Circular button for icons only
 */
interface IconButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
    variant?: ButtonVariant;
    size?: ButtonSize;
    styleOverride?: 'solid' | 'outline';
    /** Accessible label for screen readers */
    'aria-label': string;
    children: React.ReactNode;
}

const ICON_SIZE_CLASSES: Record<ButtonSize, string> = {
    sm: 'w-8 h-8',
    md: 'w-10 h-10',
    lg: 'w-12 h-12',
};

export const IconButton: React.FC<IconButtonProps> = ({
    variant = 'ghost',
    size = 'md',
    styleOverride,
    className = '',
    children,
    ...props
}) => {
    const theme = useStoreConfigStore(selectTheme);
    const buttonStyle = styleOverride || theme.ui.buttonStyle;

    const baseClasses = [
        'inline-flex items-center justify-center',
        'rounded-full',
        'transition-all duration-200',
        'focus:outline-none focus:ring-2 focus:ring-offset-2',
        'disabled:opacity-50 disabled:cursor-not-allowed',
        ICON_SIZE_CLASSES[size],
        getVariantClasses(variant, buttonStyle),
        className,
    ].filter(Boolean).join(' ');

    return (
        <button className={baseClasses} {...props}>
            {children}
        </button>
    );
};

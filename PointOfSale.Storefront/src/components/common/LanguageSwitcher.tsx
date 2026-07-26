/**
 * Language Switcher Component
 * Phase 10.1: Multi-language Support
 * 
 * A toggle button to switch between English and Arabic.
 * Supports multiple variants for different contexts.
 */

import React from 'react';
import { useTranslation } from '@/i18n';

interface LanguageSwitcherProps {
    /** Visual variant */
    variant?: 'icon' | 'text' | 'full';
    /** Additional CSS classes */
    className?: string;
    /** Size variant */
    size?: 'sm' | 'md' | 'lg';
}

/**
 * Language Switcher - toggles between EN/AR
 */
export const LanguageSwitcher: React.FC<LanguageSwitcherProps> = ({
    variant = 'text',
    className = '',
    size = 'md',
}) => {
    const { language, toggleLanguage, setLanguage } = useTranslation();

    const sizeClasses = {
        sm: 'text-xs px-2 py-1',
        md: 'text-sm px-3 py-1.5',
        lg: 'text-base px-4 py-2',
    };

    const baseClasses = `
        inline-flex items-center justify-center gap-2
        rounded-lg font-medium transition-all duration-200
        focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
        hover:bg-white/10
        ${sizeClasses[size]}
        ${className}
    `.trim();

    // Icon-only variant
    if (variant === 'icon') {
        return (
            <button
                onClick={toggleLanguage}
                className={`${baseClasses} min-w-[44px] min-h-[44px]`}
                aria-label={`Switch to ${language === 'en' ? 'Arabic' : 'English'}`}
                title={language === 'en' ? 'العربية' : 'English'}
            >
                <GlobeIcon className="w-5 h-5" />
            </button>
        );
    }

    // Text-only variant (shows target language)
    if (variant === 'text') {
        return (
            <button
                onClick={toggleLanguage}
                className={baseClasses}
                aria-label={`Switch to ${language === 'en' ? 'Arabic' : 'English'}`}
            >
                <GlobeIcon className="w-4 h-4 opacity-70" />
                <span>{language === 'en' ? 'العربية' : 'EN'}</span>
            </button>
        );
    }

    // Full variant - dropdown with both options
    return (
        <div className="relative group">
            <button
                className={`${baseClasses} group`}
                aria-haspopup="listbox"
                aria-expanded="false"
            >
                <GlobeIcon className="w-4 h-4 opacity-70" />
                <span>{language === 'en' ? 'English' : 'العربية'}</span>
                <ChevronDownIcon className="w-3 h-3 opacity-50 group-hover:opacity-100 transition-opacity" />
            </button>

            <div className="absolute top-full right-0 mt-1 py-1 bg-background border border-muted/20 rounded-lg shadow-xl opacity-0 invisible group-hover:opacity-100 group-hover:visible transition-all duration-200 min-w-[120px] z-50">
                <LanguageOption
                    label="English"
                    isActive={language === 'en'}
                    onClick={() => setLanguage('en')}
                />
                <LanguageOption
                    label="العربية"
                    isActive={language === 'ar'}
                    onClick={() => setLanguage('ar')}
                />
            </div>
        </div>
    );
};

// ============================================================================
// Sub-components
// ============================================================================

interface LanguageOptionProps {
    label: string;
    isActive: boolean;
    onClick: () => void;
}

const LanguageOption: React.FC<LanguageOptionProps> = ({
    label,
    isActive,
    onClick,
}) => {
    return (
        <button
            onClick={onClick}
            className={`
                w-full px-4 py-2 text-left text-sm transition-colors
                ${isActive
                    ? 'bg-primary/10 text-primary font-medium'
                    : 'text-text hover:bg-muted/10'
                }
            `}
            role="option"
            aria-selected={isActive}
        >
            <span className="flex items-center gap-2">
                {isActive && (
                    <CheckIcon className="w-4 h-4" />
                )}
                <span className={isActive ? '' : 'ml-6'}>{label}</span>
            </span>
        </button>
    );
};

// ============================================================================
// Icons
// ============================================================================

const GlobeIcon: React.FC<{ className?: string }> = ({ className }) => (
    <svg
        className={className}
        fill="none"
        stroke="currentColor"
        viewBox="0 0 24 24"
        aria-hidden="true"
    >
        <path
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth={1.5}
            d="M21 12a9 9 0 01-9 9m9-9a9 9 0 00-9-9m9 9H3m9 9a9 9 0 01-9-9m9 9c1.657 0 3-4.03 3-9s-1.343-9-3-9m0 18c-1.657 0-3-4.03-3-9s1.343-9 3-9m-9 9a9 9 0 019-9"
        />
    </svg>
);

const ChevronDownIcon: React.FC<{ className?: string }> = ({ className }) => (
    <svg
        className={className}
        fill="none"
        stroke="currentColor"
        viewBox="0 0 24 24"
        aria-hidden="true"
    >
        <path
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth={2}
            d="M19 9l-7 7-7-7"
        />
    </svg>
);

const CheckIcon: React.FC<{ className?: string }> = ({ className }) => (
    <svg
        className={className}
        fill="none"
        stroke="currentColor"
        viewBox="0 0 24 24"
        aria-hidden="true"
    >
        <path
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth={2}
            d="M5 13l4 4L19 7"
        />
    </svg>
);

export default LanguageSwitcher;

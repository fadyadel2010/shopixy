import React from 'react';

interface FooterSocialIconProps {
    platform: string;
    className?: string;
}

export const FooterSocialIcon: React.FC<FooterSocialIconProps> = ({ platform, className = 'h-5 w-5' }) => {
    const normalized = platform.trim().toLowerCase();

    switch (normalized) {
        case 'facebook':
            return (
                <svg className={className} fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                    <path d="M24 12.073C24 5.404 18.627 0 12 0S0 5.404 0 12.073c0 6.019 4.388 11.01 10.125 11.927v-8.438H7.078v-3.49h3.047V9.41c0-3.017 1.792-4.685 4.533-4.685 1.313 0 2.686.236 2.686.236v2.964h-1.514c-1.49 0-1.956.93-1.956 1.885v2.262h3.328l-.532 3.49h-2.796V24C19.612 23.083 24 18.092 24 12.073Z" />
                </svg>
            );
        case 'instagram':
            return (
                <svg className={className} fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                    <path d="M12 2.163c3.204 0 3.584.012 4.85.07 3.251.148 4.77 1.69 4.918 4.918.058 1.265.069 1.645.069 4.849 0 3.204-.012 3.584-.069 4.849-.149 3.227-1.664 4.771-4.919 4.919-1.266.058-1.645.07-4.849.07-3.204 0-3.583-.012-4.849-.07-3.259-.149-4.771-1.699-4.919-4.92C2.175 15.583 2.163 15.204 2.163 12c0-3.204.013-3.583.07-4.849.149-3.227 1.664-4.77 4.919-4.919C8.417 2.175 8.796 2.163 12 2.163Zm0-2.163C8.741 0 8.333.014 7.052.072 2.695.272.273 2.69.073 7.052.014 8.333 0 8.741 0 12c0 3.259.014 3.668.072 4.948.2 4.359 2.618 6.78 6.98 6.98C8.333 23.986 8.741 24 12 24c3.259 0 3.668-.014 4.948-.072 4.354-.2 6.782-2.618 6.98-6.98C23.986 15.668 24 15.259 24 12c0-3.259-.014-3.667-.072-4.948-.196-4.354-2.617-6.78-6.979-6.98C15.668.014 15.259 0 12 0Zm0 5.838A6.162 6.162 0 1 0 12 18.162a6.162 6.162 0 0 0 0-12.324Zm0 10.162A4 4 0 1 1 12 8a4 4 0 0 1 0 8Zm6.406-11.845a1.44 1.44 0 1 0 0 2.881 1.44 1.44 0 0 0 0-2.881Z" />
                </svg>
            );
        case 'twitter':
        case 'x':
            return (
                <svg className={className} fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                    <path d="M18.901 1.154h3.68l-8.043 9.193L24 22.847h-7.406l-5.8-7.584-6.638 7.584H.474l8.603-9.832L0 1.154h7.594l5.243 6.932 6.064-6.932Zm-1.291 19.49h2.039L6.486 3.244H4.298L17.61 20.644Z" />
                </svg>
            );
        case 'tiktok':
            return (
                <svg className={className} fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                    <path d="M19.589 6.686a4.793 4.793 0 0 1-3.77-4.718h-3.17v12.702a2.896 2.896 0 1 1-2.896-2.896c.357 0 .699.066 1.015.186V8.734a6.07 6.07 0 0 0-1.015-.086A6.066 6.066 0 1 0 15.82 14.71V8.244a7.958 7.958 0 0 0 4.678 1.515V6.686h-.909Z" />
                </svg>
            );
        case 'youtube':
            return (
                <svg className={className} fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                    <path d="M23.499 6.203a2.96 2.96 0 0 0-2.08-2.095C19.57 3.6 12 3.6 12 3.6s-7.57 0-9.418.508a2.96 2.96 0 0 0-2.08 2.095C0 8.074 0 12 0 12s0 3.926.502 5.797a2.96 2.96 0 0 0 2.08 2.095C4.43 20.4 12 20.4 12 20.4s7.57 0 9.419-.508a2.96 2.96 0 0 0 2.08-2.095C24 15.926 24 12 24 12s0-3.926-.501-5.797ZM9.6 15.6V8.4l6.24 3.6L9.6 15.6Z" />
                </svg>
            );
        case 'linkedin':
            return (
                <svg className={className} fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                    <path d="M20.447 20.452H16.89V14.87c0-1.332-.026-3.045-1.857-3.045-1.859 0-2.143 1.45-2.143 2.949v5.678H9.333V9h3.414v1.561h.049c.476-.9 1.637-1.85 3.369-1.85 3.603 0 4.268 2.371 4.268 5.455v6.286ZM5.337 7.433a2.063 2.063 0 1 1 0-4.127 2.063 2.063 0 0 1 0 4.127ZM7.119 20.452H3.554V9h3.565v11.452Z" />
                </svg>
            );
        default:
            return (
                <span className={`${className} inline-flex items-center justify-center rounded-full bg-current/10 text-[0.65rem] font-semibold uppercase`}>
                    {normalized.slice(0, 1) || '?'}
                </span>
            );
    }
};

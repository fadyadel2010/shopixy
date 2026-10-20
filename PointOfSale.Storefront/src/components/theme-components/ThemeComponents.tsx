import React from 'react';
import { MediaFrame } from '@/components/media';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import {
    normalizeThemeComponentTheme,
    type ThemeComponentAction,
    type ThemeComponentAlign,
    type ThemeComponentTheme,
    type ThemePricePresentation,
} from './themeComponentTypes';

function cx(...classes: Array<string | false | null | undefined>): string {
    return classes.filter(Boolean).join(' ');
}

const surfaceStyles: Record<ThemeComponentTheme, string> = {
    modern: 'overflow-hidden rounded-md border border-muted/15 bg-background/95 shadow-sm backdrop-blur transition-all duration-normal motion-safe:hover:-translate-y-1 hover:border-primary/30 hover:shadow-xl',
    minimal: 'border-b border-muted/20 bg-background transition-colors duration-fast hover:border-text/40',
    fashion: 'overflow-hidden bg-background transition-transform duration-slow motion-safe:hover:-translate-y-1',
    pearl: 'border border-secondary/30 bg-background shadow-[0_18px_40px_rgba(15,23,42,0.06)] transition-all duration-slow hover:border-primary/60',
    phoenix: 'overflow-hidden border border-black/10 bg-background shadow-sm transition-all duration-normal motion-safe:hover:-translate-y-1 hover:border-primary/50',
    hype: 'overflow-hidden rounded-lg border border-muted/20 bg-background shadow-sm ring-1 ring-muted/5 transition-all duration-fast hover:border-primary/50 hover:shadow-md',
    electronics: 'overflow-hidden rounded-md border border-muted/25 bg-background shadow-sm ring-1 ring-muted/5 transition-all duration-fast hover:border-primary/50 hover:shadow-md',
    alizon: 'overflow-hidden border border-muted/20 bg-background shadow-sm transition-all duration-fast hover:border-primary/50 hover:shadow-md',
    sharp: 'overflow-hidden bg-background shadow-[0_4px_16px_rgba(1,52,35,0.04)] transition-all duration-fast hover:shadow-lg',
    cosmetics: 'overflow-hidden bg-background transition-all duration-slow hover:shadow-[0_16px_34px_rgba(208,93,97,0.10)]',
    seven: 'overflow-hidden bg-background transition-transform duration-slow motion-safe:hover:-translate-y-1',
    default: 'overflow-hidden rounded-xl border border-muted/20 bg-background shadow-sm transition-colors duration-normal hover:border-primary/30',
};

const sectionHeaderStyles: Record<ThemeComponentTheme, {
    wrapper: string;
    eyebrow: string;
    title: string;
    subtitle: string;
    action: string;
}> = {
    modern: {
        wrapper: 'mb-10 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between',
        eyebrow: 'text-sm font-semibold uppercase tracking-[0.24em] text-primary-text',
        title: 'mt-3 text-3xl font-semibold tracking-tight text-text sm:text-4xl',
        subtitle: 'mt-4 text-base leading-7 text-muted',
        action: 'inline-flex items-center gap-2 text-sm font-semibold uppercase tracking-[0.18em] text-text transition-colors hover:text-primary-text',
    },
    minimal: {
        wrapper: 'mb-12 flex flex-col gap-3 border-b border-border pb-6 sm:flex-row sm:items-end sm:justify-between',
        eyebrow: 'text-[11px] font-medium uppercase tracking-[0.18em] text-muted',
        title: 'max-w-xl text-2xl font-normal leading-tight text-text',
        subtitle: 'mt-3 text-sm leading-6 text-muted',
        action: 'text-xs font-medium uppercase tracking-[0.18em] text-muted underline underline-offset-8 transition-colors hover:text-text',
    },
    fashion: {
        wrapper: 'mb-12 grid gap-5 border-l-4 border-text pl-5 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-end',
        eyebrow: 'text-xs font-bold uppercase tracking-[0.34em] text-primary-text',
        title: 'mt-3 max-w-3xl text-4xl font-black uppercase leading-[0.95] text-text sm:text-5xl',
        subtitle: 'mt-5 max-w-xl text-base leading-7 text-muted',
        action: 'self-start border-b-2 border-text pb-1 text-xs font-bold uppercase tracking-[0.26em] text-text transition-colors hover:border-primary hover:text-primary-text',
    },
    pearl: {
        wrapper: 'mb-14 max-w-3xl border-b border-secondary/25 pb-7 text-center sm:mx-auto',
        eyebrow: 'text-[11px] font-semibold uppercase tracking-[0.34em] text-secondary-text',
        title: 'mx-auto mt-4 max-w-2xl font-serif text-3xl font-normal leading-tight text-text sm:text-4xl',
        subtitle: 'mx-auto mt-5 max-w-xl text-sm leading-7 text-muted',
        action: 'mt-5 inline-flex border-b border-secondary pb-1 text-[11px] font-semibold uppercase tracking-[0.3em] text-secondary-text transition-colors hover:border-primary hover:text-primary-text',
    },
    phoenix: {
        wrapper: 'mb-10 grid gap-5 border-b border-black/10 pb-6 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-end',
        eyebrow: 'text-xs font-bold uppercase tracking-[0.28em] text-primary-text',
        title: 'mt-3 max-w-3xl text-4xl font-black uppercase leading-[0.95] text-text sm:text-5xl',
        subtitle: 'mt-4 max-w-xl text-sm leading-7 text-muted',
        action: 'self-start border-b border-primary pb-1 text-xs font-bold uppercase tracking-[0.22em] text-primary-text transition-colors hover:border-text hover:text-text',
    },
    hype: {
        wrapper: 'mb-7 grid gap-3 border-b border-muted/20 pb-4 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-end',
        eyebrow: 'text-[11px] font-bold uppercase tracking-[0.16em] text-primary-text',
        title: 'text-2xl font-black leading-tight text-text sm:text-3xl',
        subtitle: 'mt-2 max-w-3xl text-sm leading-6 text-muted',
        action: 'rounded-full border border-primary/35 px-4 py-2 text-[11px] font-bold uppercase tracking-[0.1em] text-primary-text transition-colors hover:bg-primary hover:text-on-primary',
    },
    electronics: {
        wrapper: 'mb-7 grid gap-3 border-b border-muted/25 pb-4 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-end',
        eyebrow: 'font-mono text-[11px] font-bold uppercase tracking-[0.16em] text-primary-text',
        title: 'text-2xl font-black leading-tight text-text sm:text-3xl',
        subtitle: 'mt-2 max-w-3xl text-sm leading-6 text-muted',
        action: 'rounded border border-primary/35 px-3 py-2 font-mono text-[11px] font-bold uppercase tracking-[0.08em] text-primary-text transition-colors hover:bg-primary hover:text-on-primary',
    },
    alizon: {
        wrapper: 'mb-6 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between',
        eyebrow: 'text-[11px] font-semibold uppercase tracking-[0.16em] text-primary-text',
        title: 'text-2xl font-semibold leading-tight text-text sm:text-3xl',
        subtitle: 'mt-2 max-w-xl text-sm leading-6 text-muted',
        action: 'inline-flex items-center gap-1 text-sm font-semibold uppercase tracking-[0.14em] text-text transition-colors hover:text-primary-text',
    },
    sharp: {
        wrapper: 'mb-10 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between',
        eyebrow: 'text-[11px] font-semibold uppercase tracking-[0.2em] text-secondary-text',
        title: 'text-2xl font-semibold leading-tight text-text sm:text-3xl',
        subtitle: 'mt-3 max-w-xl text-sm leading-6 text-muted',
        action: 'inline-flex items-center gap-1 text-sm font-semibold uppercase tracking-[0.14em] text-text transition-colors hover:text-secondary-text',
    },
    cosmetics: {
        wrapper: 'mb-0 flex min-h-[50px] items-center justify-between gap-4 px-3 py-3',
        eyebrow: 'text-[11px] font-medium uppercase tracking-[0.18em] text-primary-text',
        title: 'text-sm font-normal capitalize leading-[26.4px] text-primary-text sm:text-2xl',
        subtitle: 'mt-2 max-w-xl text-sm leading-6 text-muted',
        action: 'text-sm font-normal capitalize leading-7 text-text transition-opacity hover:opacity-70 sm:text-[19px]',
    },
    seven: {
        wrapper: 'mb-12 grid gap-5 border-l-4 border-text pl-5 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-end',
        eyebrow: 'text-xs font-bold uppercase tracking-[0.34em] text-primary-text',
        title: 'mt-3 max-w-3xl text-4xl font-black uppercase leading-[0.95] text-text sm:text-5xl',
        subtitle: 'mt-5 max-w-xl text-base leading-7 text-muted',
        action: 'self-start border-b-2 border-text pb-1 text-xs font-bold uppercase tracking-[0.26em] text-text transition-colors hover:border-primary hover:text-primary-text',
    },
    default: {
        wrapper: 'mb-8 flex items-end justify-between gap-4',
        eyebrow: 'text-sm font-semibold uppercase tracking-[0.2em] text-primary-text',
        title: 'text-2xl font-bold text-text',
        subtitle: 'mt-3 text-sm leading-6 text-muted',
        action: 'text-primary-text hover:underline',
    },
};

const categoryArt = {
    modern: [
        'bg-[linear-gradient(135deg,#f97316_0%,#fb7185_58%,#111827_100%)]',
        'bg-[linear-gradient(135deg,#0f172a_0%,#2563eb_58%,#dbeafe_100%)]',
        'bg-[linear-gradient(135deg,#14532d_0%,#22c55e_60%,#ecfccb_100%)]',
        'bg-[linear-gradient(135deg,#831843_0%,#c026d3_58%,#f5d0fe_100%)]',
    ],
    fashion: [
        'bg-[linear-gradient(90deg,#111827_0%,#111827_46%,#9d174d_46%,#9d174d_100%)]',
        'bg-[linear-gradient(90deg,#172554_0%,#172554_42%,#6d28d9_42%,#6d28d9_100%)]',
        'bg-[linear-gradient(90deg,#422006_0%,#422006_44%,#f97316_44%,#f97316_100%)]',
        'bg-[linear-gradient(90deg,#0f172a_0%,#0f172a_40%,#0ea5e9_40%,#0ea5e9_100%)]',
    ],
    pearl: [
        'bg-[linear-gradient(135deg,#fff7ed_0%,#f8fafc_44%,#334155_44%,#111827_100%)]',
        'bg-[linear-gradient(135deg,#faf5ff_0%,#ffffff_46%,#713f12_46%,#1f2937_100%)]',
        'bg-[linear-gradient(135deg,#f8fafc_0%,#fefce8_48%,#475569_48%,#020617_100%)]',
        'bg-[linear-gradient(135deg,#ffffff_0%,#f5f5f4_45%,#44403c_45%,#1c1917_100%)]',
    ],
    phoenix: [
        'bg-[linear-gradient(135deg,#ece9e2_0%,#ffffff_46%,#cd1515_46%,#202021_100%)]',
        'bg-[linear-gradient(135deg,#202021_0%,#202021_48%,#cd1515_48%,#ece9e2_100%)]',
        'bg-[linear-gradient(135deg,#f5f5f4_0%,#ece9e2_50%,#131316_50%,#cd1515_100%)]',
        'bg-[linear-gradient(135deg,#ffffff_0%,#ece9e2_44%,#cd1515_44%,#202021_100%)]',
    ],
    hype: [
        'bg-[linear-gradient(135deg,#141218_0%,#212A2F_50%,#F06736_50%,#27CDE4_100%)]',
        'bg-[linear-gradient(135deg,#111827_0%,#374151_48%,#F54E05_48%,#fff7ed_100%)]',
        'bg-[linear-gradient(135deg,#18181b_0%,#3f3f46_52%,#27CDE4_52%,#ecfeff_100%)]',
        'bg-[linear-gradient(135deg,#0f172a_0%,#212A2F_50%,#F06736_50%,#fff7ed_100%)]',
    ],
    electronics: [
        'bg-[linear-gradient(135deg,#020617_0%,#1e293b_50%,#38bdf8_50%,#e0f2fe_100%)]',
        'bg-[linear-gradient(135deg,#111827_0%,#374151_48%,#22c55e_48%,#dcfce7_100%)]',
        'bg-[linear-gradient(135deg,#18181b_0%,#3f3f46_52%,#facc15_52%,#fef9c3_100%)]',
        'bg-[linear-gradient(135deg,#0f172a_0%,#334155_50%,#a855f7_50%,#f3e8ff_100%)]',
    ],
    alizon: [
        'bg-[linear-gradient(135deg,#131921_0%,#1E293B_50%,#FF0000_50%,#FFFFFF_100%)]',
        'bg-[linear-gradient(135deg,#0F1729_0%,#1E293B_48%,#FFD814_48%,#FFFFFF_100%)]',
        'bg-[linear-gradient(135deg,#1E293B_0%,#334159_52%,#FF0000_52%,#F1F5F9_100%)]',
        'bg-[linear-gradient(135deg,#0F1729_0%,#1E293B_50%,#FF0000_50%,#FFFFFF_100%)]',
    ],
    sharp: [
        'bg-[linear-gradient(135deg,#FFFFFF_0%,#F8FAFC_44%,#013423_44%,#013423_100%)]',
        'bg-[linear-gradient(135deg,#F0F9FF_0%,#FFFFFF_46%,#013423_46%,#013423_100%)]',
        'bg-[linear-gradient(135deg,#FFFFFF_0%,#F1F5F9_48%,#013423_48%,#013423_100%)]',
        'bg-[linear-gradient(135deg,#FFFFFF_0%,#F8FAFC_45%,#013423_45%,#013423_100%)]',
    ],
    cosmetics: [
        'bg-[linear-gradient(135deg,#C99673_0%,#C99673_47%,#E7DFDB_47%,#FFFFFF_100%)]',
        'bg-[linear-gradient(135deg,#FFFFFF_0%,#E7DFDB_46%,#D05D61_46%,#7A4E4C_100%)]',
        'bg-[linear-gradient(135deg,#F7E8E6_0%,#FFFFFF_48%,#D05D61_48%,#1C1A17_100%)]',
        'bg-[linear-gradient(135deg,#B88773_0%,#E7DFDB_45%,#FFFFFF_45%,#D05D61_100%)]',
    ],
    seven: [
        'bg-[linear-gradient(135deg,#FFFFFF_0%,#F8FAFC_44%,#9D7877_44%,#131316_100%)]',
        'bg-[linear-gradient(135deg,#F5F0EB_0%,#FFFFFF_46%,#9D7877_46%,#131316_100%)]',
        'bg-[linear-gradient(135deg,#FFFFFF_0%,#F1F5F9_48%,#9D7877_48%,#131316_100%)]',
        'bg-[linear-gradient(135deg,#FFFFFF_0%,#F8FAFC_45%,#9D7877_45%,#131316_100%)]',
    ],
    default: [
        'bg-gradient-to-br from-primary/10 to-secondary/10',
        'bg-gradient-to-br from-secondary/10 to-primary/10',
    ],
};

const buttonStyles: Record<ThemeComponentTheme, string> = {
    modern: 'rounded-md bg-primary px-7 py-3.5 text-sm font-semibold text-on-primary shadow-lg shadow-primary/25 transition-all duration-normal hover:-translate-y-0.5 hover:shadow-xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary-text focus-visible:ring-offset-2',
    minimal: 'border border-current bg-transparent px-6 py-3 text-xs font-medium uppercase tracking-[0.16em] text-text transition-opacity duration-fast hover:opacity-80 focus-visible:ring-2 focus-visible:ring-primary-text focus-visible:ring-offset-2',
    fashion: 'border-2 border-current bg-slate-950 px-8 py-3.5 text-xs font-black uppercase tracking-[0.24em] text-white transition-all duration-normal hover:bg-white hover:text-slate-950 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white/60 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-950',
    pearl: 'border border-secondary bg-background px-7 py-3 text-[11px] font-semibold uppercase tracking-[0.28em] text-text transition-colors duration-slow hover:border-primary hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-secondary/50 focus-visible:ring-offset-2',
    phoenix: 'border border-primary bg-primary px-7 py-3 text-xs font-bold uppercase tracking-[0.22em] text-on-primary transition-colors duration-normal hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary-text focus-visible:ring-offset-2',
    hype: 'rounded-full bg-primary px-5 py-3 text-xs font-bold uppercase tracking-[0.12em] text-on-primary transition-colors duration-fast hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary-text focus-visible:ring-offset-2',
    electronics: 'rounded-md bg-primary px-4 py-2.5 font-mono text-[11px] font-bold uppercase tracking-[0.08em] text-on-primary transition-colors duration-fast hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary-text focus-visible:ring-offset-2',
    alizon: 'rounded-none bg-primary px-6 py-3 text-sm font-semibold text-on-primary transition-colors duration-fast hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary-text focus-visible:ring-offset-2',
    sharp: 'bg-primary px-6 py-3 text-sm font-semibold text-on-primary transition-colors duration-fast hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary-text focus-visible:ring-offset-2',
    cosmetics: 'border border-current bg-transparent px-7 py-3 text-xs font-bold uppercase tracking-[0.08em] text-current transition-colors duration-slow hover:bg-current hover:text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 focus-visible:ring-offset-2',
    seven: 'border-2 border-current bg-slate-950 px-8 py-3.5 text-xs font-black uppercase tracking-[0.24em] text-white transition-all duration-normal hover:bg-white hover:text-slate-950 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white/60 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-950',
    default: 'rounded-xl bg-primary px-7 py-3 font-semibold text-on-primary transition-opacity hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary-text focus-visible:ring-offset-2',
};

export function ThemeSurface({
    themeId,
    as = 'article',
    className,
    children,
    ...props
}: {
    themeId?: string | null;
    as?: 'article' | 'div';
    className?: string;
    children: React.ReactNode;
} & React.HTMLAttributes<HTMLElement>) {
    const theme = normalizeThemeComponentTheme(themeId);
    const Component = as;

    return (
        <Component className={cx(surfaceStyles[theme], className)} {...props}>
            {children}
        </Component>
    );
}

export function ThemeSectionHeader({
    themeId,
    eyebrow,
    title,
    subtitle,
    action,
    className,
}: {
    themeId?: string | null;
    eyebrow?: string;
    title: string;
    subtitle?: string;
    action?: ThemeComponentAction;
    className?: string;
}) {
    const theme = normalizeThemeComponentTheme(themeId);
    const styles = sectionHeaderStyles[theme];
    const body = (
        <div className="max-w-2xl">
            {eyebrow ? <p className={styles.eyebrow}>{eyebrow}</p> : null}
            <h2 className={styles.title}>{title}</h2>
            {subtitle ? <p className={styles.subtitle}>{subtitle}</p> : null}
        </div>
    );

    return (
        <div className={cx(styles.wrapper, className)}>
            {body}
            {action?.href ? (
                <AffiliateLink href={action.href} className={styles.action}>
                    {action.label}
                    {theme === 'modern' ? <span aria-hidden="true">{'\u2192'}</span> : null}
                </AffiliateLink>
            ) : null}
        </div>
    );
}

export function ThemeHeroCTA({
    themeId,
    href,
    children,
    align = 'left',
    className,
    color,
}: {
    themeId?: string | null;
    href: string;
    children: React.ReactNode;
    align?: ThemeComponentAlign;
    className?: string;
    color?: string;
}) {
    const justify = align === 'center' ? 'justify-center' : align === 'right' ? 'justify-end' : 'justify-start';

    return (
        <div className={cx('flex', justify, className)}>
            <AffiliateLink
                href={href}
                style={color ? { color, borderColor: color } : undefined}
                className={cx(
                    'inline-flex min-h-11 items-center justify-center gap-2',
                    buttonStyles[normalizeThemeComponentTheme(themeId)]
                )}
            >
                {children}
            </AffiliateLink>
        </div>
    );
}

export function ThemeButton({
    themeId,
    appearance = 'theme',
    className,
    children,
    ...props
}: {
    themeId?: string | null;
    appearance?: 'theme' | 'custom';
    className?: string;
    children: React.ReactNode;
} & React.ButtonHTMLAttributes<HTMLButtonElement>) {
    return (
        <button
            type="button"
            className={cx(
                'inline-flex min-h-9 items-center justify-center gap-1',
                appearance === 'theme' && buttonStyles[normalizeThemeComponentTheme(themeId)],
                'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary-text focus-visible:ring-offset-2',
                className
            )}
            {...props}
        >
            {children}
        </button>
    );
}

/** Navigation action with the same visual/focus treatment as the theme's buttons. */
export function ThemeActionLink({ themeId, appearance = 'theme', href, className, children }: {
    themeId?: string | null;
    appearance?: 'theme' | 'custom';
    href: string;
    className?: string;
    children: React.ReactNode;
}) {
    return <AffiliateLink href={href} className={cx(
        'inline-flex min-h-9 w-full items-center justify-center gap-1',
        appearance === 'theme' && buttonStyles[normalizeThemeComponentTheme(themeId)],
        'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary-text focus-visible:ring-offset-2', className
    )}>{children}</AffiliateLink>;
}

export function ThemeBadge({
    themeId,
    children,
    tone = 'primary',
    className,
}: {
    themeId?: string | null;
    children: React.ReactNode;
    tone?: 'primary' | 'accent' | 'muted';
    className?: string;
}) {
    const theme = normalizeThemeComponentTheme(themeId);
    const toneStyles: Record<ThemeComponentTheme, Record<'primary' | 'accent' | 'muted', string>> = {
        modern: {
            primary: 'bg-primary text-on-primary',
            accent: 'bg-rose-500 text-white',
            muted: 'border border-muted/20 bg-background/90 text-muted',
        },
        minimal: {
            primary: 'border border-text bg-background text-text',
            accent: 'border border-text bg-background text-text',
            muted: 'border border-muted/25 bg-background text-muted',
        },
        fashion: {
            primary: 'bg-text text-on-text',
            accent: 'bg-primary text-on-primary',
            muted: 'border border-text/20 bg-background/90 text-text',
        },
        pearl: {
            primary: 'border border-secondary/40 bg-background text-secondary-text',
            accent: 'bg-secondary text-on-secondary',
            muted: 'border border-secondary/25 bg-background/95 text-muted',
        },
        phoenix: {
            primary: 'border border-primary bg-primary text-on-primary',
            accent: 'bg-text text-on-text',
            muted: 'border border-black/10 bg-background/95 text-muted',
        },
        hype: {
            primary: 'border border-primary bg-primary text-on-primary',
            accent: 'border border-cyan-400 bg-cyan-50 text-cyan-700',
            muted: 'border border-muted/20 bg-muted/10 text-muted',
        },
        electronics: {
            primary: 'border border-primary bg-primary text-on-primary',
            accent: 'border border-primary/40 bg-primary/10 text-primary',
            muted: 'border border-muted/25 bg-muted/10 text-muted',
        },
        alizon: {
            primary: 'bg-primary text-on-primary',
            accent: 'bg-primary text-on-primary',
            muted: 'border border-muted/20 bg-background text-muted',
        },
        sharp: {
            primary: 'bg-primary text-on-primary',
            accent: 'bg-primary text-on-primary',
            muted: 'border border-muted/20 bg-background text-muted',
        },
        cosmetics: {
            primary: 'bg-primary text-on-primary',
            accent: 'bg-[#F2627B] text-white',
            muted: 'border border-primary/20 bg-background/95 text-primary',
        },
        seven: {
            primary: 'bg-text text-on-text',
            accent: 'bg-primary text-on-primary',
            muted: 'border border-text/20 bg-background/90 text-text',
        },
        default: {
            primary: 'bg-primary text-on-primary',
            accent: 'bg-rose-500 text-white',
            muted: 'border border-muted/20 bg-background/85 text-muted',
        },
    };
    const themeClass: Record<ThemeComponentTheme, string> = {
        modern: 'rounded-md px-3 py-1 text-[11px] font-bold uppercase tracking-[0.12em] shadow-sm',
        minimal: 'px-2.5 py-1 text-[10px] font-medium uppercase tracking-[0.14em] shadow-none',
        fashion: 'px-3 py-1.5 text-[10px] font-black uppercase tracking-[0.28em]',
        pearl: 'px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.28em]',
        phoenix: 'px-3 py-1 text-[10px] font-bold uppercase tracking-[0.24em]',
        hype: 'rounded-sm px-2.5 py-1 text-[10px] font-bold uppercase tracking-[0.12em]',
        electronics: 'rounded-sm px-2 py-1 font-mono text-[10px] font-bold uppercase tracking-[0.08em]',
        alizon: 'px-2.5 py-1 text-[10px] font-semibold uppercase tracking-[0.12em]',
        sharp: 'px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.2em]',
        cosmetics: 'px-2.5 py-1 text-[10px] font-semibold uppercase tracking-[0.16em]',
        seven: 'px-3 py-1.5 text-[10px] font-black uppercase tracking-[0.28em]',
        default: 'rounded-full px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.14em] shadow-sm',
    };

    return (
        <span className={cx(toneStyles[theme][tone], themeClass[theme], className)}>
            {children}
        </span>
    );
}

export function ThemeLabel({
    themeId,
    children,
    className,
}: {
    themeId?: string | null;
    children: React.ReactNode;
    className?: string;
}) {
    const theme = normalizeThemeComponentTheme(themeId);
    const styles: Record<ThemeComponentTheme, string> = {
        modern: 'rounded-md border border-primary/20 px-3 py-1 text-xs font-semibold uppercase tracking-[0.12em] text-primary',
        minimal: 'text-[11px] uppercase tracking-[0.14em] text-muted',
        fashion: 'border-l-2 border-primary pl-2 text-xs font-bold uppercase tracking-[0.24em] text-text',
        pearl: 'text-[11px] font-semibold uppercase tracking-[0.3em] text-secondary-text',
        phoenix: 'border-l-2 border-primary pl-2 text-xs font-bold uppercase tracking-[0.22em] text-text',
        hype: 'rounded-sm border border-primary/25 bg-primary/5 px-2 py-1 text-[11px] font-bold uppercase tracking-[0.1em] text-primary',
        electronics: 'rounded-sm border border-primary/25 bg-primary/5 px-2 py-1 font-mono text-[11px] font-bold uppercase tracking-[0.08em] text-primary',
        alizon: 'text-[11px] font-semibold uppercase tracking-[0.14em] text-muted',
        sharp: 'text-[11px] font-semibold uppercase tracking-[0.2em] text-secondary-text',
        cosmetics: 'text-[11px] font-medium uppercase tracking-[0.16em] text-primary',
        seven: 'border-l-2 border-primary pl-2 text-xs font-bold uppercase tracking-[0.24em] text-text',
        default: 'rounded-full border border-primary/20 px-3 py-1 text-xs font-semibold uppercase tracking-[0.12em] text-primary',
    };

    return <span className={cx(styles[theme], className)}>{children}</span>;
}

export function ThemeProductTitle({
    themeId,
    children,
    className,
}: {
    themeId?: string | null;
    children: React.ReactNode;
    className?: string;
}) {
    const theme = normalizeThemeComponentTheme(themeId);
    const styles: Record<ThemeComponentTheme, string> = {
        modern: 'line-clamp-2 text-base font-semibold leading-snug text-text transition-colors duration-fast group-hover:text-primary',
        minimal: 'line-clamp-2 text-sm font-normal leading-snug text-text',
        fashion: 'line-clamp-2 text-xl font-black uppercase leading-[1.02] text-text sm:text-2xl',
        pearl: 'line-clamp-2 font-serif text-base font-normal leading-snug text-text',
        phoenix: 'line-clamp-2 text-lg font-black uppercase leading-[1.02] text-text sm:text-xl',
        hype: 'line-clamp-2 text-sm font-bold leading-snug text-text sm:text-base',
        electronics: 'line-clamp-2 text-sm font-bold leading-snug text-text sm:text-base',
        alizon: 'line-clamp-2 text-sm font-normal leading-snug text-text sm:text-base',
        sharp: 'line-clamp-2 text-base font-semibold leading-snug text-text transition-colors duration-fast group-hover:text-primary',
        cosmetics: 'line-clamp-2 text-sm font-normal capitalize leading-[1.45] text-primary transition-opacity duration-fast group-hover:opacity-80',
        seven: 'line-clamp-2 text-xl font-black uppercase leading-[1.02] text-text sm:text-2xl',
        default: 'line-clamp-2 text-base font-semibold leading-snug text-text',
    };

    return <h3 className={cx(styles[theme], className)}>{children}</h3>;
}

export function ThemeProductMeta({
    themeId,
    children,
    dir,
    className,
}: {
    themeId?: string | null;
    children: React.ReactNode;
    dir?: 'ltr' | 'rtl';
    className?: string;
}) {
    const theme = normalizeThemeComponentTheme(themeId);
    const styles: Record<ThemeComponentTheme, string> = {
        modern: 'text-xs font-semibold uppercase tracking-[0.16em] text-muted',
        minimal: 'text-[11px] uppercase tracking-[0.14em] text-muted',
        fashion: 'line-clamp-1 text-xs font-bold uppercase tracking-[0.24em] text-primary',
        pearl: 'text-[11px] font-semibold uppercase tracking-[0.3em] text-secondary-text',
        phoenix: 'line-clamp-1 text-xs font-bold uppercase tracking-[0.22em] text-primary',
        hype: 'text-[11px] font-semibold uppercase tracking-[0.1em] text-muted',
        electronics: 'font-mono text-[11px] font-semibold uppercase tracking-[0.08em] text-muted',
        alizon: 'text-[11px] font-semibold uppercase tracking-[0.14em] text-muted',
        sharp: 'text-[11px] font-semibold uppercase tracking-[0.2em] text-secondary-text',
        cosmetics: 'text-[10px] font-semibold uppercase tracking-[0.18em] text-primary',
        seven: 'line-clamp-1 text-xs font-bold uppercase tracking-[0.24em] text-primary',
        default: 'text-xs font-semibold uppercase tracking-[0.16em] text-muted',
    };

    return <p className={cx(styles[theme], className)} dir={dir}>{children}</p>;
}

export function ThemePrice({
    themeId,
    price,
    className,
    textClassName,
    currentClassName,
    saleClassName,
    compareClassName,
}: {
    themeId?: string | null;
    price: ThemePricePresentation;
    className?: string;
    textClassName?: string;
    currentClassName?: string;
    saleClassName?: string;
    compareClassName?: string;
}) {
    const theme = normalizeThemeComponentTheme(themeId);
    const wrapperStyles: Record<ThemeComponentTheme, string> = {
        modern: 'flex items-baseline gap-2',
        minimal: 'flex items-baseline gap-2',
        fashion: 'flex flex-col gap-1',
        pearl: 'flex items-baseline gap-2',
        phoenix: 'flex items-baseline gap-2',
        hype: 'flex items-baseline gap-2 tabular-nums',
        electronics: 'flex items-baseline gap-2 tabular-nums',
        alizon: 'flex items-baseline gap-2 tabular-nums',
        sharp: 'flex items-baseline gap-2',
        cosmetics: 'flex items-baseline gap-2 tabular-nums',
        seven: 'flex flex-col gap-1',
        default: 'flex items-baseline gap-2',
    };
    const currentStyles: Record<ThemeComponentTheme, string> = {
        modern: 'text-lg font-bold text-primary',
        minimal: 'text-sm font-semibold text-text',
        fashion: 'text-2xl font-black leading-none text-text',
        pearl: 'font-serif text-lg font-normal text-text',
        phoenix: 'text-lg font-black leading-none text-primary',
        hype: 'text-lg font-black leading-none text-primary tabular-nums',
        electronics: 'font-mono text-lg font-black leading-none text-primary tabular-nums',
        alizon: 'text-lg font-bold text-primary tabular-nums',
        sharp: 'text-lg font-semibold text-text',
        cosmetics: 'text-[10px] font-normal leading-[0.71] text-[rgba(3,3,2,0.76)] tabular-nums',
        seven: 'text-2xl font-black leading-none text-primary',
        default: 'text-lg font-bold text-primary',
    };
    const mutedStyles: Record<ThemeComponentTheme, string> = {
        modern: 'text-sm font-medium text-muted',
        minimal: 'text-sm font-medium text-muted',
        fashion: 'text-base font-semibold text-muted',
        pearl: 'text-sm font-medium text-muted',
        phoenix: 'text-sm font-semibold text-muted',
        hype: 'text-xs font-semibold text-muted tabular-nums',
        electronics: 'font-mono text-xs font-semibold text-muted tabular-nums',
        alizon: 'text-sm font-semibold text-muted tabular-nums',
        sharp: 'text-sm font-medium text-muted',
        cosmetics: 'text-[10px] font-normal leading-[0.71] text-[rgba(3,3,2,0.76)] tabular-nums',
        seven: 'text-base font-semibold text-muted',
        default: 'text-sm font-medium text-muted',
    };
    const compareStyles: Record<ThemeComponentTheme, string> = {
        modern: 'text-sm text-muted line-through',
        minimal: 'text-xs text-muted line-through',
        fashion: 'text-sm font-semibold text-muted line-through',
        pearl: 'text-xs text-muted line-through',
        phoenix: 'text-sm font-semibold text-muted line-through',
        hype: 'text-xs text-muted line-through tabular-nums',
        electronics: 'font-mono text-xs text-muted line-through tabular-nums',
        alizon: 'text-sm text-muted line-through tabular-nums',
        sharp: 'text-sm text-muted line-through',
        cosmetics: 'text-[10px] text-[rgba(3,3,2,0.5)] line-through tabular-nums',
        seven: 'text-sm font-semibold text-muted line-through',
        default: 'text-sm text-muted line-through',
    };
    const wrapper = cx(wrapperStyles[theme], className);

    if (price.kind === 'text') {
        return (
            <div className={wrapper}>
                <span className={cx(mutedStyles[theme], textClassName)}>{price.text}</span>
            </div>
        );
    }

    if (price.kind === 'sale') {
        return (
            <div className={wrapper}>
                <span className={cx(currentStyles[theme], saleClassName)}>{price.current}</span>
                <span className={cx(compareStyles[theme], compareClassName)}>{price.compareAt}</span>
            </div>
        );
    }

    return (
        <div className={wrapper}>
            <span className={cx(currentStyles[theme], currentClassName)}>{price.current}</span>
        </div>
    );
}

export function ThemeImageFrame({
    themeId,
    className,
    children,
}: {
    themeId?: string | null;
    className?: string;
    children: React.ReactNode;
}) {
    const theme = normalizeThemeComponentTheme(themeId);
    const styles: Record<ThemeComponentTheme, string> = {
        modern: 'relative aspect-square overflow-hidden rounded-t-md bg-muted/5',
        minimal: 'relative aspect-[5/6] overflow-hidden bg-muted/5',
        fashion: 'relative aspect-[3/4] overflow-hidden bg-muted/5 after:pointer-events-none after:absolute after:inset-0 after:border after:border-black/10',
        pearl: 'relative aspect-[4/5] overflow-hidden bg-muted/5 p-2 before:pointer-events-none before:absolute before:inset-2 before:z-10 before:border before:border-secondary/20',
        phoenix: 'relative aspect-[3/4] overflow-hidden bg-muted/5 after:pointer-events-none after:absolute after:inset-0 after:border after:border-black/10',
        hype: 'relative aspect-product overflow-hidden bg-muted/5 before:pointer-events-none before:absolute before:inset-0 before:z-10 before:bg-[linear-gradient(90deg,rgba(240,103,54,0.08)_1px,transparent_1px),linear-gradient(180deg,rgba(39,205,228,0.08)_1px,transparent_1px)] before:bg-[size:24px_24px]',
        electronics: 'relative aspect-product overflow-hidden bg-muted/5 before:pointer-events-none before:absolute before:inset-0 before:z-10 before:bg-[linear-gradient(90deg,rgba(100,116,139,0.08)_1px,transparent_1px),linear-gradient(180deg,rgba(100,116,139,0.08)_1px,transparent_1px)] before:bg-[size:24px_24px]',
        alizon: 'relative aspect-[4/3] overflow-hidden bg-muted/5',
        sharp: 'relative aspect-square overflow-hidden bg-muted/5',
        cosmetics: 'relative aspect-square overflow-hidden bg-[#e6e6e6]',
        seven: 'relative aspect-[3/4] overflow-hidden bg-muted/5 after:pointer-events-none after:absolute after:inset-0 after:border after:border-black/10',
        default: 'relative aspect-square overflow-hidden bg-muted/5',
    };

    return <MediaFrame role="product-card" className={cx(styles[theme], className)}>{children}</MediaFrame>;
}

export function ThemeAddToCartButton({
    themeId,
    appearance,
    label,
    onClick,
    disabled,
    className,
}: {
    themeId?: string | null;
    appearance?: 'theme' | 'custom';
    label: string;
    onClick: React.MouseEventHandler<HTMLButtonElement>;
    disabled?: boolean;
    className?: string;
}) {
    return (
        <ThemeButton
            themeId={themeId}
            appearance={appearance}
            onClick={onClick}
            disabled={disabled}
            className={cx('w-full', className)}
            aria-label={label}
        >
            <span aria-hidden="true">+</span>
            <span>{label}</span>
        </ThemeButton>
    );
}

export function ThemeLoadingSkeleton({
    themeId,
    className,
}: {
    themeId?: string | null;
    className?: string;
}) {
    const theme = normalizeThemeComponentTheme(themeId);
    const styles: Record<ThemeComponentTheme, string> = {
        modern: 'animate-pulse rounded-md bg-slate-200/70 shadow-sm',
        minimal: 'animate-pulse border-b border-slate-200 bg-slate-100',
        fashion: 'animate-pulse bg-[linear-gradient(90deg,#e5e7eb_0%,#e5e7eb_44%,#cbd5e1_44%,#cbd5e1_100%)]',
        pearl: 'animate-pulse border border-secondary/25 bg-muted/10 shadow-[0_18px_40px_rgba(15,23,42,0.04)]',
        phoenix: 'animate-pulse border border-black/10 bg-[linear-gradient(90deg,#ece9e2_0%,#ece9e2_44%,#ffffff_44%,#ffffff_100%)]',
        hype: 'animate-pulse rounded-lg border border-muted/20 bg-[linear-gradient(90deg,rgba(240,103,54,0.10)_1px,transparent_1px),linear-gradient(180deg,rgba(39,205,228,0.10)_1px,transparent_1px)] bg-[size:20px_20px]',
        electronics: 'animate-pulse rounded-md border border-muted/25 bg-[linear-gradient(90deg,rgba(100,116,139,0.10)_1px,transparent_1px),linear-gradient(180deg,rgba(100,116,139,0.10)_1px,transparent_1px)] bg-[size:20px_20px]',
        alizon: 'animate-pulse border border-muted/20 bg-[linear-gradient(90deg,rgba(255,0,0,0.06)_1px,transparent_1px),linear-gradient(180deg,rgba(255,0,0,0.06)_1px,transparent_1px)] bg-[size:20px_20px]',
        sharp: 'animate-pulse border border-muted/20 bg-[linear-gradient(90deg,rgba(1,52,35,0.06)_1px,transparent_1px),linear-gradient(180deg,rgba(1,52,35,0.06)_1px,transparent_1px)] bg-[size:20px_20px]',
        cosmetics: 'animate-pulse bg-[linear-gradient(90deg,#f5eeee_0%,#f5eeee_44%,#e7dfdb_44%,#e7dfdb_100%)]',
        seven: 'animate-pulse bg-[linear-gradient(90deg,#e5e7eb_0%,#e5e7eb_44%,#cbd5e1_44%,#cbd5e1_100%)]',
        default: 'animate-pulse rounded-xl bg-muted/10',
    };

    return <div className={cx(styles[theme], className)} />;
}

export function ThemeEmptyState({
    themeId,
    title,
    message,
    className,
}: {
    themeId?: string | null;
    title: string;
    message?: string;
    className?: string;
}) {
    const theme = normalizeThemeComponentTheme(themeId);
    const styles: Record<ThemeComponentTheme, { wrapper: string; title: string; message: string }> = {
        modern: {
            wrapper: 'rounded-md border border-slate-200 bg-white px-6 py-10 text-center text-slate-500 shadow-sm',
            title: 'text-lg font-semibold text-text',
            message: 'mx-auto mt-2 max-w-md text-sm leading-6',
        },
        minimal: {
            wrapper: 'border-y border-slate-200 px-6 py-12 text-center text-slate-500',
            title: 'text-base font-normal text-text',
            message: 'mx-auto mt-3 max-w-md text-sm leading-6',
        },
        fashion: {
            wrapper: 'border-l-4 border-text px-6 py-12 text-left text-slate-500',
            title: 'text-2xl font-black uppercase leading-none text-text',
            message: 'mt-3 max-w-md text-sm leading-6',
        },
        pearl: {
            wrapper: 'border border-secondary/30 bg-background px-8 py-12 text-center text-muted shadow-[0_18px_40px_rgba(15,23,42,0.04)]',
            title: 'font-serif text-xl font-normal text-text',
            message: 'mx-auto mt-3 max-w-md text-sm leading-7',
        },
        phoenix: {
            wrapper: 'border border-black/10 bg-background px-6 py-10 text-left text-muted shadow-sm',
            title: 'text-xl font-black uppercase leading-none text-text',
            message: 'mt-3 max-w-md text-sm leading-6',
        },
        hype: {
            wrapper: 'rounded-lg border border-muted/20 bg-background px-5 py-8 text-left text-muted',
            title: 'text-sm font-bold uppercase tracking-[0.12em] text-text',
            message: 'mt-2 max-w-md text-sm leading-6',
        },
        electronics: {
            wrapper: 'rounded-md border border-muted/25 bg-background px-5 py-8 text-left text-muted',
            title: 'font-mono text-sm font-bold uppercase tracking-[0.08em] text-text',
            message: 'mt-2 max-w-md text-sm leading-6',
        },
        alizon: {
            wrapper: 'rounded border border-muted/20 bg-background px-6 py-10 text-center text-muted shadow-sm',
            title: 'text-lg font-semibold text-text',
            message: 'mx-auto mt-2 max-w-md text-sm leading-6',
        },
        sharp: {
            wrapper: 'border border-muted/20 bg-background px-6 py-10 text-center text-muted shadow-[0_4px_16px_rgba(1,52,35,0.04)]',
            title: 'text-lg font-semibold text-text',
            message: 'mx-auto mt-2 max-w-md text-sm leading-6',
        },
        cosmetics: {
            wrapper: 'border-y border-primary/20 bg-background px-6 py-10 text-center text-muted',
            title: 'text-lg font-normal text-primary',
            message: 'mx-auto mt-2 max-w-md text-sm leading-6',
        },
        seven: {
            wrapper: 'border-l-4 border-text px-6 py-12 text-left text-slate-500',
            title: 'text-2xl font-black uppercase leading-none text-text',
            message: 'mt-3 max-w-md text-sm leading-6',
        },
        default: {
            wrapper: 'px-6 py-10 text-center text-muted',
            title: 'font-semibold text-text',
            message: 'mx-auto mt-2 max-w-md text-sm leading-6',
        },
    };
    const style = styles[theme];

    return (
        <div className={cx(style.wrapper, className)} role="status" aria-label={title}>
            <h3 className={style.title}>{title}</h3>
            {message ? <p className={style.message}>{message}</p> : null}
        </div>
    );
}

export function ThemeCategoryCard({
    themeId,
    href,
    title,
    index,
}: {
    themeId?: string | null;
    href: string;
    title: string;
    index: number;
}) {
    const theme = normalizeThemeComponentTheme(themeId);

    if (theme === 'minimal') {
        return (
            <AffiliateLink href={href} className="group block border border-slate-200 bg-white p-4 transition-colors duration-fast hover:border-slate-950">
                <div className="relative aspect-[4/3] overflow-hidden bg-[linear-gradient(135deg,#ffffff_0%,#f8fafc_58%,#e2e8f0_100%)]">
                    <div className="absolute inset-x-4 bottom-4 h-px bg-slate-300 transition-colors duration-fast group-hover:bg-slate-950" />
                </div>
                <h3 className="mt-4 min-h-[3rem] overflow-hidden text-base font-normal leading-6 text-slate-900 [display:-webkit-box] [-webkit-box-orient:vertical] [-webkit-line-clamp:2]">{title}</h3>
            </AffiliateLink>
        );
    }

    if (theme === 'fashion') {
        const artClass = categoryArt.fashion[index % categoryArt.fashion.length];

        return (
            <AffiliateLink href={href} className="group block overflow-hidden bg-slate-950 ring-1 ring-white/10 transition-transform duration-300 hover:-translate-y-1">
                <article className="relative aspect-[4/3] min-h-[13rem] overflow-hidden">
                    <div className={cx('absolute inset-0 transition-transform duration-700 group-hover:scale-105', artClass)} />
                    <div className="absolute inset-0 bg-[linear-gradient(180deg,rgba(15,23,42,0.08)_0%,rgba(15,23,42,0.78)_100%)]" />
                    <div className="absolute inset-x-0 bottom-0 p-4 sm:p-5">
                        <p className="mb-2 text-[10px] font-black uppercase tracking-[0.22em] text-white/70">
                            {String(index + 1).padStart(2, '0')}
                        </p>
                        <h3 className="max-w-[15rem] overflow-hidden text-xl font-black uppercase leading-tight text-white [display:-webkit-box] [-webkit-box-orient:vertical] [-webkit-line-clamp:2] sm:text-2xl">{title}</h3>
                    </div>
                </article>
            </AffiliateLink>
        );
    }

    if (theme === 'pearl') {
        const artClass = categoryArt.pearl[index % categoryArt.pearl.length];

        return (
            <AffiliateLink href={href} className="group block bg-background p-2 shadow-[0_14px_34px_rgba(15,23,42,0.05)] transition-transform duration-slow hover:-translate-y-1">
                <article className="relative aspect-[4/3] min-h-[13rem] overflow-hidden border border-secondary/25 p-3">
                    <div className={cx('absolute inset-3 transition-transform duration-slow group-hover:scale-[1.03]', artClass)} />
                    <div className="absolute inset-3 bg-[linear-gradient(180deg,rgba(255,255,255,0.08)_0%,rgba(15,23,42,0.58)_100%)]" />
                    <div className="absolute inset-x-6 bottom-6 border-t border-white/40 pt-4 text-center">
                        <h3 className="overflow-hidden font-serif text-xl font-normal leading-tight text-white [display:-webkit-box] [-webkit-box-orient:vertical] [-webkit-line-clamp:2] sm:text-2xl">{title}</h3>
                    </div>
                </article>
            </AffiliateLink>
        );
    }

    if (theme === 'phoenix') {
        const artClass = categoryArt.phoenix[index % categoryArt.phoenix.length];

        return (
            <AffiliateLink href={href} className="group block bg-background shadow-sm transition-transform duration-300 hover:-translate-y-1">
                <article className="relative aspect-[4/3] min-h-[13rem] overflow-hidden border border-black/10">
                    <div className={cx('absolute inset-0 transition-transform duration-700 group-hover:scale-105', artClass)} />
                    <div className="absolute inset-0 bg-[linear-gradient(180deg,rgba(19,19,22,0.04)_0%,rgba(19,19,22,0.76)_100%)]" />
                    <div className="absolute inset-x-4 bottom-4">
                        <h3 className="overflow-hidden text-2xl font-black uppercase leading-tight text-white [display:-webkit-box] [-webkit-box-orient:vertical] [-webkit-line-clamp:2]">{title}</h3>
                    </div>
                </article>
            </AffiliateLink>
        );
    }

    if (theme === 'hype') {
        const artClass = categoryArt.hype[index % categoryArt.hype.length];

        return (
            <AffiliateLink href={href} className="group block rounded-lg border border-muted/20 bg-background p-3 shadow-sm transition-all duration-fast hover:-translate-y-1 hover:border-primary/50 hover:shadow-md">
                <article className="relative aspect-[4/3] min-h-[12rem] overflow-hidden rounded-md">
                    <div className={cx('absolute inset-0 transition-transform duration-normal group-hover:scale-[1.03]', artClass)} />
                    <div className="absolute inset-0 bg-[linear-gradient(90deg,rgba(20,18,24,0.72)_0%,rgba(20,18,24,0.18)_100%)]" />
                    <div className="absolute inset-x-4 bottom-4">
                        <h3 className="overflow-hidden text-xl font-black uppercase leading-tight text-white [display:-webkit-box] [-webkit-box-orient:vertical] [-webkit-line-clamp:2]">{title}</h3>
                    </div>
                </article>
            </AffiliateLink>
        );
    }

    if (theme === 'electronics') {
        const artClass = categoryArt.electronics[index % categoryArt.electronics.length];

        return (
            <AffiliateLink href={href} className="group block rounded-md border border-muted/25 bg-background p-3 shadow-sm transition-all duration-fast hover:-translate-y-1 hover:border-primary/50 hover:shadow-md">
                <article className="relative aspect-[4/3] min-h-[12rem] overflow-hidden rounded-sm">
                    <div className={cx('absolute inset-0 transition-transform duration-normal group-hover:scale-[1.03]', artClass)} />
                    <div className="absolute inset-0 bg-[linear-gradient(90deg,rgba(2,6,23,0.72)_0%,rgba(2,6,23,0.18)_100%)]" />
                    <div className="absolute inset-x-4 bottom-4">
                        <h3 className="overflow-hidden font-mono text-lg font-black uppercase leading-tight text-white [display:-webkit-box] [-webkit-box-orient:vertical] [-webkit-line-clamp:2] sm:text-xl">{title}</h3>
                    </div>
                </article>
            </AffiliateLink>
        );
    }

    if (theme === 'alizon') {
        const artClass = categoryArt.alizon[index % categoryArt.alizon.length];

        return (
            <AffiliateLink href={href} className="group block overflow-hidden border border-muted/20 bg-background shadow-sm transition-all duration-fast hover:-translate-y-1 hover:border-primary/50 hover:shadow-md">
                <article className="relative aspect-[4/3] min-h-[12rem] overflow-hidden">
                    <div className={cx('absolute inset-0 transition-transform duration-normal group-hover:scale-[1.03]', artClass)} />
                    <div className="absolute inset-0 bg-[linear-gradient(180deg,rgba(19,25,33,0.72)_0%,rgba(19,25,33,0.18)_100%)]" />
                    <div className="absolute inset-x-4 bottom-4">
                        <h3 className="overflow-hidden text-lg font-semibold leading-tight text-white [display:-webkit-box] [-webkit-box-orient:vertical] [-webkit-line-clamp:2]">{title}</h3>
                    </div>
                </article>
            </AffiliateLink>
        );
    }

    if (theme === 'sharp') {
        const artClass = categoryArt.sharp[index % categoryArt.sharp.length];

        return (
            <AffiliateLink href={href} className="group block overflow-hidden bg-background shadow-sm transition-transform duration-fast hover:-translate-y-1">
                <article className="relative aspect-[4/3] min-h-[13rem] overflow-hidden border border-muted/20">
                    <div className={cx('absolute inset-0 transition-transform duration-normal group-hover:scale-[1.03]', artClass)} />
                    <div className="absolute inset-0 bg-[linear-gradient(180deg,rgba(1,52,35,0.08)_0%,rgba(1,52,35,0.78)_100%)]" />
                    <div className="absolute inset-x-4 bottom-4">
                        <h3 className="overflow-hidden text-lg font-semibold leading-tight text-white [display:-webkit-box] [-webkit-box-orient:vertical] [-webkit-line-clamp:2]">{title}</h3>
                    </div>
                </article>
            </AffiliateLink>
        );
    }

    if (theme === 'cosmetics') {
        const artClass = categoryArt.cosmetics[index % categoryArt.cosmetics.length];

        return (
            <AffiliateLink href={href} className="group block overflow-hidden bg-[#eeeeee] transition-transform duration-slow hover:-translate-y-0.5">
                <article className="relative aspect-[4/5] min-h-[18rem] overflow-hidden">
                    <div className={cx('absolute inset-0 transition-transform duration-700 group-hover:scale-105', artClass)} />
                    <div className="absolute inset-0 bg-black/20" />
                    <div className="absolute inset-0 flex items-center justify-center p-6 text-center">
                        <h3 className="max-w-[18rem] overflow-hidden text-xl font-medium uppercase leading-[1.5] tracking-[0.02em] text-white [display:-webkit-box] [-webkit-box-orient:vertical] [-webkit-line-clamp:2] sm:text-2xl">
                            {title}
                        </h3>
                    </div>
                </article>
            </AffiliateLink>
        );
    }

    if (theme === 'seven') {
        const artClass = categoryArt.seven[index % categoryArt.seven.length];

        return (
            <AffiliateLink href={href} className="group block overflow-hidden bg-slate-950 ring-1 ring-white/10 transition-transform duration-300 hover:-translate-y-1">
                <article className="relative aspect-[4/3] min-h-[13rem] overflow-hidden">
                    <div className={cx('absolute inset-0 transition-transform duration-700 group-hover:scale-105', artClass)} />
                    <div className="absolute inset-0 bg-[linear-gradient(180deg,rgba(19,19,22,0.04)_0%,rgba(19,19,22,0.76)_100%)]" />
                    <div className="absolute inset-x-4 bottom-4">
                        <p className="mb-2 text-[10px] font-black uppercase tracking-[0.22em] text-white/70">
                            {String(index + 1).padStart(2, '0')}
                        </p>
                        <h3 className="max-w-[15rem] overflow-hidden text-xl font-black uppercase leading-tight text-white [display:-webkit-box] [-webkit-box-orient:vertical] [-webkit-line-clamp:2] sm:text-2xl">{title}</h3>
                    </div>
                </article>
            </AffiliateLink>
        );
    }

    const artClass = theme === 'modern'
        ? categoryArt.modern[index % categoryArt.modern.length]
        : categoryArt.default[index % categoryArt.default.length];

    return (
        <AffiliateLink
            href={href}
            className={cx(
                'group block overflow-hidden transition-transform duration-300 hover:-translate-y-1',
                theme === 'modern' ? 'rounded-md bg-slate-950 shadow-md shadow-slate-200/70' : 'rounded-lg border border-muted/10'
            )}
        >
            <div className="relative aspect-[4/3] min-h-[12rem] overflow-hidden">
                <div className={cx('absolute inset-0 transition-transform duration-500 group-hover:scale-110', artClass)} />
                <div className="absolute inset-0 bg-[linear-gradient(180deg,rgba(15,23,42,0.06)_0%,rgba(15,23,42,0.82)_100%)]" />
                <div className="absolute right-4 top-4 flex h-11 w-11 items-center justify-center rounded-md border border-white/20 bg-white/10 text-sm font-semibold text-white backdrop-blur-md">
                    {String(index + 1).padStart(2, '0')}
                </div>
                <div className="absolute inset-x-0 bottom-0 p-4">
                    <div className="rounded-md border border-white/15 bg-white/10 p-4 backdrop-blur-md">
                        <h3 className="overflow-hidden text-xl font-semibold leading-tight text-white [display:-webkit-box] [-webkit-box-orient:vertical] [-webkit-line-clamp:2]">{title}</h3>
                    </div>
                </div>
            </div>
        </AffiliateLink>
    );
}

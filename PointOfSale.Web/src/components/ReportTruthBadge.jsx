import { useTranslation } from 'react-i18next';

/**
 * PHASE C4: UI TRUTH LABELS
 * 
 * Visual classification badge for report pages.
 * Instantly communicates whether a report is:
 * - Official Financial (Ledger-Based) → Green
 * - Operational Analytics → Blue
 * 
 * This badge appears at the top of every report page,
 * ensuring users always know the source of their data.
 */
export default function ReportTruthBadge({ type }) {
    const { t } = useTranslation();

    const styles = {
        financial: {
            background: 'linear-gradient(135deg, #0f766e 0%, #14b8a6 100%)',
            borderColor: '#0f766e',
            iconBg: 'rgba(255, 255, 255, 0.2)',
            text: t('reports.badge.financial', '📋 Official Financial Report (Ledger-Based)'),
            subtext: t('reports.badge.financialSubtext', 'Source: Immutable Double-Entry Ledger')
        },
        operational: {
            background: 'linear-gradient(135deg, #1d4ed8 0%, #3b82f6 100%)',
            borderColor: '#1d4ed8',
            iconBg: 'rgba(255, 255, 255, 0.2)',
            text: t('reports.badge.operational', '📊 Operational Analytics Report'),
            subtext: t('reports.badge.operationalSubtext', 'Source: Business Transaction Data')
        }
    };

    const cfg = styles[type] || styles.operational;

    return (
        <div style={{
            background: cfg.background,
            color: 'white',
            padding: '12px 16px',
            borderRadius: '8px',
            marginBottom: '16px',
            display: 'inline-flex',
            flexDirection: 'column',
            gap: '4px',
            boxShadow: '0 2px 8px rgba(0, 0, 0, 0.15)',
            border: `1px solid ${cfg.borderColor}`,
            maxWidth: '100%'
        }}>
            <div style={{
                fontWeight: '600',
                fontSize: '14px',
                letterSpacing: '0.025em'
            }}>
                {cfg.text}
            </div>
            <div style={{
                fontSize: '11px',
                opacity: 0.85,
                fontWeight: '400'
            }}>
                {cfg.subtext}
            </div>
        </div>
    );
}

/**
 * Compact version for tighter layouts
 */
export function ReportTruthBadgeCompact({ type }) {
    const { t } = useTranslation();

    const styles = {
        financial: {
            background: '#0f766e',
            text: t('reports.badge.financialShort', '📋 Ledger-Based')
        },
        operational: {
            background: '#1d4ed8',
            text: t('reports.badge.operationalShort', '📊 Analytics')
        }
    };

    const cfg = styles[type] || styles.operational;

    return (
        <span style={{
            background: cfg.background,
            color: 'white',
            padding: '4px 10px',
            borderRadius: '4px',
            fontSize: '12px',
            fontWeight: '600',
            display: 'inline-block'
        }}>
            {cfg.text}
        </span>
    );
}

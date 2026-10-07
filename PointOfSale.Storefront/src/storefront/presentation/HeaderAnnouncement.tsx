import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { useTranslation } from '@/i18n';
import type { HeaderPresentationModel } from './models';

export const AnnouncementLink: React.FC<{ link?: string; children: React.ReactNode }> = ({ link, children }) => {
    const safe = link && ((link.startsWith('/') && !link.startsWith('//') && !link.includes('\\')) || /^https?:\/\//i.test(link));
    return safe ? <AffiliateLink href={link} className="block focus-visible:outline focus-visible:outline-2">{children}</AffiliateLink> : <>{children}</>;
};

// Themes without an announcement default opt into the same Header contract.
export const HeaderAnnouncement: React.FC<{ model: HeaderPresentationModel; className?: string }> = ({ model, className = 'bg-primary text-white' }) => {
    const { t, direction } = useTranslation();
    const config = model.announcement;
    if (!config?.enabled || !model.labels.announcement.trim()) return null;
    return <div role="region" aria-label={t('announcement.label')} dir={direction}
        className={`px-4 py-2 text-center text-sm break-words ${className}`}
        style={{ backgroundColor: config.backgroundColor || undefined, color: config.textColor || undefined }}>
        <AnnouncementLink link={config.link}>{model.labels.announcement}</AnnouncementLink>
    </div>;
};

import type { ReactElement } from 'react';
export interface HeaderAnnouncementConfig { enabled: boolean; text: string; textAr: string; link: string; backgroundColor: string; textColor: string }
export function HeaderAnnouncementSettings(props: { value: HeaderAnnouncementConfig | null; onChange(value: HeaderAnnouncementConfig | null): void }): ReactElement;

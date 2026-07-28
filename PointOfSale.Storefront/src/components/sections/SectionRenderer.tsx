import React, { useEffect, useMemo, useState } from 'react';
import type { ComponentType, KeyboardEvent, MouseEvent } from 'react';
import { SectionErrorBoundary } from '../../storefront/components/errors/SectionErrorBoundary';
import { RuntimeSectionContainer } from '../../storefront/runtime/containers/RuntimeSectionContainer';
import { useTheme } from '../../storefront/themes/ThemeContext';
import {
    getDefaultSectionComponent,
    resolveSectionComponent,
} from '../../storefront/themes/ThemeSectionResolver';
import {
    normalizeSections,
    type HomepageSection,
    type SectionRendererInputSection,
} from './homepageSections';

interface SectionRendererProps {
    section?: SectionRendererInputSection;
    sections?: SectionRendererInputSection[];
    sectionProps?: Record<string, unknown>;
    selectedSectionId?: string | null;
    onSectionSelect?: (sectionId: string) => void;
}

function RenderedSectionItem({
    section,
    sectionProps,
    isSelected,
    onSectionSelect,
}: {
    section: HomepageSection;
    sectionProps?: Record<string, unknown>;
    isSelected: boolean;
    onSectionSelect?: (sectionId: string) => void;
}) {
    const { themeId } = useTheme();
    const [Component, setComponent] = useState<ComponentType<any> | null>(() => (
        getDefaultSectionComponent(section.type)
    ));
    const [resolutionFailed, setResolutionFailed] = useState(false);

    useEffect(() => {
        let cancelled = false;

        const fallbackComponent = getDefaultSectionComponent(section.type);
        setComponent(() => fallbackComponent);
        setResolutionFailed(false);

        resolveSectionComponent(section.type, themeId)
            .then((resolvedComponent) => {
                if (cancelled) {
                    return;
                }

                if (resolvedComponent) {
                    setComponent(() => resolvedComponent);
                    return;
                }

                setComponent(null);
                setResolutionFailed(true);
            })
            .catch((error) => {
                console.error(
                    `[SectionRenderer] Failed to resolve section "${section.type}" for theme "${themeId}".`,
                    error
                );

                if (cancelled) {
                    return;
                }

                setComponent(() => fallbackComponent);
                setResolutionFailed(fallbackComponent === null);
            });

        return () => {
            cancelled = true;
        };
    }, [section.type, themeId]);

    if (!section.enabled) {
        return null;
    }

    if (!Component) {
        if (import.meta.env.DEV && resolutionFailed) {
            return (
                <div className="p-4 border border-yellow-500 bg-yellow-50 text-yellow-700 text-center">
                    Unknown Section Type: <code>{section.type}</code>
                </div>
            );
        }

        return null;
    }

    const handleClickCapture = (event: MouseEvent<HTMLDivElement>) => {
        if (!onSectionSelect) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        onSectionSelect(section.id);
    };

    const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        if (!onSectionSelect) {
            return;
        }

        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            onSectionSelect(section.id);
        }
    };

    return (
        <div
            className={[
                'section-wrapper',
                onSectionSelect ? 'cursor-pointer transition-shadow' : '',
                isSelected ? 'rounded-[28px] ring-4 ring-sky-200/75' : '',
            ].join(' ')}
            data-section-id={section.id}
            data-section-type={section.type}
            onClickCapture={onSectionSelect ? handleClickCapture : undefined}
            onKeyDown={onSectionSelect ? handleKeyDown : undefined}
            role={onSectionSelect ? 'button' : undefined}
            tabIndex={onSectionSelect ? 0 : undefined}
        >
            <SectionErrorBoundary sectionType={section.type} resetKey={`${section.id}:${themeId}`}>
                <RuntimeSectionContainer
                    component={Component}
                    sectionType={section.type}
                    themeId={themeId}
                    sectionProps={{ ...section.data, sectionId: section.id, ...sectionProps }}
                />
            </SectionErrorBoundary>
        </div>
    );
}

export const SectionRenderer: React.FC<SectionRendererProps> = ({
    section,
    sections,
    sectionProps,
    selectedSectionId = null,
    onSectionSelect,
}) => {
    const normalizedSections = useMemo(() => {
        if (sections?.length) {
            return normalizeSections(sections);
        }

        if (section) {
            return normalizeSections([section]);
        }

        return [];
    }, [section, sections]);

    if (normalizedSections.length === 0) {
        return null;
    }

    return (
        <>
            {normalizedSections.map((normalizedSection) => (
                <RenderedSectionItem
                    key={normalizedSection.id}
                    section={normalizedSection}
                    sectionProps={sectionProps}
                    isSelected={selectedSectionId === normalizedSection.id}
                    onSectionSelect={onSectionSelect}
                />
            ))}
        </>
    );
};

export default SectionRenderer;

import React from 'react';
import { HeaderAnnouncement } from '@/storefront/presentation/HeaderAnnouncement';
import { Header } from '../Header';
import type { HeaderPresentationProps } from '@/storefront/presentation/models';

const DefaultHeader: React.FC<HeaderPresentationProps> = (props) => <><HeaderAnnouncement model={props.model} /><Header {...props} /></>;

export default DefaultHeader;

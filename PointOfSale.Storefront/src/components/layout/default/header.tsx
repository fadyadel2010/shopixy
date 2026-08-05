import React from 'react';
import { Header } from '../Header';
import type { HeaderPresentationProps } from '@/storefront/presentation/models';

const DefaultHeader: React.FC<HeaderPresentationProps> = (props) => <Header {...props} />;

export default DefaultHeader;

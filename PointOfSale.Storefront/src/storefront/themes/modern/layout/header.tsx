import React from 'react';
import DefaultHeader from '../../../../components/layout/default/header';
import type { HeaderPresentationProps } from '@/storefront/presentation/models';

const ModernHeader: React.FC<HeaderPresentationProps> = (props) => <DefaultHeader {...props} />;

export default ModernHeader;

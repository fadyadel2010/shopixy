import React from 'react';

export const SectionErrorFallback: React.FC = () => {
    return (
        <div className="rounded-xl border border-slate-200 bg-slate-50 px-5 py-6 text-sm text-slate-600">
            <p className="font-medium text-slate-800">Section failed to load</p>
        </div>
    );
};

export default SectionErrorFallback;

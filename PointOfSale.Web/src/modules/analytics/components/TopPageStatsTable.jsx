import { useTranslation } from 'react-i18next';

export default function TopPageStatsTable({ pages }) {
    const { t } = useTranslation();

    if (!pages || pages.length === 0) return null;

    return (
        <div className="mt-2 bg-gray-50 rounded-lg p-3">
            <h4 className="text-xs font-semibold text-gray-500 uppercase mb-2">
                {t('funnels.contextual.topPages')}
            </h4>
            <div className="overflow-hidden bg-white border border-gray-100 rounded">
                <table className="min-w-full divide-y divide-gray-100">
                    <tbody className="divide-y divide-gray-50">
                        {pages.map((page, index) => (
                            <tr key={index}>
                                <td className="px-3 py-2 text-xs text-gray-700 truncate max-w-[200px]">
                                    {page.pagePath}
                                </td>
                                <td className="px-3 py-2 text-xs text-right text-gray-600 font-medium">
                                    {page.viewCount?.toLocaleString()}
                                </td>
                                <td className="px-3 py-2 text-xs text-right text-gray-500 w-12">
                                    {page.percentage}%
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </div>
    );
}

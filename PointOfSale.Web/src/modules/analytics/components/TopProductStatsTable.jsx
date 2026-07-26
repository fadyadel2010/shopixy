import { useTranslation } from 'react-i18next';

export default function TopProductStatsTable({ products, titleKey = 'topProducts' }) {
    const { t } = useTranslation();

    if (!products || products.length === 0) return null;

    const title = t(`funnels.contextual.${titleKey}`);

    return (
        <div className="mt-2 bg-gray-50 rounded-lg p-3">
            <h4 className="text-xs font-semibold text-gray-500 uppercase mb-2">{title}</h4>
            <div className="overflow-hidden bg-white border border-gray-100 rounded">
                <table className="min-w-full divide-y divide-gray-100">
                    <tbody className="divide-y divide-gray-50">
                        {products.map((product, index) => (
                            <tr key={index}>
                                <td className="px-3 py-2 text-xs text-gray-700 truncate max-w-[200px]">
                                    {product.productName || t('onlineCustomers.wishlist.unknownProduct')}
                                    <span className="text-gray-400 text-[10px] ml-1">
                                        ({product.productId?.substring?.(0, 8) || product.productId})
                                    </span>
                                </td>
                                <td className="px-3 py-2 text-xs text-right text-gray-600 font-medium">
                                    {product.count?.toLocaleString()}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </div>
    );
}

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import VariantMediaPicker from './VariantMediaPicker'

export default function VariantBulkActions({
    productId,
    selectedCount,
    onApply,
    generationBlocked,
    warehouses = [],
    selectedWarehouseId = '',
    onWarehouseChange = () => {}
}) {
    const { t } = useTranslation()
    const tx = (key, fallback) => {
        const value = t(key)
        return value === key ? fallback : value
    }
    const [setPrice, setSetPrice] = useState('')
    const [setCostPrice, setSetCostPrice] = useState('')
    const [setInventory, setSetInventory] = useState('')
    const [setWeight, setSetWeight] = useState('')
    const [percentChange, setPercentChange] = useState('')
    const [imagePickerOpen, setImagePickerOpen] = useState(false)

    const noSelection = selectedCount === 0
    const selectedCaption = selectedCount === 1
        ? tx('variants.oneSelected', '1 Selected')
        : tx('variants.countSelected', '{{count}} Selected').replace('{{count}}', selectedCount)
    const applyCaption = noSelection
        ? tx('variants.applyToSelected', 'Apply to Selected')
        : tx('variants.applyToCountSelected', 'Apply to {{count}} Selected').replace('{{count}}', selectedCount)
    const enableCaption = noSelection
        ? tx('common.enable', 'Enable')
        : tx('variants.enableCountSelected', 'Enable {{count}} Selected').replace('{{count}}', selectedCount)
    const disableCaption = noSelection
        ? tx('common.disable', 'Disable')
        : tx('variants.disableCountSelected', 'Disable {{count}} Selected').replace('{{count}}', selectedCount)
    const deleteCaption = noSelection
        ? tx('variants.deleteSelected', 'Delete Selected')
        : tx('variants.deleteCountSelected', 'Delete {{count}} Selected').replace('{{count}}', selectedCount)

    return (
        <div className="space-y-4 rounded-2xl border border-gray-200 bg-white p-4 shadow-sm">
            <div>
                <h4 className="text-sm font-semibold text-gray-900">
                    {tx('variants.bulkActions', 'Bulk Actions')}
                </h4>
                <p className="text-sm text-gray-500">
                    {tx('variants.selectedVariants', 'Selected variants')}: {selectedCaption}
                </p>
                {generationBlocked && (
                    <p className="mt-2 text-sm text-amber-700">
                        {tx('variants.generationNeedsAttention', 'Variant generation needs attention before editing can continue.')}
                    </p>
                )}
            </div>

            <div className="grid gap-4 lg:grid-cols-2 xl:grid-cols-4">
                <div className="space-y-2 rounded-xl border border-gray-200 bg-gray-50 p-3">
                    <label className="label">{tx('variants.setCostPrice', 'Set Purchase Price')}</label>
                    <input
                        type="number"
                        step="0.01"
                        value={setCostPrice}
                        onChange={(e) => setSetCostPrice(e.target.value)}
                        className="input"
                        placeholder="0.00"
                    />
                    <button
                        type="button"
                        onClick={() => {
                            onApply({ type: 'set-cost-price', value: setCostPrice })
                            setSetCostPrice('')
                        }}
                        disabled={noSelection || !setCostPrice}
                        className="w-full rounded-lg border border-gray-200 px-3 py-2 text-sm font-medium text-gray-700 transition hover:border-gray-300 hover:bg-white disabled:cursor-not-allowed disabled:opacity-50"
                    >
                        {applyCaption}
                    </button>
                </div>

                <div className="space-y-2 rounded-xl border border-gray-200 bg-gray-50 p-3">
                    <label className="label">{tx('variants.setPrice', 'Set Selling Price')}</label>
                    <input
                        type="number"
                        step="0.01"
                        value={setPrice}
                        onChange={(e) => setSetPrice(e.target.value)}
                        className="input"
                        placeholder="0.00"
                    />
                    <button
                        type="button"
                        onClick={() => {
                            onApply({ type: 'set-price', value: setPrice })
                            setSetPrice('')
                        }}
                        disabled={noSelection || !setPrice}
                        className="w-full rounded-lg border border-gray-200 px-3 py-2 text-sm font-medium text-gray-700 transition hover:border-gray-300 hover:bg-white disabled:cursor-not-allowed disabled:opacity-50"
                    >
                        {applyCaption}
                    </button>
                </div>

                <div className="space-y-2 rounded-xl border border-gray-200 bg-gray-50 p-3">
                    <label className="label">{tx('variants.increaseByPercent', 'Increase Price %')}</label>
                    <input
                        type="number"
                        step="0.01"
                        value={percentChange}
                        onChange={(e) => setPercentChange(e.target.value)}
                        className="input"
                        placeholder="10"
                    />
                    <button
                        type="button"
                        onClick={() => {
                            onApply({ type: 'increase-percent', value: percentChange })
                            setPercentChange('')
                        }}
                        disabled={noSelection || !percentChange}
                        className="w-full rounded-lg border border-gray-200 px-3 py-2 text-sm font-medium text-gray-700 transition hover:border-gray-300 hover:bg-white disabled:cursor-not-allowed disabled:opacity-50"
                    >
                        {noSelection ? tx('variants.applyIncrease', 'Increase Selected') : tx('variants.increaseCountSelected', 'Increase {{count}} Selected').replace('{{count}}', selectedCount)}
                    </button>
                </div>

                <div className="space-y-2 rounded-xl border border-gray-200 bg-gray-50 p-3">
                    <label className="label">{tx('variants.setInventory', 'Set Inventory')}</label>
                    <select
                        value={selectedWarehouseId}
                        onChange={(e) => onWarehouseChange(e.target.value)}
                        className="input"
                    >
                        {warehouses.map((warehouse) => (
                            <option key={warehouse.id} value={warehouse.id}>
                                {warehouse.name}
                            </option>
                        ))}
                    </select>
                    <input
                        type="number"
                        step="1"
                        value={setInventory}
                        onChange={(e) => setSetInventory(e.target.value)}
                        className="input"
                        placeholder="0"
                    />
                    <button
                        type="button"
                        onClick={() => {
                            onApply({ type: 'set-inventory', value: setInventory })
                            setSetInventory('')
                        }}
                        disabled={noSelection || !setInventory || !selectedWarehouseId}
                        className="w-full rounded-lg border border-gray-200 px-3 py-2 text-sm font-medium text-gray-700 transition hover:border-gray-300 hover:bg-white disabled:cursor-not-allowed disabled:opacity-50"
                    >
                        {applyCaption}
                    </button>
                </div>

                <div className="space-y-2 rounded-xl border border-gray-200 bg-gray-50 p-3">
                    <label className="label">{tx('variants.setWeight', 'Set Weight')}</label>
                    <input
                        type="number"
                        step="0.001"
                        value={setWeight}
                        onChange={(e) => setSetWeight(e.target.value)}
                        className="input"
                        placeholder="0.000"
                    />
                    <button
                        type="button"
                        onClick={() => {
                            onApply({ type: 'set-weight', value: setWeight })
                            setSetWeight('')
                        }}
                        disabled={noSelection || !setWeight}
                        className="w-full rounded-lg border border-gray-200 px-3 py-2 text-sm font-medium text-gray-700 transition hover:border-gray-300 hover:bg-white disabled:cursor-not-allowed disabled:opacity-50"
                    >
                        {applyCaption}
                    </button>
                </div>

                <div className="space-y-2 rounded-xl border border-gray-200 bg-gray-50 p-3">
                    <label className="label">{tx('variants.identityTools', 'Identity Tools')}</label>
                    <button
                        type="button"
                        onClick={() => onApply({ type: 'generate-sku' })}
                        disabled={noSelection}
                        className="w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm font-medium text-gray-700 transition hover:bg-gray-100 disabled:cursor-not-allowed disabled:opacity-50"
                    >
                        {noSelection ? tx('variants.generateSku', 'Generate SKU') : tx('variants.generateSkuCountSelected', 'Generate SKU for {{count}} Selected').replace('{{count}}', selectedCount)}
                    </button>
                    <button
                        type="button"
                        onClick={() => onApply({ type: 'generate-barcode' })}
                        disabled={noSelection}
                        className="w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm font-medium text-gray-700 transition hover:bg-gray-100 disabled:cursor-not-allowed disabled:opacity-50"
                    >
                        {noSelection ? tx('variants.generateBarcode', 'Generate Barcode') : tx('variants.generateBarcodeCountSelected', 'Generate Barcode for {{count}} Selected').replace('{{count}}', selectedCount)}
                    </button>
                </div>

                <div className="space-y-2 rounded-xl border border-gray-200 bg-gray-50 p-3">
                    <label className="label">{tx('variants.variantStatus', 'Variant Status')}</label>
                    <div className="grid grid-cols-2 gap-2">
                        <button
                            type="button"
                            onClick={() => onApply({ type: 'set-active', value: true })}
                            disabled={noSelection}
                            className="rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-sm font-medium text-emerald-700 transition hover:bg-emerald-100 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                            {enableCaption}
                        </button>
                        <button
                            type="button"
                            onClick={() => onApply({ type: 'set-active', value: false })}
                            disabled={noSelection}
                            className="rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm font-medium text-gray-700 transition hover:bg-gray-100 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                            {disableCaption}
                        </button>
                    </div>
                    <button
                        type="button"
                        onClick={() => onApply({ type: 'delete-selected' })}
                        disabled={noSelection}
                        className="w-full rounded-lg border border-red-200 bg-white px-3 py-2 text-sm font-medium text-red-600 transition hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-50"
                    >
                        {deleteCaption}
                    </button>
                </div>

                <div className="space-y-2 rounded-xl border border-gray-200 bg-gray-50 p-3">
                    <label className="label">{tx('variants.variantImages', 'Variant Images')}</label>
                    <button
                        type="button"
                        onClick={() => setImagePickerOpen(true)}
                        disabled={noSelection || !productId}
                        className="w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm font-medium text-gray-700 transition hover:bg-gray-100 disabled:cursor-not-allowed disabled:opacity-50"
                    >
                        {noSelection ? tx('variants.setImage', 'Set Image') : tx('variants.setImageCountSelected', 'Set Image for {{count}} Selected').replace('{{count}}', selectedCount)}
                    </button>
                    <button
                        type="button"
                        onClick={() => onApply({ type: 'set-image', value: null })}
                        disabled={noSelection}
                        className="w-full rounded-lg border border-red-200 bg-white px-3 py-2 text-sm font-medium text-red-600 transition hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-50"
                    >
                        {noSelection ? tx('variants.clearImage', 'Clear Image') : tx('variants.clearImageCountSelected', 'Clear Image for {{count}} Selected').replace('{{count}}', selectedCount)}
                    </button>
                </div>
            </div>

            <VariantMediaPicker
                open={imagePickerOpen}
                productId={productId}
                selectedMediaId={null}
                onClose={() => setImagePickerOpen(false)}
                onSelect={(mediaItem) => {
                    onApply({ type: 'set-image', value: mediaItem?.id || null })
                    setImagePickerOpen(false)
                }}
            />
        </div>
    )
}

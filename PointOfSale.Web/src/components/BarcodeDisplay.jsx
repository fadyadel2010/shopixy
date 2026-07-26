import { useEffect, useRef } from 'react'
import JsBarcode from 'jsbarcode'
import { useTranslation } from 'react-i18next'

export default function BarcodeDisplay({ barcode, type = 'EAN13', onClose }) {
    const { t } = useTranslation()
    const barcodeRef = useRef(null)

    useEffect(() => {
        if (barcodeRef.current && barcode) {
            try {
                JsBarcode(barcodeRef.current, barcode, {
                    format: type,
                    width: 2,
                    height: 100,
                    displayValue: true,
                    fontSize: 16,
                    margin: 10
                })
            } catch (error) {
                console.error('Error generating barcode:', error)
            }
        }
    }, [barcode, type])

    const handlePrint = () => {
        if (!barcodeRef.current) return

        // Get the SVG element and serialize it
        const svgElement = barcodeRef.current
        const svgData = new XMLSerializer().serializeToString(svgElement)
        const svgBlob = new Blob([svgData], { type: 'image/svg+xml;charset=utf-8' })
        const svgUrl = URL.createObjectURL(svgBlob)

        const printWindow = window.open('', '', 'width=600,height=400')

        printWindow.document.write(`
            <html>
                <head>
                    <title>${t('products.printBarcode')} - ${barcode}</title>
                    <style>
                        body {
                            display: flex;
                            justify-content: center;
                            align-items: center;
                            height: 100vh;
                            margin: 0;
                            font-family: Arial, sans-serif;
                        }
                        .barcode-container {
                            text-align: center;
                        }
                        svg {
                            max-width: 100%;
                        }
                        @media print {
                            body {
                                margin: 0;
                            }
                        }
                    </style>
                </head>
                <body>
                    <div class="barcode-container">
                        ${svgData}
                    </div>
                    <script>
                        window.onload = function() {
                            window.print();
                            window.onafterprint = function() {
                                window.close();
                            }
                        }
                    </script>
                </body>
            </html>
        `)
        printWindow.document.close()

        // Clean up the blob URL
        setTimeout(() => URL.revokeObjectURL(svgUrl), 100)
    }

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4">
            <div className="bg-white rounded-lg shadow-xl max-w-lg w-full">
                <div className="p-6">
                    <div className="flex justify-between items-start mb-6">
                        <div>
                            <h3 className="text-2xl font-bold text-gray-900">{t('products.barcode')}</h3>
                            <p className="text-sm text-gray-500 mt-1">{barcode}</p>
                        </div>
                        <button
                            onClick={onClose}
                            className="text-gray-400 hover:text-gray-600"
                        >
                            <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    </div>

                    <div className="card bg-gray-50 flex items-center justify-center p-8 mb-6">
                        <svg ref={barcodeRef}></svg>
                    </div>

                    <div className="flex gap-3">
                        <button
                            onClick={onClose}
                            className="flex-1 btn-secondary"
                        >
                            {t('common.close')}
                        </button>
                        <button
                            onClick={handlePrint}
                            className="flex-1 btn-primary"
                        >
                            <svg className="w-5 h-5 inline-block mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17 17h2a2 2 0 002-2v-4a2 2 0 00-2-2H5a2 2 0 00-2 2v4a2 2 0 002 2h2m2 4h6a2 2 0 002-2v-4a2 2 0 00-2-2H9a2 2 0 00-2 2v4a2 2 0 002 2zm8-12V5a2 2 0 00-2-2H9a2 2 0 00-2 2v4h10z" />
                            </svg>
                            {t('products.printBarcode')}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    )
}

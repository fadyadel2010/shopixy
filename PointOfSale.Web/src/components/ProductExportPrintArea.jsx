import { forwardRef } from 'react'

// Phase 3 — print-only "Products Report" view consumed by react-to-print.
//
// PDF export goes through the browser print pipeline (the established report
// pattern in this project — see BankStatement/SalesReturnReport) because the
// browser renders Arabic shaping + RTL correctly, which the bundled jsPDF
// cannot do without embedded Arabic fonts. The node is hidden on screen;
// react-to-print clones it into a print iframe, so the hidden ancestor never
// affects the printed output.
//
// Styling is plain CSS inside the node (not Tailwind utilities) so the print
// iframe renders it reliably. <thead> repeats automatically on every page.
const ProductExportPrintArea = forwardRef(function ProductExportPrintArea(
    {
        rows,
        headers,
        title,
        storeName,
        scopeLabel,
        exportedAt,
        countLabel,
        filtersSummary,
        isRTL,
        dir,
    },
    ref,
) {
    const textAlign = isRTL ? 'right' : 'left'

    return (
        <div ref={ref} dir={dir} style={{ color: '#111827' }}>
            <style>{`
                @page { size: A4 landscape; margin: 12mm; }
                .products-export-report { font-family: 'Noto Kufi Arabic', 'Segoe UI', Tahoma, Arial, sans-serif; }
                .products-export-report h1 { font-size: 18px; font-weight: 700; margin: 0 0 2px; }
                .products-export-report .meta { font-size: 10px; color: #4b5563; margin: 1px 0; }
                .products-export-report table {
                    width: 100%;
                    border-collapse: collapse;
                    font-size: 10px;
                    margin-top: 10px;
                }
                .products-export-report th,
                .products-export-report td {
                    border: 1px solid #d1d5db;
                    padding: 5px 7px;
                    text-align: ${textAlign};
                    word-break: break-word;
                }
                .products-export-report thead th {
                    background: #f3f4f6;
                    font-weight: 700;
                }
                .products-export-report tbody tr { page-break-inside: avoid; }
            `}</style>

            <div className="products-export-report">
                <h1>{title}</h1>
                {storeName && <p className="meta">{storeName}</p>}
                <p className="meta">
                    {scopeLabel} · {exportedAt} · {countLabel}
                </p>
                {filtersSummary && <p className="meta">{filtersSummary}</p>}

                <table>
                    <thead>
                        <tr>
                            {headers.map((header) => (
                                <th key={header}>{header}</th>
                            ))}
                        </tr>
                    </thead>
                    <tbody>
                        {rows.map((row) => (
                            <tr key={row.id}>
                                <td>{row.name}</td>
                                <td>{row.sku}</td>
                                <td>{row.typeLabel}</td>
                                <td>{row.category}</td>
                                <td>{row.priceDisplay}</td>
                                <td>{row.stockDisplay}</td>
                                <td>{row.statusLabel}</td>
                                <td>{row.publicationLabel}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </div>
    )
})

export default ProductExportPrintArea

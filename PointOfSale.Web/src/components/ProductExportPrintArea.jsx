import { forwardRef } from 'react'

const PRINT_PAGE_ROW_LIMIT = 7

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
// iframe renders it reliably. Rows are split into deterministic page-sized
// tables because Chromium can otherwise fragment a long RTL table incorrectly.
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
    const exportRows = Array.isArray(rows) ? rows : []
    const rowPages = []
    for (let index = 0; index < exportRows.length; index += PRINT_PAGE_ROW_LIMIT) {
        rowPages.push(exportRows.slice(index, index + PRINT_PAGE_ROW_LIMIT))
    }
    if (rowPages.length === 0) rowPages.push([])

    return (
        <div ref={ref} dir={dir} style={{ color: '#111827' }}>
            <style>{`
                @page { size: A4 landscape; margin: 12mm; }
                @media print {
                    html, body { margin: 0 !important; padding: 0 !important; }
                }
                .products-export-report { font-family: 'Noto Kufi Arabic', 'Segoe UI', Tahoma, Arial, sans-serif; }
                .products-export-report h1 { font-size: 18px; font-weight: 700; margin: 0 0 2px; }
                .products-export-report .meta { font-size: 10px; color: #4b5563; margin: 1px 0; }
                .products-export-report table {
                    width: 100%;
                    table-layout: fixed;
                    border-collapse: collapse;
                    font-size: 10px;
                    margin-top: 10px;
                }
                .products-export-report th,
                .products-export-report td {
                    border: 1px solid #d1d5db;
                    padding: 5px 7px;
                    text-align: ${textAlign};
                    vertical-align: top;
                    white-space: normal;
                    overflow-wrap: anywhere;
                    word-break: normal;
                }
                .products-export-report thead th {
                    background: #f3f4f6;
                    font-weight: 700;
                }
                .products-export-report thead { display: table-header-group; }
                .products-export-report tbody { display: table-row-group; }
                .products-export-report tbody tr {
                    break-inside: avoid;
                    page-break-inside: avoid;
                }
                .products-export-report .products-export-page {
                    display: flow-root;
                    box-sizing: border-box;
                    break-inside: avoid;
                    page-break-inside: avoid;
                }
                .products-export-report .products-export-page:not(:last-child) {
                    break-after: page;
                    page-break-after: always;
                }
                .products-export-report .products-export-page + .products-export-page {
                    padding-top: 16mm;
                }
                .products-export-report .bidi-text { unicode-bidi: plaintext; }
                .products-export-report .code-cell,
                .products-export-report .number-cell {
                    direction: ltr;
                    unicode-bidi: isolate;
                }
                .products-export-report .stock-state {
                    display: block;
                    margin-top: 2px;
                    color: #4b5563;
                    font-size: 9px;
                }
            `}</style>

            <div className="products-export-report">
                {rowPages.map((pageRows, pageIndex) => (
                    <div className="products-export-page" key={`page-${pageIndex}`}>
                        {pageIndex === 0 && (
                            <>
                                <h1>{title}</h1>
                                {storeName && <p className="meta">{storeName}</p>}
                                <p className="meta">
                                    {scopeLabel} · {exportedAt} · {countLabel}
                                </p>
                                {filtersSummary && <p className="meta">{filtersSummary}</p>}
                            </>
                        )}
                        <table>
                            <colgroup>
                                <col style={{ width: '24%' }} />
                                <col style={{ width: '14%' }} />
                                <col style={{ width: '11%' }} />
                                <col style={{ width: '15%' }} />
                                <col style={{ width: '10%' }} />
                                <col style={{ width: '11%' }} />
                                <col style={{ width: '7%' }} />
                                <col style={{ width: '8%' }} />
                            </colgroup>
                            <thead>
                                <tr>
                                    {headers.map((header) => (
                                        <th key={header}>{header}</th>
                                    ))}
                                </tr>
                            </thead>
                            <tbody>
                                {pageRows.map((row, rowIndex) => (
                                    <tr key={`${pageIndex}-${row.id ?? rowIndex}`}>
                                        <td className="bidi-text" dir="auto">{row.name}</td>
                                        <td className="code-cell" dir="ltr">{row.sku}</td>
                                        <td>{row.typeLabel}</td>
                                        <td className="bidi-text" dir="auto">{row.category}</td>
                                        <td className="number-cell" dir="ltr">{row.priceDisplay}</td>
                                        <td className="number-cell" dir="ltr">
                                            {row.stockDisplay}
                                            {row.stockStateLabel && row.stockStateLabel !== '—' && (
                                                <span className="stock-state">{row.stockStateLabel}</span>
                                            )}
                                        </td>
                                        <td>{row.statusLabel}</td>
                                        <td>{row.publicationLabel}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                ))}
            </div>
        </div>
    )
})

export default ProductExportPrintArea

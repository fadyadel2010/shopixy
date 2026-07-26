export const loadExcelModule = async () => import('xlsx')

export const createPdfDocument = async (...args) => {
    const [{ default: jsPDF }] = await Promise.all([
        import('jspdf'),
        import('jspdf-autotable'),
    ])

    return new jsPDF(...args)
}

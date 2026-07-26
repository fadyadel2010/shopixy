import { describe, expect, it } from 'vitest';
import * as fs from 'fs';
import * as path from 'path';

const WEB_SRC = path.resolve(__dirname, '..');
const REPO_ROOT = path.resolve(__dirname, '../../..');

const warehouseFrontendFiles = [
    'pages/Warehouses.jsx',
    'pages/Inventory.jsx',
    'pages/WarehouseOpeningStock.jsx',
    'pages/StockTransfers.jsx',
    'pages/SalesReturns.jsx',
    'pages/PurchaseReturns.jsx',
    'pages/InventoryValuation.jsx',
    'pages/ProductMovementReport.jsx',
    'components/Sidebar.jsx',
    'services/posDataService.js',
];

function readWeb(relativePath: string): string {
    return fs.readFileSync(path.join(WEB_SRC, relativePath), 'utf-8');
}

function readRepo(relativePath: string): string {
    return fs.readFileSync(path.join(REPO_ROOT, relativePath), 'utf-8');
}

function combinedWarehouseFrontend(): string {
    return warehouseFrontendFiles.map(readWeb).join('\n');
}

describe('Warehouse frontend certification', () => {
    it('reuses shared frontend infrastructure instead of defining duplicate clients, dialogs, or lookup controls', () => {
        const source = combinedWarehouseFrontend();
        const inventory = readWeb('pages/Inventory.jsx');
        const openingStock = readWeb('pages/WarehouseOpeningStock.jsx');
        const transfers = readWeb('pages/StockTransfers.jsx');
        const salesReturns = readWeb('pages/SalesReturns.jsx');
        const purchaseReturns = readWeb('pages/PurchaseReturns.jsx');

        expect(source).toContain("from '../services/api'");
        expect(source).toContain("from '../services/posDataService'");
        expect(openingStock).toContain("from '../common/confirm/ConfirmDialogProvider'");
        expect(transfers).toContain("from '../common/confirm/ConfirmDialogProvider'");
        expect(salesReturns).toContain("from '../common/confirm/ConfirmDialogProvider'");
        expect(transfers).toContain("from '../components/AsyncEntitySelect'");
        expect(salesReturns).toContain("from '../components/AsyncEntitySelect'");
        expect(purchaseReturns).toContain("from '../components/AsyncEntitySelect'");
        expect(inventory).toContain('isRequestCanceled');
        expect(source).not.toMatch(/axios\.create|from 'axios'|\bfetch\s*\(/);
        expect(source).not.toMatch(/new WebSocket|EventSource|localStorage\.setItem|sessionStorage\.setItem/);
    });

    it('consumes certified warehouse APIs and does not call inventory persistence internals directly', () => {
        const source = combinedWarehouseFrontend();
        const posDataService = readWeb('services/posDataService.js');
        const inventory = readWeb('pages/Inventory.jsx');
        const openingStock = readWeb('pages/WarehouseOpeningStock.jsx');
        const transfers = readWeb('pages/StockTransfers.jsx');
        const salesReturns = readWeb('pages/SalesReturns.jsx');
        const purchaseReturns = readWeb('pages/PurchaseReturns.jsx');

        expect(posDataService).toContain("api.get(`/warehouses/${warehouseId}/stock`");
        expect(posDataService).toContain("api.get('/warehouses/opening-stock'");
        expect(inventory).toContain("api.post('/warehouses/adjust-stock'");
        expect(inventory).toContain("api.post('/warehouses/transfer-stock'");
        expect(openingStock).toContain("api.post('/warehouses/opening-stock'");
        expect(transfers).toContain("api.get(`/StockTransfers${params}`)");
        expect(transfers).toContain("api.post('/StockTransfers'");
        expect(salesReturns).toContain('api.get(`/salesreturns?page=');
        expect(salesReturns).toContain("api.post('/salesreturns'");
        expect(purchaseReturns).toContain('api.get(`/purchasereturns?page=');
        expect(purchaseReturns).toContain("api.post('/purchasereturns'");

        expect(source).not.toMatch(/\/api\/inventory|InventoryReservations|ProductVariantStock|StockMovements|AdjustStockAsync|TransferStockAsync|ReserveOrderItemAsync/);
    });

    it('renders warehouse list, inventory, reservation, allocation, health, status, and alerts from API data', () => {
        const warehouses = readWeb('pages/Warehouses.jsx');
        const inventory = readWeb('pages/Inventory.jsx');
        const valuation = readWeb('pages/InventoryValuation.jsx');
        const movement = readWeb('pages/ProductMovementReport.jsx');

        expect(warehouses).toContain('warehouses.map((warehouse)');
        expect(warehouses).toContain('warehouse.isActive');
        expect(warehouses).toContain('warehouse.isDefault');
        expect(inventory).toContain('filteredStockLevels.map((stock)');
        expect(inventory).toContain('stock.reservedQuantity');
        expect(inventory).toContain('stock.availableQuantity');
        expect(inventory).toContain('getWarehouseStockLevels');
        expect(valuation).toContain('data?.warehouseValuations.map');
        expect(valuation).toContain('data.lowStockItems.map');
        expect(movement).toContain('reportData.movements');

        expect(`${warehouses}\n${inventory}\n${valuation}`).not.toMatch(/Main Warehouse|Warehouse A|Warehouse B|defaultWarehouseId|hardcodedWarehouse/i);
    });

    it('presents opening balance as inventory admission and not direct frontend mutation', () => {
        const openingStock = readWeb('pages/WarehouseOpeningStock.jsx');
        const en = readWeb('i18n/locales/en.json');
        const certification = readRepo('docs/architecture/WAREHOUSE_2_5_UX_NAVIGATION_CERTIFICATION.md');

        expect(en).toContain('"warehouseStockTitle": "Inventory Admission - Warehouses"');
        expect(en).toContain('"saveOpeningStock": "Admit Inventory"');
        expect(en).toContain('backend records the ledger movement');
        expect(en).toContain('does not directly mutate inventory');
        expect(openingStock).toContain("api.get(`/warehouses/opening-stock?warehouseId=${warehouseId}`)");
        expect(openingStock).toContain("api.post('/warehouses/opening-stock'");
        expect(certification).toContain('Opening Balance is presented as Inventory Admission');
        expect(en).not.toContain('Save Opening Stock');
        expect(en).not.toContain('Opening stock saved successfully');
    });

    it('keeps reservation and allocation UI presentation-only', () => {
        const inventory = readWeb('pages/Inventory.jsx');
        const transfers = readWeb('pages/StockTransfers.jsx');
        const source = `${inventory}\n${transfers}`;

        expect(inventory).toContain('stock.reservedQuantity');
        expect(inventory).toContain('stock.availableQuantity');
        expect(inventory).toContain('fetchStockLevels(selectedWarehouse');
        expect(transfers).toContain('fromWarehouseId');
        expect(transfers).toContain('toWarehouseId');
        expect(transfers).toContain('handleApprove');
        expect(transfers).toContain('handleReject');
        expect(transfers).toContain('handleComplete');

        expect(source).not.toMatch(/availableQuantity\s*[:=]\s*[^;\n]*quantity\s*-/);
        expect(source).not.toMatch(/reservedQuantity\s*[:=]|allocate|selectBestWarehouse|chooseWarehouse|warehouseSelection/i);
    });

    it('keeps receiving, transfer, returns, and health presentation outside frontend business authority', () => {
        const openingStock = readWeb('pages/WarehouseOpeningStock.jsx');
        const transfers = readWeb('pages/StockTransfers.jsx');
        const salesReturns = readWeb('pages/SalesReturns.jsx');
        const purchaseReturns = readWeb('pages/PurchaseReturns.jsx');
        const valuation = readWeb('pages/InventoryValuation.jsx');
        const source = `${openingStock}\n${transfers}\n${salesReturns}\n${purchaseReturns}\n${valuation}`;

        expect(openingStock).toContain('setSaving(true)');
        expect(transfers).toContain('statusFilter');
        expect(transfers).toContain('pendingActions');
        expect(salesReturns).toContain('warehouseId');
        expect(purchaseReturns).toContain('warehouseId');
        expect(valuation).toContain('lowStockItemsCount');
        expect(valuation).toContain('warehouseValuations');

        expect(source).not.toMatch(/DbContext|SQL|StockMovementType|ledger mutation|mutate inventory/i);
        expect(source).not.toMatch(/refund authority|payment lifecycle|carrier execution|shipment lifecycle/i);
    });

    it('preserves responsive, loading, retry, disabled-action, and accessibility primitives', () => {
        const source = combinedWarehouseFrontend();
        const inventory = readWeb('pages/Inventory.jsx');
        const openingStock = readWeb('pages/WarehouseOpeningStock.jsx');
        const transfers = readWeb('pages/StockTransfers.jsx');
        const sidebar = readWeb('components/Sidebar.jsx');

        expect(source).toContain('overflow-x-auto');
        expect(source).toMatch(/grid-cols-1|md:grid-cols|lg:grid-cols/);
        expect(source).toContain('<label');
        expect(inventory).toContain('common.retry');
        expect(openingStock).toContain('disabled={saving}');
        expect(transfers).toContain('disabled={rowActionsDisabled}');
        expect(transfers).toContain('disabled={isSubmittingRef.current || isSubmitting');
        expect(sidebar).toContain("path: '/warehouses'");
        expect(sidebar).toContain("path: '/opening-balance/warehouse-stock'");
    });

    it('documents the warehouse frontend certification decision', () => {
        const certification = readRepo('docs/architecture/WAREHOUSE_3_FRONTEND.md');

        [
            'Warehouse Frontend Certified? **Yes.**',
            'Component Reuse Certified? **Yes.**',
            'API Consumption Certified? **Yes.**',
            'Reservation UI Certified? **Yes.**',
            'Allocation UI Certified? **Yes.**',
            'Receiving UI Certified? **Yes.**',
            'Transfer UI Certified? **Yes.**',
            'Returns UI Certified? **Yes.**',
            'Warehouse Health UI Certified? **Yes.**',
            'Dynamic Rendering Certified? **Yes.**',
            'Architecture preserved? **Yes.**',
            'Production-certifiable frontend? **Yes',
        ].forEach((expected) => {
            expect(certification).toContain(expected);
        });
    });
});

namespace PointOfSale.Core.Interfaces.Accounting;

/// <summary>
/// Marker interface for services that MUST read ONLY from the immutable ledger.
/// 
/// PHASE C3: SOURCE ENFORCEMENT GUARDS
/// 
/// Services implementing this interface are subject to architectural enforcement tests
/// that verify they do NOT access business tables (SalesInvoices, PurchaseInvoices,
/// Customers, Suppliers, Products, etc.).
/// 
/// ALLOWED DbSets:
/// - Accounts
/// - JournalEntries  
/// - LedgerEntries
/// 
/// FORBIDDEN DbSets:
/// - SalesInvoices, PurchaseInvoices, Customers, Suppliers, Products,
///   StockMovements, legacy treasury tables, and other mutable business tables.
/// 
/// Any violation will cause architecture tests to FAIL during CI/CD.
/// </summary>
public interface ILedgerOnlyService
{
    // This is a marker interface - no methods required.
    // Used for compile-time and reflection-based enforcement.
}

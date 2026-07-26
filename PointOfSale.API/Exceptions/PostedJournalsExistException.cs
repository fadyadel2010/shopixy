namespace PointOfSale.API.Exceptions;

/// <summary>
/// Exception thrown when attempting to delete a sales invoice that has posted accounting journals.
/// The journals must be reversed before the invoice can be safely deleted.
/// </summary>
public class PostedJournalsExistException : Exception
{
    public string InvoiceNumber { get; }
    public Guid InvoiceId { get; }
    
    public PostedJournalsExistException(string invoiceNumber, Guid invoiceId) 
        : base($"Invoice {invoiceNumber} still has posted accounting journals and cannot be deleted safely. Reverse the ledger journals before deleting the document.")
    {
        InvoiceNumber = invoiceNumber;
        InvoiceId = invoiceId;
    }
}

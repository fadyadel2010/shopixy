using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Customer entity with balance tracking
/// </summary>
public class Customer : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this customer.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxNumber { get; set; }
    
    /// <summary>
    /// Current balance (positive = customer owes us, negative = we owe customer)
    /// </summary>
    public decimal Balance { get; set; }
    
    /// <summary>
    /// Opening balance amount (initial balance when starting to use the system)
    /// </summary>
    public decimal? OpeningBalanceAmount { get; set; }
    
    /// <summary>
    /// Balance type: Debit (customer owes us) or Credit (we owe customer)
    /// </summary>
    public string? BalanceType { get; set; } // "Debit" or "Credit"
    
    /// <summary>
    /// Date of opening balance
    /// </summary>
    public DateTime? OpeningBalanceDate { get; set; }
    
    public string? Notes { get; set; }
    
    // Navigation properties
    public ICollection<SalesInvoice> SalesInvoices { get; set; } = new List<SalesInvoice>();
    public ICollection<CustomerTransaction> Transactions { get; set; } = new List<CustomerTransaction>();
    public ICollection<CustomerAddress> Addresses { get; set; } = new List<CustomerAddress>();
}

public class CustomerAddress : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public Guid? CurrentVersionId { get; set; }
    public CustomerAddressVersion? CurrentVersion { get; set; }
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
    public int Version { get; set; }
    public ICollection<CustomerAddressVersion> Versions { get; set; } = new List<CustomerAddressVersion>();
}

public class CustomerAddressVersion : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CustomerAddressId { get; set; }
    public CustomerAddress CustomerAddress { get; set; } = null!;
    public int Version { get; set; }
    public Guid AddressAdmissionEvidenceId { get; set; }
    public Guid GeographicCatalogReleaseId { get; set; }
    public Guid CountryPlaceId { get; set; }
    public Guid? AdministrativeAreaPlaceId { get; set; }
    public Guid? LocalityPlaceId { get; set; }
    public Guid? DistrictPlaceId { get; set; }
    public Guid? ZonePlaceId { get; set; }
    public string? CountryDisplayName { get; set; }
    public string? AdministrativeAreaDisplayName { get; set; }
    public string? LocalityDisplayName { get; set; }
    public string? DistrictDisplayName { get; set; }
    public string? ZoneDisplayName { get; set; }
    public string Street { get; set; } = string.Empty;
    public string? Street2 { get; set; }
    public string? Building { get; set; }
    public string? Floor { get; set; }
    public string? Apartment { get; set; }
    public string? Landmark { get; set; }
    public string? PostalCode { get; set; }
    public string? LegacyAddressText { get; set; }
    public DateTime AdmittedAtUtc { get; set; }
    public DateTime SnapshotCreatedAtUtc { get; set; }
    public int SnapshotVersion { get; set; } = 1;
}

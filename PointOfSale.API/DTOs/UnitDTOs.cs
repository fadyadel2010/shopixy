namespace PointOfSale.API.DTOs;

/// <summary>
/// DTO for Unit of Measure
/// </summary>
public class UnitDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// DTO for creating/updating a Unit
/// </summary>
public class CreateUpdateUnitDto
{
    public string Name { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// DTO for Product Unit configuration
/// </summary>
public class ProductUnitDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public string? UnitSymbol { get; set; }
    public decimal ConversionFactor { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? SellingPrice { get; set; }
    public string? Barcode { get; set; }
    public bool IsDefault { get; set; }
    public bool IsBaseUnit { get; set; }
}

/// <summary>
/// DTO for creating/updating a Product Unit
/// </summary>
public class CreateUpdateProductUnitDto
{
    public Guid UnitId { get; set; }
    public decimal ConversionFactor { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? SellingPrice { get; set; }
    public string? Barcode { get; set; }
    public bool IsDefault { get; set; }
    public bool IsBaseUnit { get; set; }
}

/// <summary>
/// DTO for unit selection in transactions
/// </summary>
public class UnitSelectionDto
{
    public Guid UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public string? UnitSymbol { get; set; }
    public decimal ConversionFactor { get; set; }
    public decimal? SuggestedPrice { get; set; }
    public bool IsDefault { get; set; }
    public bool IsBaseUnit { get; set; }
    public string ConversionText { get; set; } = string.Empty; // e.g., "1 Carton = 5 Pieces"
}

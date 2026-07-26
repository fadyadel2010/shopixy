namespace PointOfSale.Core.Enums;

/// <summary>
/// Product publication status for e-commerce visibility
/// </summary>
public enum PublishStatus
{
    /// <summary>Product is in draft mode, not visible</summary>
    Draft = 0,
    
    /// <summary>Product is published and visible online</summary>
    Published = 1,
    
    /// <summary>Product is archived, not visible</summary>
    Archived = 2
}

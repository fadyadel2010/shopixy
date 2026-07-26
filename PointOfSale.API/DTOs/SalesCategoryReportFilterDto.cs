using System;

namespace PointOfSale.API.DTOs;

public class SalesCategoryReportFilterDto
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public Guid? MainCategoryId { get; set; }
    public Guid? SubCategoryId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? SalesRepresentativeId { get; set; }
    public string? PaymentMethod { get; set; }
}

using System.Reflection;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Infrastructure.MongoDB.Documents;
using Xunit;

namespace PointOfSale.Tests.Storefront;

public sealed class StorefrontProductIdentityMappingTests
{
    [Fact]
    public void ProductDetailMapping_PreservesBothLanguagesForNameAndDescription()
    {
        var document = new ProductDocument
        {
            ProductId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Name = "Primary name",
            NameAr = "الاسم العربي",
            Description = "Primary description",
            DescriptionAr = "الوصف العربي",
            Slug = "primary-name"
        };
        var map = typeof(StorefrontProductService).GetMethod(
            "MapToDto",
            BindingFlags.Static | BindingFlags.NonPublic);

        var result = Assert.IsType<ProductReadDto>(map!.Invoke(null, [document]));

        Assert.Equal("Primary name", result.Name);
        Assert.Equal("الاسم العربي", result.NameAr);
        Assert.Equal("Primary description", result.Description);
        Assert.Equal("الوصف العربي", result.DescriptionAr);
    }
}

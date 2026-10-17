using PointOfSale.API.Controllers;

namespace PointOfSale.Tests.Admin;

public sealed class ShippingMethodCountryScopeValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("[\"EG\"]")]
    [InlineData("[ \"EG\", \"SA\", \"AE\" ]")]
    [InlineData("[\"AD\",\"AQ\",\"AX\",\"BQ\",\"GB\",\"PS\",\"SS\",\"US\",\"ZW\"]")]
    public void CreateAndUpdate_AcceptExistingCountryContractWithoutRewritingIt(string? countries)
    {
        foreach (var dto in Dtos(countries))
        {
            Assert.Null(dto.Validate());
            Assert.Equal(countries, dto.AvailableCountries);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("true")]
    [InlineData("\"EG\"")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[1]")]
    [InlineData("[[]]")]
    [InlineData("[\"EG\",null]")]
    [InlineData("[\"EG\",\"EG\"]")]
    [InlineData("[\"ZZ\"]")]
    [InlineData("[\"XX\"]")]
    [InlineData("[\"UK\"]")]
    [InlineData("[\"EU\"]")]
    [InlineData("[\"XK\"]")]
    [InlineData("[\"Egypt\"]")]
    [InlineData("[\"eg\"]")]
    [InlineData("[\" EG \"]")]
    [InlineData("[\"EG\",]")]
    public void CreateAndUpdate_RejectMalformedAndUnassignedCountriesWithoutBroadeningScope(string countries)
    {
        foreach (var dto in Dtos(countries))
        {
            Assert.Contains("AvailableCountries", dto.Validate());
            Assert.Equal(countries, dto.AvailableCountries);
        }
    }

    private static IEnumerable<CreateShippingMethodDto> Dtos(string? countries)
    {
        yield return new CreateShippingMethodDto { Name = "Synthetic shared rates", AvailableCountries = countries };
        yield return new UpdateShippingMethodDto { Name = "Synthetic shared rates", AvailableCountries = countries };
    }
}

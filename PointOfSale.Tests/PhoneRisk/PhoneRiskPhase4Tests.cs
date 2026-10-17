using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Services;

namespace PointOfSale.Tests.PhoneRisk;

public sealed class PhoneRiskPhase4Tests
{
    [Theory]
    [InlineData("123")]
    [InlineData("++201000000001")]
    [InlineData("abc01000000001")]
    public async Task Block_InvalidInput_DoesNotPersistAlternativeIdentity(string phone)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new PhoneRiskProfileService(db,
            new PhoneNumberNormalizer(db, null, NullLogger<PhoneNumberNormalizer>.Instance),
            Mock.Of<IOrderVelocityService>(), NullLogger<PhoneRiskProfileService>.Instance);
        await Assert.ThrowsAsync<ArgumentException>(() => service.BlockAsync(Guid.NewGuid(), phone, "P4QA", null));
        Assert.Empty(await db.PhoneRiskProfiles.IgnoreQueryFilters().ToListAsync());
    }
}

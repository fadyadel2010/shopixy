using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Analytics;
using Xunit;

namespace PointOfSale.Tests.Infrastructure.Analytics
{
    public class PixelValueResolverTests
    {
        private readonly Mock<IPixelTrackingConfigService> _mockConfigService;
        private readonly Mock<IProductPixelOverrideService> _mockProductOverrideService;
        private readonly Mock<ILogger<PixelValueResolver>> _mockLogger;
        private readonly PixelValueResolver _resolver;
        private readonly Guid _tenantId = Guid.NewGuid();

        public PixelValueResolverTests()
        {
            _mockConfigService = new Mock<IPixelTrackingConfigService>();
            _mockProductOverrideService = new Mock<IProductPixelOverrideService>();
            _mockLogger = new Mock<ILogger<PixelValueResolver>>();
            _resolver = new PixelValueResolver(
                _mockConfigService.Object,
                _mockProductOverrideService.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task ResolvePixelValueAsync_SingleValue_NoOverrides_ReturnsActual()
        {
            // Arrange
            _mockConfigService.Setup(s => s.GetConfigAsync(_tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PixelTrackingConfig { IsEnabled = false });

            // Act
            var result = await _resolver.ResolvePixelValueAsync(_tenantId, 100m, PixelProvider.Meta);

            // Assert
            Assert.Equal(100m, result);
        }

        [Fact]
        public async Task ResolvePixelValueAsync_SingleValue_GlobalOverride_Percentage_ReturnsCalculated()
        {
            // Arrange
            _mockConfigService.Setup(s => s.GetConfigAsync(_tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PixelTrackingConfig 
                { 
                    IsEnabled = true,
                    Strategy = PixelOverrideStrategy.Percentage,
                    Value = 80, // 80%
                    AppliesTo = new[] { PixelProvider.Meta }
                });

            // Act
            var result = await _resolver.ResolvePixelValueAsync(_tenantId, 100m, PixelProvider.Meta);

            // Assert
            Assert.Equal(80m, result);
        }

        [Fact]
        public async Task ResolvePixelValueAsync_Items_NoOverrides_ReturnsTotal()
        {
            // Arrange
            var items = new List<OrderItemPixelInfo>
            {
                new OrderItemPixelInfo(Guid.NewGuid(), 50m),
                new OrderItemPixelInfo(Guid.NewGuid(), 50m)
            };

            _mockProductOverrideService.Setup(s => s.GetOverridesForTenantAsync(_tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, ProductPixelOverride>());

            _mockConfigService.Setup(s => s.GetConfigAsync(_tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PixelTrackingConfig { IsEnabled = false });

            // Act
            var result = await _resolver.ResolvePixelValueAsync(_tenantId, items, PixelProvider.Meta);

            // Assert
            Assert.Equal(100m, result);
        }

        [Fact]
        public async Task ResolvePixelValueAsync_Items_ProductOverride_AppliesCorrectly()
        {
            // Arrange
            var prodId1 = Guid.NewGuid();
            var prodId2 = Guid.NewGuid(); // No override

            var items = new List<OrderItemPixelInfo>
            {
                new OrderItemPixelInfo(prodId1, 100m),
                new OrderItemPixelInfo(prodId2, 50m)
            };

            var overrides = new Dictionary<Guid, ProductPixelOverride>
            {
                { 
                    prodId1, 
                    new ProductPixelOverride 
                    { 
                        IsEnabled = true, 
                        Strategy = PixelOverrideStrategy.Fixed, 
                        Value = 10m,
                        ApplyToMeta = true 
                    } 
                }
            };

            _mockProductOverrideService.Setup(s => s.GetOverridesForTenantAsync(_tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(overrides);

            _mockConfigService.Setup(s => s.GetConfigAsync(_tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PixelTrackingConfig { IsEnabled = false });

            // Act
            var result = await _resolver.ResolvePixelValueAsync(_tenantId, items, PixelProvider.Meta);

            // Assert
            // Item 1: 100 -> Fixed 10
            // Item 2: 50 -> Actual 50 (since global disabled or logic dictates mixed = actual)
            // Expect: 10 + 50 = 60
            Assert.Equal(60m, result);
        }

        [Fact]
        public async Task ResolvePixelValueAsync_Items_Mixed_OverrideAndGlobal_GlobalIgnoredIfProductOverridePresent()
        {
            // Logic Check: Implementation says:
            // "If some items have product override, some don't: Overridden items: use product override, Non-overridden: use ACTUAL"
            // This means Global Override is IGNORED for the non-overridden items if at least one product override is applied.
            
            // Arrange
            var prodId1 = Guid.NewGuid();
            var prodId2 = Guid.NewGuid(); 

            var items = new List<OrderItemPixelInfo>
            {
                new OrderItemPixelInfo(prodId1, 100m),
                new OrderItemPixelInfo(prodId2, 100m)
            };

            var overrides = new Dictionary<Guid, ProductPixelOverride>
            {
                { 
                    prodId1, 
                    new ProductPixelOverride 
                    { 
                        IsEnabled = true, 
                        Strategy = PixelOverrideStrategy.Percentage, 
                        Value = 50, // 50%
                        ApplyToMeta = true 
                    } 
                }
            };

            _mockProductOverrideService.Setup(s => s.GetOverridesForTenantAsync(_tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(overrides);

            // Global override enabled (e.g. 10%)
            _mockConfigService.Setup(s => s.GetConfigAsync(_tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PixelTrackingConfig 
                { 
                    IsEnabled = true,
                    Strategy = PixelOverrideStrategy.Percentage,
                    Value = 10,
                    AppliesTo = new[] { PixelProvider.Meta }
                });

            // Act
            var result = await _resolver.ResolvePixelValueAsync(_tenantId, items, PixelProvider.Meta);

            // Assert
            // Item 1: 100 * 0.5 = 50
            // Item 2: 100 (Actual, because Mixed rule says Global is ignored if Product is present)
            // Total: 150
            Assert.Equal(150m, result);
        }

        [Fact]
        public async Task ResolvePixelValueAsync_Items_NoProductOverrides_FallsBackToGlobal()
        {
            // Arrange
            var prodId1 = Guid.NewGuid();
            
            var items = new List<OrderItemPixelInfo>
            {
                new OrderItemPixelInfo(prodId1, 100m)
            };

            _mockProductOverrideService.Setup(s => s.GetOverridesForTenantAsync(_tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, ProductPixelOverride>());

            // Global override enabled (50%)
            _mockConfigService.Setup(s => s.GetConfigAsync(_tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PixelTrackingConfig 
                { 
                    IsEnabled = true,
                    Strategy = PixelOverrideStrategy.Percentage,
                    Value = 50,
                    AppliesTo = new[] { PixelProvider.Meta }
                });

            // Act
            var result = await _resolver.ResolvePixelValueAsync(_tenantId, items, PixelProvider.Meta);

            // Assert
            // No product overrides -> Global Applies
            // 100 * 0.5 = 50
            Assert.Equal(50m, result);
        }
    }
}

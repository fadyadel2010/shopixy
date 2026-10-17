using Microsoft.CodeAnalysis.CSharp;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentLifecyclePartialOwnershipTests
{
    [Theory]
    [InlineData("namespace PointOfSale.Infrastructure.Payments; public partial class PaymentLifecycleService { void Set(Order order) { order.PaymentStatus = PaymentStatus.Settled; } }", true)]
    [InlineData("namespace PointOfSale.Infrastructure { namespace Payments { public partial class PaymentLifecycleService { void Set(Order order) { order.PaymentStatus = PaymentStatus.Settled; } } } }", true)]
    [InlineData("namespace PointOfSale.Infrastructure.Payments; public class CarrierRemittanceService { void Set(Order order) { order.PaymentStatus = PaymentStatus.Settled; } }", false)]
    [InlineData("namespace Other.Payments; public partial class PaymentLifecycleService { void Set(Order order) { order.PaymentStatus = PaymentStatus.Settled; } }", false)]
    [InlineData("namespace PointOfSale.Infrastructure.Payments; public partial class PaymentLifecycleService { class OtherWriter { void Set(Order order) { order.PaymentStatus = PaymentStatus.Settled; } } }", false)]
    [InlineData("namespace PointOfSale.Infrastructure.Payments; public class OtherWriter { /* partial class PaymentLifecycleService */ void Set(Order order) { order.PaymentStatus = PaymentStatus.Settled; } }", false)]
    [InlineData("namespace PointOfSale.Infrastructure.Payments; public class PaymentLifecycleService { void Set(Order order) { order.PaymentStatus = PaymentStatus.Settled; } }", false)]
    public void OwnershipUsesExactCanonicalTopLevelPartial_NotFilenameOrTextAppearance(string source, bool expected)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var position = source.IndexOf("order.PaymentStatus =", StringComparison.Ordinal);
        Assert.True(position >= 0);
        Assert.Equal(expected, PaymentLifecycleTests.IsCanonicalPaymentLifecycleOwner(root, position));
    }

    [Theory]
    [InlineData("PaymentLifecycleService.ManualCodSettlement.cs", true)]
    [InlineData("PaymentLifecycleService.CarrierRemittance.cs", true)]
    public void ActualSettlementSources_HaveDistinctOwnership_WithoutExecutingFinancialOperations(string filename, bool expected)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "PointOfSale.Infrastructure", "Payments", filename));
        var position = source.IndexOf("order.PaymentStatus = PaymentStatus.Settled", StringComparison.Ordinal);
        Assert.True(position >= 0, "Retained settlement assignment must remain visible to the ownership scan.");
        Assert.Equal(expected, PaymentLifecycleTests.IsCanonicalPaymentLifecycleOwner(CSharpSyntaxTree.ParseText(source).GetRoot(), position));
    }

    [Fact]
    public void CarrierFacade_ContainsNoPaymentAssignmentOrInterceptorAuthorization()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "PointOfSale.Infrastructure", "Payments", "CarrierRemittanceService.cs"));
        Assert.DoesNotContain("PaymentStatus =", source);
        Assert.DoesNotContain("EnableAuthorizedContext", source);
        Assert.Contains("lifecycle.RecordCarrierRemittanceAsync", source);
    }
}

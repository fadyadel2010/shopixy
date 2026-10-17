using System.Reflection;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Jobs;

namespace PointOfSale.Tests.Shipping;

public sealed class SqlLogisticsOutboxTypeResolutionTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void RetainedIntegrationType_ResolvesWithoutLoadingCode(bool qualified)
    {
        var type = typeof(FulfillmentDeliveredEvent);
        Assert.Equal(type, Resolve(qualified ? type.AssemblyQualifiedName! : type.FullName!));
    }

    [Fact]
    public void ForeignRetainedAssembly_NeverFallsBackToCurrentTypeOrLoadsCode()
    {
        var before = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.FullName).OrderBy(n => n).ToArray();
        Assert.Null(Resolve(typeof(FulfillmentDeliveredEvent).FullName + ", Synthetic.Unknown.Assembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"));
        Assert.Equal(before, AppDomain.CurrentDomain.GetAssemblies().Select(a => a.FullName).OrderBy(n => n).ToArray());
    }

    private static Type? Resolve(string name) => (Type?)typeof(OutboxPublisherJob)
        .GetMethod("ResolveEventType", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [name]);
}

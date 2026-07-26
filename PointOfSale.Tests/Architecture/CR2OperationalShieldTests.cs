using System.Reflection;
using Xunit;
using PointOfSale.Infrastructure.Jobs;
using PointOfSale.API.Hubs;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Tests.Architecture;

public class CR2OperationalShieldTests
{
    [Fact]
    public void TrustDriftReconciliationJob_MustExist_And_BePublic()
    {
        var jobType = typeof(TrustDriftReconciliationJob);
        Assert.True(jobType.IsPublic, "TrustDriftReconciliationJob must be public");
        Assert.Equal("PointOfSale.Infrastructure.Jobs", jobType.Namespace);
    }

    [Fact]
    public void ElasticsearchDriftReconciliationJob_MustExist_And_BePublic()
    {
        var jobType = typeof(ElasticsearchDriftReconciliationJob);
        Assert.True(jobType.IsPublic, "ElasticsearchDriftReconciliationJob must be public");
        Assert.Equal("PointOfSale.Infrastructure.Jobs", jobType.Namespace);
    }

    [Fact]
    public void ChatHub_Must_Depend_On_Telemetry()
    {
        var hubType = typeof(ChatHub);
        var constructors = hubType.GetConstructors();
        
        // Find constructor that accepts MarketplaceTelemetry
        bool hasTelemetryDependency = constructors.Any(c => 
            c.GetParameters().Any(p => p.ParameterType == typeof(MarketplaceTelemetry)));
            
        Assert.True(hasTelemetryDependency, "ChatHub must have a constructor dependency on MarketplaceTelemetry");
    }

    [Fact]
    public void TrustDriftReconciliationJob_Must_Depend_On_Telemetry()
    {
        var jobType = typeof(TrustDriftReconciliationJob);
        var constructors = jobType.GetConstructors();
        
        bool hasTelemetryDependency = constructors.Any(c => 
            c.GetParameters().Any(p => p.ParameterType == typeof(MarketplaceTelemetry)));
            
        Assert.True(hasTelemetryDependency, "TrustDriftReconciliationJob must have a constructor dependency on MarketplaceTelemetry");
    }
    
    [Fact]
    public void Marketplace_Telemetry_Must_Have_Drift_Metrics()
    {
        var telemetryType = typeof(MarketplaceTelemetry);
        
        var trustDriftProp = telemetryType.GetProperty("TrustDriftCount");
        Assert.NotNull(trustDriftProp);
        
        var esDriftProp = telemetryType.GetProperty("ElasticsearchDriftCount");
        Assert.NotNull(esDriftProp);
        
        var updateMethod = telemetryType.GetMethod("RecordTrustDrift");
        Assert.NotNull(updateMethod);
    }
}

using System.Diagnostics.Metrics;
using System.Reflection;
using PointOfSale.API.Instrumentation;

namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentCertificationClosure7Tests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void OperationalTelemetryAuthority_MustOwnCanonicalEventsAndStableSchema()
    {
        var expectedEvents = new[]
        {
            ManualPaymentOperationalTelemetry.InitializedEventName,
            ManualPaymentOperationalTelemetry.EvidenceSubmittedEventName,
            ManualPaymentOperationalTelemetry.RuntimeRejectedEventName,
            ManualPaymentOperationalTelemetry.ConfigurationRejectedEventName,
            ManualPaymentOperationalTelemetry.CapabilityRejectedEventName,
            ManualPaymentOperationalTelemetry.ChannelRejectedEventName,
            ManualPaymentOperationalTelemetry.EnvironmentRejectedEventName
        };

        Assert.Equal(expectedEvents, ManualPaymentOperationalTelemetry.RequiredEventNames);
        Assert.Contains("manual_payment_initializations_total", ManualPaymentOperationalTelemetry.RequiredMetricNames);
        Assert.Contains("manual_payment_evidence_total", ManualPaymentOperationalTelemetry.RequiredMetricNames);
        Assert.Contains("manual_payment_runtime_rejections_total", ManualPaymentOperationalTelemetry.RequiredMetricNames);
        Assert.Contains("tenant_bucket", CertificationSource.ReadRepoFile("PointOfSale.API/Instrumentation/ManualPaymentOperationalTelemetry.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void OperationalTelemetryAuthority_MustEmitCanonicalMetricOnceAndSuppressDuplicate()
    {
        var measurements = new List<(string Instrument, long Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ManualPaymentOperationalTelemetry.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            measurements.Add((instrument.Name, value)));
        listener.Start();

        using var telemetry = new ManualPaymentOperationalTelemetry();
        var telemetryEvent = new ManualPaymentOperationalTelemetryEvent(
            ManualPaymentOperationalTelemetry.InitializedEventName,
            TenantId,
            "reference_payment_initialization",
            "payment-intent-1",
            Channel: "bank_transfer");

        var first = telemetry.Emit(telemetryEvent);
        var duplicate = telemetry.Emit(telemetryEvent);

        Assert.Equal(ManualPaymentTelemetryEmissionStatus.Emitted, first.Status);
        Assert.Equal(ManualPaymentTelemetryEmissionStatus.SuppressedDuplicate, duplicate.Status);
        Assert.Single(measurements.Where(item => item.Instrument == "manual_payment_initializations_total"));
    }

    [Fact]
    public void OperationalTelemetryAuthority_MustRejectUnknownEventAndInvalidSchema()
    {
        using var telemetry = new ManualPaymentOperationalTelemetry();

        Assert.Throws<InvalidOperationException>(() => telemetry.Emit(new ManualPaymentOperationalTelemetryEvent(
            "ManualPayment.Unknown",
            TenantId,
            "reference_payment_initialization",
            "payment-intent-1",
            Channel: "bank_transfer")));

        Assert.Throws<InvalidOperationException>(() => telemetry.Emit(new ManualPaymentOperationalTelemetryEvent(
            ManualPaymentOperationalTelemetry.RuntimeRejectedEventName,
            TenantId,
            "reference_payment_initialization",
            "payment-intent-1")));
    }

    [Fact]
    public void OperationalTelemetryAuthority_MustBeTheOnlyProductionEmitter()
    {
        var telemetry = typeof(ManualPaymentOperationalTelemetry);
        var publicInstanceMethods = telemetry
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "Dispose", "Emit" }, publicInstanceMethods);

        var application = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");
        var foundation = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentFoundationServiceCollectionExtensions.cs");
        var controllers = string.Concat(
            CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminManualPaymentController.cs"),
            CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PaymentsController.cs"),
            CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PaymentLifecycleController.cs"),
            CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontManualWalletController.cs"));

        Assert.Contains("IManualPaymentOperationalTelemetryAuthority", foundation, StringComparison.Ordinal);
        Assert.Contains("IManualPaymentOperationalTelemetryAuthority", application, StringComparison.Ordinal);
        Assert.Contains("_telemetryAuthority.Emit", application, StringComparison.Ordinal);
        Assert.DoesNotContain("IManualPaymentOperationalTelemetryAuthority", controllers, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualPaymentOperationalTelemetry", controllers, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustKeepTelemetryOutOfRuntimeBridgeProviderAndEvidenceAdapters()
    {
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);
        var bridge = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeBridge.cs");
        var providerAdapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentProviderAdapter.cs");
        var evidenceAdapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentEvidenceAdapter.cs");

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("ManualPaymentOperationalTelemetry", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IManualPaymentOperationalTelemetryAuthority", source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("ManualPaymentOperationalTelemetry", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("IManualPaymentOperationalTelemetryAuthority", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualPaymentOperationalTelemetry", providerAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IManualPaymentOperationalTelemetryAuthority", providerAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualPaymentOperationalTelemetry", evidenceAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IManualPaymentOperationalTelemetryAuthority", evidenceAdapter, StringComparison.Ordinal);
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}

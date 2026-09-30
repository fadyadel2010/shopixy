using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using PointOfSale.Infrastructure.Observability;

namespace PointOfSale.Tests.Diagnostics;

public sealed class NpgsqlDiagnosticsIntegrationTests
{
    [Fact]
    public void AddNpgsqlDiagnostics_RegistersRequiredLifetimesExactlyOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNpgsqlDiagnostics(new ConfigurationBuilder().Build());

        AssertDescriptor<NpgsqlDiagnosticsTelemetry>(services, ServiceLifetime.Singleton);
        AssertDescriptor<RequestPhaseDiagnosticRateLimiter>(services, ServiceLifetime.Singleton);
        AssertDescriptor<NpgsqlDiagnosticLogRateLimiter>(services, ServiceLifetime.Singleton);
        AssertDescriptor<NpgsqlLifecycleDiagnosticInterceptor>(services, ServiceLifetime.Scoped);
        AssertDescriptor<NpgsqlConnectionLifecycleDiagnosticInterceptor>(services, ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var telemetry = provider.GetRequiredService<NpgsqlDiagnosticsTelemetry>();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.Same(telemetry, first.ServiceProvider.GetRequiredService<NpgsqlDiagnosticsTelemetry>());
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<NpgsqlLifecycleDiagnosticInterceptor>(),
            second.ServiceProvider.GetRequiredService<NpgsqlLifecycleDiagnosticInterceptor>());
    }

    [Fact]
    public void DefaultOptions_EnableInstrumentationButDisableDetailedLogging()
    {
        var npgsql = new NpgsqlDiagnosticsOptions();
        var requests = new RequestPhaseDiagnosticsOptions();

        Assert.True(npgsql.Enabled);
        Assert.False(npgsql.SlowCommandLoggingEnabled);
        Assert.False(npgsql.CaptureCommandTextInActivities);
        Assert.False(npgsql.CaptureConnectionIdentityInActivities);
        Assert.Equal(10, npgsql.MaxSlowCommandLogsPerSecond);
        Assert.False(requests.Enabled);
        Assert.Equal(10, requests.MaxRecordsPerSecond);
    }

    [Fact]
    public void InvalidConfiguration_IsRejectedWhenOptionsAreResolved()
    {
        var values = new Dictionary<string, string?>
        {
            ["NpgsqlDiagnostics:SlowCommandThresholdMs"] = "1",
            ["RequestPhaseDiagnostics:MaxRecordsPerSecond"] = "1000"
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNpgsqlDiagnostics(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<NpgsqlDiagnosticsOptions>>().Value);
        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RequestPhaseDiagnosticsOptions>>().Value);
    }

    [Fact]
    public void DisabledInstrumentation_IsANoOp()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var interceptor = new NpgsqlLifecycleDiagnosticInterceptor(
            NullLogger<NpgsqlLifecycleDiagnosticInterceptor>.Instance,
            telemetry,
            Options.Create(new NpgsqlDiagnosticsOptions { Enabled = false }));
        using var capture = new NpgsqlLifecycleDiagnosticActivityTests.ActivityCapture();
        var command = NpgsqlLifecycleDiagnosticActivityTests.NewNpgsqlCommand("SELECT 1");
        var commandId = Guid.NewGuid();

        interceptor.ScalarExecuting(command, NpgsqlDiagnosticEventData.Start(command, commandId, DbCommandMethod.ExecuteScalar), default);
        interceptor.ScalarExecuted(
            command,
            NpgsqlDiagnosticEventData.Executed(command, commandId, TimeSpan.FromMilliseconds(3), DbCommandMethod.ExecuteScalar),
            1);

        Assert.Empty(capture.Started);
        Assert.Equal(0, interceptor.TrackedCommandCount);
        Assert.Equal(0, telemetry.GetSnapshot().ExecutingCommands);
    }

    [Fact]
    public void SourceRemainsUsableAfterAnInterceptorScopeIsDisposed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNpgsqlDiagnostics(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            _ = scope.ServiceProvider.GetRequiredService<NpgsqlLifecycleDiagnosticInterceptor>().ActivitySource;
        }

        using var capture = new NpgsqlLifecycleDiagnosticActivityTests.ActivityCapture();
        using var nextScope = provider.CreateScope();
        var interceptor = nextScope.ServiceProvider.GetRequiredService<NpgsqlLifecycleDiagnosticInterceptor>();
        var command = NpgsqlLifecycleDiagnosticActivityTests.NewNpgsqlCommand("SELECT 1");
        var commandId = Guid.NewGuid();
        interceptor.ScalarExecuting(command, NpgsqlDiagnosticEventData.Start(command, commandId, DbCommandMethod.ExecuteScalar), default);
        interceptor.ScalarExecuted(
            command,
            NpgsqlDiagnosticEventData.Executed(command, commandId, TimeSpan.FromMilliseconds(2), DbCommandMethod.ExecuteScalar),
            1);

        Assert.Single(capture.Stopped);
        Assert.Same(NpgsqlLifecycleDiagnosticInterceptor.SharedActivitySource, interceptor.ActivitySource);
    }

    [Fact]
    public void CommandCancellation_ReleasesStateAndMarksTheActivity()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var interceptor = new NpgsqlLifecycleDiagnosticInterceptor(
            NullLogger<NpgsqlLifecycleDiagnosticInterceptor>.Instance,
            telemetry);
        using var capture = new NpgsqlLifecycleDiagnosticActivityTests.ActivityCapture();
        var command = NpgsqlLifecycleDiagnosticActivityTests.NewNpgsqlCommand("SELECT pg_sleep(1)");
        var commandId = Guid.NewGuid();
        interceptor.ScalarExecuting(command, NpgsqlDiagnosticEventData.Start(command, commandId, DbCommandMethod.ExecuteScalar), default);

        interceptor.CommandCanceled(
            command,
            NpgsqlDiagnosticEventData.Cancelled(command, commandId, TimeSpan.FromMilliseconds(25), DbCommandMethod.ExecuteScalar));

        var activity = Assert.Single(capture.Stopped);
        Assert.Equal(true, NpgsqlLifecycleDiagnosticActivityTests.Tag(activity, "db.command.cancelled"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(0, interceptor.TrackedCommandCount);
        Assert.Equal(0, telemetry.GetSnapshot().ExecutingCommands);
    }

    [Fact]
    public void NoActivityListener_CommandTrackingAndMetricsRemainCorrect()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var interceptor = new NpgsqlLifecycleDiagnosticInterceptor(
            NullLogger<NpgsqlLifecycleDiagnosticInterceptor>.Instance,
            telemetry);
        var command = NpgsqlLifecycleDiagnosticActivityTests.NewNpgsqlCommand("SELECT 1");
        var commandId = Guid.NewGuid();

        interceptor.ScalarExecuting(command, NpgsqlDiagnosticEventData.Start(command, commandId, DbCommandMethod.ExecuteScalar), default);
        Assert.Equal(1, interceptor.TrackedCommandCount);
        interceptor.ScalarExecuted(
            command,
            NpgsqlDiagnosticEventData.Executed(command, commandId, TimeSpan.FromMilliseconds(2), DbCommandMethod.ExecuteScalar),
            1);

        Assert.Equal(0, interceptor.TrackedCommandCount);
        Assert.Equal(0, telemetry.GetSnapshot().ExecutingCommands);
    }

    [Fact]
    public async Task ConcurrentScopes_DoNotLeakCommandState()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNpgsqlDiagnostics(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var tasks = Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            using var scope = provider.CreateScope();
            var interceptor = scope.ServiceProvider.GetRequiredService<NpgsqlLifecycleDiagnosticInterceptor>();
            var command = NpgsqlLifecycleDiagnosticActivityTests.NewNpgsqlCommand($"SELECT {index}");
            var commandId = Guid.NewGuid();
            interceptor.ScalarExecuting(command, NpgsqlDiagnosticEventData.Start(command, commandId, DbCommandMethod.ExecuteScalar), default);
            interceptor.ScalarExecuted(
                command,
                NpgsqlDiagnosticEventData.Executed(command, commandId, TimeSpan.FromMilliseconds(1), DbCommandMethod.ExecuteScalar),
                index);
            return (interceptor.TrackedCommandCount, interceptor.ExecutingCommandCount);
        }));

        var results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.Equal((0, 0), result));
        Assert.Equal(0, provider.GetRequiredService<NpgsqlDiagnosticsTelemetry>().GetSnapshot().ExecutingCommands);
    }

    [Fact]
    public void Telemetry_PublishesExpectedMeterAndInstrumentsWithoutDimensions()
    {
        var instruments = new HashSet<string>(StringComparer.Ordinal);
        var commandMeasurements = new List<double>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name != NpgsqlDiagnosticsTelemetry.MeterName)
                    return;

                instruments.Add(instrument.Name);
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "npgsql.diagnostics.command.execution_ms")
            {
                Assert.Equal(0, tags.Length);
                commandMeasurements.Add(measurement);
            }
        });
        listener.Start();
        using var telemetry = new NpgsqlDiagnosticsTelemetry();

        telemetry.RecordCommandExecution(12.5);

        Assert.Contains("npgsql.diagnostics.command.execution_ms", instruments);
        Assert.Contains("npgsql.diagnostics.connection.acquisition_ms", instruments);
        Assert.Contains("npgsql.diagnostics.connections.idle", instruments);
        Assert.Contains("npgsql.diagnostics.pool.exhausted", instruments);
        Assert.Contains("npgsql.diagnostics.commands.executing", instruments);
        Assert.Equal(12.5, Assert.Single(commandMeasurements));
    }

    [Fact]
    public void ConnectionInterceptor_RecordsAcquisitionAndMaintainsGlobalActiveCount()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var interceptor = new NpgsqlConnectionLifecycleDiagnosticInterceptor(
            NullLogger<NpgsqlConnectionLifecycleDiagnosticInterceptor>.Instance,
            telemetry);
        using var requestScope = RequestPhaseDiagnosticsContext.Begin();
        using var connection = new NpgsqlConnection();
        var connectionId = Guid.NewGuid();
        static string Message(EventDefinitionBase _, EventData __) => "m46";
        var opening = new ConnectionEventData(
            null!, Message, connection, null!, connectionId, false, DateTimeOffset.UtcNow);
        var opened = new ConnectionEndEventData(
            null!, Message, connection, null!, connectionId, false, DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(4));

        interceptor.ConnectionOpening(connection, opening, default);
        interceptor.ConnectionOpened(connection, opened);

        Assert.Equal(1, telemetry.GetSnapshot().ActiveConnections);
        Assert.Equal(1, RequestPhaseDiagnosticsContext.Current!.Snapshot().Npgsql.ConnectionAcquireCount);

        interceptor.ConnectionClosed(connection, opened);
        Assert.Equal(0, telemetry.GetSnapshot().ActiveConnections);
    }

    [Fact]
    public async Task RequestContexts_AreIsolatedAcrossConcurrentOperations()
    {
        static Task<RequestPhaseDiagnosticRecord> Capture(double commandMs) => Task.Run(async () =>
        {
            using var scope = RequestPhaseDiagnosticsContext.Begin();
            RequestPhaseDiagnosticsContext.Current!.RequestId = commandMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
            RequestPhaseDiagnosticsContext.Current.RecordNpgsqlCommand(commandMs);
            await Task.Yield();
            return RequestPhaseDiagnosticsContext.Current.Snapshot();
        });

        var records = await Task.WhenAll(Capture(3), Capture(7));

        Assert.Contains(records, record => record.RequestId == "3" && record.Npgsql.CommandTotalMs == 3);
        Assert.Contains(records, record => record.RequestId == "7" && record.Npgsql.CommandTotalMs == 7);
        Assert.Null(RequestPhaseDiagnosticsContext.Current);
    }

    [Fact]
    public void Program_IntegratesDiagnosticsExactlyOnceWithoutRemovingExistingInterceptors()
    {
        var program = File.ReadAllText(Path.Combine(FindRepoRoot(), "PointOfSale.API", "Program.cs"));

        Assert.Equal(1, Count(program, "builder.Services.AddNpgsqlDiagnostics(builder.Configuration);"));
        Assert.Equal(1, Count(program, "sp.GetRequiredService<NpgsqlLifecycleDiagnosticInterceptor>()"));
        Assert.Equal(1, Count(program, "sp.GetRequiredService<NpgsqlConnectionLifecycleDiagnosticInterceptor>()"));
        Assert.Equal(1, Count(program, ".AddMeter(NpgsqlDiagnosticsTelemetry.MeterName)"));
        Assert.Equal(1, Count(program, ".AddSource(\"PointOfSale.Npgsql.Diagnostics\")"));
        Assert.Equal(1, Count(program, "app.UseMiddleware<RequestLifecycleDiagnosticMiddleware>();"));
        Assert.Contains("sp.GetRequiredService<PointOfSale.Infrastructure.Observability.SlowQueryLoggingInterceptor>()", program, StringComparison.Ordinal);
        Assert.Contains("sp.GetRequiredService<TenantSaveChangesInterceptor>()", program, StringComparison.Ordinal);
        Assert.Contains("PointOfSale.Infrastructure.Elasticsearch.Services.SearchTelemetry.MeterName", program, StringComparison.Ordinal);
    }

    private static void AssertDescriptor<T>(IServiceCollection services, ServiceLifetime lifetime)
    {
        var descriptor = Assert.Single(services, service => service.ServiceType == typeof(T));
        Assert.Equal(lifetime, descriptor.Lifetime);
    }

    private static int Count(string value, string token) =>
        value.Split(token, StringSplitOptions.None).Length - 1;

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}

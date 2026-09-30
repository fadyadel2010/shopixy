using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Infrastructure.Observability;

namespace PointOfSale.Tests.Diagnostics;

public sealed class NpgsqlLifecycleDiagnosticActivitySourceLifetimeTests
{
    private const int WarmUpCount = 50;
    private const int BatchSize = 500;
    private const int BatchCount = 3;

    [Fact]
    public void EveryInterceptorInstance_ResolvesTheSameProcessLifetimeActivitySource()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var logger = NullLogger<NpgsqlLifecycleDiagnosticInterceptor>.Instance;
        var first = new NpgsqlLifecycleDiagnosticInterceptor(logger, telemetry);
        var expected = first.ActivitySource;

        Assert.Equal("PointOfSale.Npgsql.Diagnostics", expected.Name);
        Assert.Equal("1.0.0", expected.Version);
        Assert.Same(NpgsqlLifecycleDiagnosticInterceptor.SharedActivitySource, expected);

        var sources = Enumerable.Range(0, 500)
            .Select(_ => new NpgsqlLifecycleDiagnosticInterceptor(logger, telemetry).ActivitySource);
        Assert.All(sources, source => Assert.Same(expected, source));
    }

    [Fact]
    public void ScopedResolutions_ShareTheSourceButNotTheCommandState()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<NpgsqlDiagnosticsTelemetry>()
            .AddScoped<NpgsqlLifecycleDiagnosticInterceptor>()
            .BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var a = first.ServiceProvider.GetRequiredService<NpgsqlLifecycleDiagnosticInterceptor>();
        var b = second.ServiceProvider.GetRequiredService<NpgsqlLifecycleDiagnosticInterceptor>();

        Assert.NotSame(a, b);
        Assert.Same(a.ActivitySource, b.ActivitySource);
        Assert.Equal(0, a.TrackedCommandCount);
        Assert.Equal(0, b.TrackedCommandCount);
    }

    [Fact]
    public void ConstructingInterceptors_DoesNotShowGrowingPerInstanceAllocation()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var logger = NullLogger<NpgsqlLifecycleDiagnosticInterceptor>.Instance;

        for (var i = 0; i < WarmUpCount; i++)
            _ = new NpgsqlLifecycleDiagnosticInterceptor(logger, telemetry);

        var measurements = Enumerable.Range(0, BatchCount)
            .Select(_ => MeasureAverageBytesPerConstruction(logger, telemetry, BatchSize))
            .ToArray();
        var first = measurements[0];
        var last = measurements[^1];

        Assert.True(
            last < first * 1.5,
            $"Per-construction allocation grew across batches: {string.Join(", ", measurements.Select(m => m.ToString("F1")))}");
        Assert.True(
            last < 4096,
            $"Per-construction allocation was {last:F1} bytes: {string.Join(", ", measurements.Select(m => m.ToString("F1")))}");
    }

    [Fact]
    public void ScopedInterceptorConstructor_MustNotConstructAnActivitySource()
    {
        var source = File.ReadAllText(InterceptorPath());
        var constructor = ExtractConstructor(source, "public NpgsqlLifecycleDiagnosticInterceptor(");

        Assert.DoesNotContain("new ActivitySource", constructor, StringComparison.Ordinal);
        Assert.DoesNotContain("new ActivitySource", source, StringComparison.Ordinal);
        Assert.Matches(@"static\s+readonly\s+ActivitySource\s+\w+\s*=\s*new", source);
    }

    private static double MeasureAverageBytesPerConstruction(
        ILogger<NpgsqlLifecycleDiagnosticInterceptor> logger,
        NpgsqlDiagnosticsTelemetry telemetry,
        int count)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < count; i++)
            _ = new NpgsqlLifecycleDiagnosticInterceptor(logger, telemetry);
        return (GC.GetAllocatedBytesForCurrentThread() - before) / (double)count;
    }

    private static string InterceptorPath() => Path.Combine(
        FindRepoRoot(AppContext.BaseDirectory),
        "PointOfSale.Infrastructure",
        "Observability",
        "NpgsqlLifecycleDiagnosticInterceptor.cs");

    private static string ExtractConstructor(string source, string signature)
    {
        var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Constructor signature '{signature}' was not found.");
        var bodyStart = source.IndexOf('{', signatureIndex);
        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            depth += source[index] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
                return source[signatureIndex..(index + 1)];
        }

        throw new InvalidOperationException($"Constructor body for '{signature}' was not balanced.");
    }

    private static string FindRepoRoot(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}

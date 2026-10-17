using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.HealthChecks;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Infrastructure;

public sealed class Phase6R2ConfiguredReadOnlyTests
{
    [Phase6ReadOnlyFact]
    public async Task Existing_R2_DNS_TLS_and_minimal_list_probe_without_object_output_or_mutation()
    {
        var root = Path.Combine(TestConfiguration.GetSolutionRoot(), "PointOfSale.API");
        var configuration = new ConfigurationBuilder().SetBasePath(root)
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true)
            .AddJsonFile("appsettings.Development.local.json", true)
            .AddUserSecrets<PointOfSale.API.Configuration.R2Options>(optional: true)
            .AddEnvironmentVariables().Build();
        string Value(string flat, string nested) => (configuration[flat] ?? configuration[nested] ?? "").Trim();
        var options = new R2Options {
            AccountId = Value("R2_ACCOUNT_ID", "R2:AccountId"), AccessKeyId = Value("R2_ACCESS_KEY_ID", "R2:AccessKeyId"),
            SecretAccessKey = Value("R2_SECRET_ACCESS_KEY", "R2:SecretAccessKey"), Bucket = Value("R2_BUCKET", "R2:Bucket"),
            ServiceUrl = Value("R2_SERVICE_URL", "R2:ServiceUrl"), PublicUrl = Value("R2_PUBLIC_URL", "R2:PublicUrl") };
        Assert.True(options.IsConfigured);
        var endpoint = new Uri(options.ResolvedServiceUrl);
        Assert.Equal("https", endpoint.Scheme);
        var transport = new List<object>();
        using (var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            var clock = Stopwatch.StartNew();
            try {
                var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, budget.Token);
                transport.Add(new { operation = "dns", succeeded = true, durationMs = clock.Elapsed.TotalMilliseconds,
                    addressCount = addresses.Length });
                clock.Restart();
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(endpoint.Host, endpoint.Port, budget.Token);
                await using var tls = new SslStream(tcp.GetStream());
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = endpoint.Host }, budget.Token);
                transport.Add(new { operation = "tls", succeeded = tls.IsAuthenticated, durationMs = clock.Elapsed.TotalMilliseconds,
                    protocol = tls.SslProtocol.ToString() });
            } catch (Exception ex) {
                transport.Add(new { operation = "dns-connect-tls", succeeded = false, durationMs = clock.Elapsed.TotalMilliseconds,
                    exceptionCategory = ex.GetType().Name });
            }
        }
        var samples = new List<object>();
        var check = new R2DependencyHealthCheck(Options.Create(options), new EnvironmentStub(root), NullLogger<R2DependencyHealthCheck>.Instance);
        for (var i = 0; i < 6; i++) {
            var clock = Stopwatch.StartNew();
            var result = await check.CheckHealthAsync(new HealthCheckContext());
            samples.Add(new { observedUtc = DateTime.UtcNow, durationMs = clock.Elapsed.TotalMilliseconds,
                status = result.Status.ToString(), exceptionCategory = result.Exception?.GetType().Name, result.Data });
            if (i < 5) await Task.Delay(TimeSpan.FromSeconds(10));
        }
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("SHOPIXY_PHASE6_EVIDENCE")!, "r2-configured-probes.json"),
            JsonSerializer.Serialize(new { configured = true, endpointScheme = endpoint.Scheme,
                r2Endpoint = endpoint.Host.EndsWith(".r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase),
                pathStyle = true, authenticationRegion = "auto", timeoutSeconds = 5, retries = 0,
                transport, samples, writes = 0, objectNamesRecorded = 0 }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class EnvironmentStub(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "PointOfSale.API";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

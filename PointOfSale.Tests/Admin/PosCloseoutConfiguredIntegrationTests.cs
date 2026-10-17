using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Infrastructure.WhatsApp.Transport;
using PointOfSale.Tests.Infrastructure;
using Xunit.Abstractions;

namespace PointOfSale.Tests.Admin;

public sealed class PosCloseoutConfiguredIntegrationTests(ITestOutputHelper output)
{
    [ConfiguredAdminIntegrationFact]
    public async Task ExistingApplicationBridge_StatusContract_WithoutActivationOrMessageDispatch()
    {
        var configuration = new ConfigurationBuilder().SetBasePath(Path.Combine(TestConfiguration.GetSolutionRoot(), "PointOfSale.API"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true)
            .AddJsonFile("appsettings.Development.local.json", true).AddEnvironmentVariables().Build();
        Assert.True(configuration.GetValue<bool>("Smtp:Enabled"));
        Assert.True(configuration.GetValue<bool>("Smtp:EnableSsl"));
        Assert.Equal(587, configuration.GetValue<int>("Smtp:Port"));
        Assert.True(MailAddress.TryCreate(configuration["Smtp:FromEmail"], out _));
        Assert.False(string.IsNullOrWhiteSpace(configuration["BaileysService:ApiKey"]));
        var configuredTestRecipientKeys = configuration.AsEnumerable()
            .Where(p => !string.IsNullOrWhiteSpace(p.Value) &&
                (p.Key.Contains("TestRecipient", StringComparison.OrdinalIgnoreCase) ||
                 p.Key.Contains("SafeRecipient", StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Key).ToArray();
        var tenant = Environment.GetEnvironmentVariable("POS_ADMIN_OBSERVE_TENANT")!;
        Assert.True(Guid.TryParse(tenant, out _));
        var observer = new StatusOnlyHandler();
        using var client = new HttpClient(observer);
        var transport = new BaileysWhatsAppTransport(client, configuration, NullLogger<BaileysWhatsAppTransport>.Instance);
        var status = await transport.GetStatusAsync(tenant);
        Assert.Equal(HttpMethod.Get, observer.Method);
        Assert.Equal($"/sessions/{tenant}/status", observer.Path);
        Assert.Contains(observer.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });
        // A disconnected/missing session is a supported negative status contract,
        // never positive messaging readiness. Do not connect or send to turn it green.
        var evidence = new
        {
            observedUtc = DateTime.UtcNow,
            configuredTestRecipientKeys,
            configuredAuthenticationHeader = true,
            bridgeHttp = (int)observer.StatusCode,
            applicationStatusReadSucceeded = status.Success,
            connectionStatus = status.Status.ToString(),
            smtpEnabled = true, smtpTlsEnabled = true, smtpSenderConfigured = true,
            externalMessagesSubmitted = 0, accountsActivated = 0,
        };
        var text = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
        output.WriteLine(text);
        var directory = Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY");
        if (!string.IsNullOrEmpty(directory)) await File.WriteAllTextAsync(Path.Combine(directory, "configured-messaging-contract.json"), text);
    }

    private sealed class StatusOnlyHandler : DelegatingHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public HttpStatusCode StatusCode { get; private set; }
        public StatusOnlyHandler() => InnerHandler = new HttpClientHandler();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // This observer cannot accidentally submit an external message.
            if (request.Method != HttpMethod.Get || !request.RequestUri!.AbsolutePath.EndsWith("/status", StringComparison.Ordinal))
                throw new InvalidOperationException("Only read-only bridge status requests are permitted.");
            Method = request.Method;
            Path = request.RequestUri.AbsolutePath;
            var response = await base.SendAsync(request, cancellationToken);
            StatusCode = response.StatusCode;
            return response;
        }
    }
}

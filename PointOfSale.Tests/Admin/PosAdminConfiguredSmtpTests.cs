using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using PointOfSale.Tests.Infrastructure;
using Xunit.Abstractions;

namespace PointOfSale.Tests.Admin;

public sealed class PosAdminConfiguredSmtpTests(ITestOutputHelper output)
{
    [ConfiguredAdminIntegrationFact]
    public async Task ExistingSmtp_StartTlsAndAuthenticate_WithoutSubmittingAnyMessage()
    {
        var configuration = new ConfigurationBuilder().SetBasePath(Path.Combine(TestConfiguration.GetSolutionRoot(), "PointOfSale.API"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true)
            .AddJsonFile("appsettings.Development.local.json", true).AddEnvironmentVariables().Build();
        var smtp = configuration.GetSection("Smtp");
        Assert.True(smtp.GetValue<bool>("Enabled"));
        Assert.True(smtp.GetValue<bool>("EnableSsl"));
        var host = smtp["Host"]!;
        var port = smtp.GetValue<int>("Port");
        // This check implements STARTTLS, never an insecure AUTH fallback.
        Assert.Equal(587, port);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, deadline.Token);
        var network = client.GetStream();
        var reader = new StreamReader(network, Encoding.ASCII, leaveOpen: true);
        var writer = new StreamWriter(network, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
        async Task<(int Code, string Text)> Read()
        {
            var lines = new List<string>();
            string line;
            do
            {
                line = await reader.ReadLineAsync(deadline.Token) ?? throw new IOException("SMTP closed connection");
                lines.Add(line);
            } while (line.Length > 3 && line[3] == '-');
            return (int.Parse(line[..3]), string.Join('\n', lines));
        }
        async Task<int> Send(string command)
        {
            await writer.WriteLineAsync(command.AsMemory(), deadline.Token);
            return (await Read()).Code;
        }
        Assert.Equal(220, (await Read()).Code);
        Assert.Equal(250, await Send("EHLO pos-admin-quality.local"));
        Assert.Equal(220, await Send("STARTTLS"));
        reader.Dispose(); writer.Dispose();
        using var tls = new SslStream(network, leaveInnerStreamOpen: true);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, deadline.Token);
        Assert.True(tls.IsAuthenticated && tls.IsEncrypted);
        using var tlsReader = new StreamReader(tls, Encoding.ASCII, leaveOpen: true);
        using var tlsWriter = new StreamWriter(tls, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
        reader = tlsReader; writer = tlsWriter;
        Assert.Equal(250, await Send("EHLO pos-admin-quality.local"));
        Assert.Equal(334, await Send("AUTH LOGIN"));
        Assert.Equal(334, await Send(Convert.ToBase64String(Encoding.UTF8.GetBytes(smtp["Username"]!))));
        // Never log credentials, their Base64 representation or raw provider replies.
        var authenticated = await Send(Convert.ToBase64String(Encoding.UTF8.GetBytes(smtp["Password"]!)));
        output.WriteLine($"Configured SMTP: TLS certificate verified, AUTH result={authenticated}. No MAIL FROM, RCPT TO or DATA sent; no delivery claimed.");
        Assert.Equal(235, authenticated);
        Assert.Equal(221, await Send("QUIT"));
    }
}

public sealed class ConfiguredAdminIntegrationFactAttribute : FactAttribute
{
    public ConfiguredAdminIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("POS_ADMIN_CONFIGURED_CHECKS") != "1")
            Skip = "Opt-in configured integration check; no provisioning or message dispatch.";
    }
}

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Services.Auth;

namespace PointOfSale.Tests.Storefront;

public sealed class StorefrontQualitySmtpTests
{
    [Fact]
    public async Task SmtpSender_UsesControlledLoopbackTransport_WithMultipartContent()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = new List<string>();
        var sink = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync(deadline.Token);
            await using var stream = connection.GetStream();
            using var reader = new StreamReader(stream);
            await using var writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\r\n" };
            await writer.WriteLineAsync("220 localhost controlled test sink");
            while (await reader.ReadLineAsync(deadline.Token) is { } line)
            {
                if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("354 End with a dot");
                    while (await reader.ReadLineAsync(deadline.Token) is { } body && body != ".") received.Add(body);
                    await writer.WriteLineAsync("250 Accepted by controlled sink");
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 Bye");
                    break;
                }
                else await writer.WriteLineAsync("250 localhost");
            }
        }, deadline.Token);
        var sender = new SmtpEmailSender(Options.Create(new SmtpSettings
        {
            Enabled = true, Host = "127.0.0.1", Port = port, EnableSsl = false,
            FromEmail = "sender@example.test", FromName = "Controlled quality test"
        }), NullLogger<SmtpEmailSender>.Instance);
        await sender.SendEmailAsync("quality@example.test", "Controlled verification", "<p>Controlled verification</p>", "Controlled verification")
            .WaitAsync(deadline.Token);
        await sink.WaitAsync(deadline.Token);
        var mime = string.Join("\n", received);
        Assert.Contains("quality@example.test", mime);
        Assert.Contains("text/plain", mime);
        Assert.Contains("text/html", mime);
        Assert.Contains("multipart/alternative", mime);
    }
}

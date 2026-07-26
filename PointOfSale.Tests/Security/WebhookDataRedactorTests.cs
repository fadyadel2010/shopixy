using PointOfSale.Core.Security;
using Xunit;

namespace PointOfSale.Tests.Security;

public class WebhookDataRedactorTests
{
    [Fact]
    public void RedactPayload_WithJsonContainingPII_RedactsSensitiveFields()
    {
        // Arrange
        var payload = @"{
            ""event"": ""delivered"",
            ""tracking_number"": ""ABC123"",
            ""recipient"": {
                ""name"": ""John Doe"",
                ""phone"": ""+1-555-123-4567"",
                ""email"": ""john.doe@example.com"",
                ""address"": ""123 Main Street, New York, NY 10001""
            },
            ""status"": ""delivered""
        }";

        // Act
        var redacted = WebhookDataRedactor.RedactPayload(payload);

        // Assert
        Assert.DoesNotContain("John Doe", redacted);
        Assert.DoesNotContain("+1-555-123-4567", redacted);
        Assert.DoesNotContain("john.doe@example.com", redacted);
        Assert.DoesNotContain("123 Main Street", redacted);
        Assert.Contains("[REDACTED]", redacted);
        Assert.Contains("delivered", redacted); // Non-PII fields preserved
        Assert.Contains("ABC123", redacted); // Tracking number preserved
    }

    [Fact]
    public void RedactPayload_WithLongPayload_TruncatesTo500Chars()
    {
        // Arrange
        var longPayload = new string('x', 1000);

        // Act
        var redacted = WebhookDataRedactor.RedactPayload(longPayload);

        // Assert
        Assert.True(redacted.Length <= 520); // 500 + "[truncated]"
        Assert.Contains("...[truncated]", redacted);
    }

    [Fact]
    public void RedactPayload_WithPlainTextContainingEmail_RedactsEmail()
    {
        // Arrange
        var payload = "Customer email: john.doe@example.com requested delivery";

        // Act
        var redacted = WebhookDataRedactor.RedactPayload(payload);

        // Assert
        Assert.DoesNotContain("john.doe@example.com", redacted);
        Assert.Contains("[EMAIL_REDACTED]", redacted);
    }

    [Fact]
    public void RedactIpAddress_WithIPv4_ReturnsNetworkPrefix()
    {
        // Arrange
        var ipAddress = "203.0.113.45";

        // Act
        var redacted = WebhookDataRedactor.RedactIpAddress(ipAddress);

        // Assert
        Assert.Equal("203.0.113.0/24", redacted);
    }

    [Fact]
    public void RedactIpAddress_WithIPv6_ReturnsNetworkPrefix()
    {
        // Arrange
        var ipAddress = "2001:0db8:85a3:0000:0000:8a2e:0370:7334";

        // Act
        var redacted = WebhookDataRedactor.RedactIpAddress(ipAddress);

        // Assert - representation-agnostic IPv6 validation
        Assert.Contains("/64", redacted);
        
        var parts = redacted.Split('/');
        Assert.Equal(2, parts.Length);
        Assert.Equal("64", parts[1]);
        
        // Parse the address portion and verify the network prefix bytes
        var redactedAddress = System.Net.IPAddress.Parse(parts[0]);
        var redactedBytes = redactedAddress.GetAddressBytes();
        
        // Verify we have exactly 16 bytes for IPv6 address
        Assert.Equal(16, redactedBytes.Length);
        
        // Expected first 8 bytes for 2001:0db8:85a3:0000::/64
        var expectedBytes = new byte[] { 0x20, 0x01, 0x0d, 0xb8, 0x85, 0xa3, 0x00, 0x00 };
        
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(expectedBytes[i], redactedBytes[i]);
        }
        
        // Verify the host half (bytes 8-15) is zeroed out
        for (int i = 8; i < 16; i++)
        {
            Assert.Equal(0, redactedBytes[i]);
        }
    }

    [Fact]
    public void RedactIpAddress_WithNull_ReturnsNull()
    {
        // Act
        var redacted = WebhookDataRedactor.RedactIpAddress(null);

        // Assert
        Assert.Null(redacted);
    }

    [Fact]
    public void TruncateUserAgent_WithLongUserAgent_TruncatesTo100Chars()
    {
        // Arrange
        var userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36 with lots of extra information that makes this very long";

        // Act
        var truncated = WebhookDataRedactor.TruncateUserAgent(userAgent);

        // Assert
        Assert.True(truncated!.Length <= 103); // 100 + "..."
        Assert.Contains("...", truncated);
    }

    [Fact]
    public void TruncateUserAgent_RemovesVersionNumbers()
    {
        // Arrange
        var userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/91.0.4472.124";

        // Act
        var truncated = WebhookDataRedactor.TruncateUserAgent(userAgent);

        // Assert
        Assert.DoesNotContain("5.0", truncated);
        Assert.DoesNotContain("10.0", truncated);
        Assert.DoesNotContain("91.0.4472.124", truncated);
        Assert.Contains("x.x", truncated);
    }

    [Fact]
    public void TruncateUserAgent_WithNull_ReturnsNull()
    {
        // Act
        var truncated = WebhookDataRedactor.TruncateUserAgent(null);

        // Assert
        Assert.Null(truncated);
    }

    [Fact]
    public void RedactPayload_WithEmptyString_ReturnsEmpty()
    {
        // Act
        var redacted = WebhookDataRedactor.RedactPayload("");

        // Assert
        Assert.Equal(string.Empty, redacted);
    }

    [Fact]
    public void RedactPayload_PreservesNonPIIFields()
    {
        // Arrange
        var payload = @"{
            ""event_id"": ""evt_123"",
            ""status"": ""in_transit"",
            ""timestamp"": ""2026-04-21T10:00:00Z"",
            ""carrier"": ""DHL""
        }";

        // Act
        var redacted = WebhookDataRedactor.RedactPayload(payload);

        // Assert
        Assert.Contains("evt_123", redacted);
        Assert.Contains("in_transit", redacted);
        Assert.Contains("DHL", redacted);
    }
}

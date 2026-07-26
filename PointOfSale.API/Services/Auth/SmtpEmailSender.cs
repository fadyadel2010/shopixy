using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services.Auth;

/// <summary>
/// SMTP-based email sender for production use.
/// Phase 8.4: Email Verification & Account Trust
/// 
/// Configure in appsettings.json:
/// {
///   "Smtp": {
///     "Host": "smtp.gmail.com",
///     "Port": 587,
///     "Username": "your-email@gmail.com",
///     "Password": "your-app-password",
///     "EnableSsl": true,
///     "FromEmail": "noreply@myplatform.com",
///     "FromName": "MyPlatform"
///   }
/// }
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(
        IOptions<SmtpSettings> settings,
        ILogger<SmtpEmailSender> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, string textBody)
    {
        if (!_settings.Enabled)
        {
            _logger.LogWarning("SMTP is disabled. Email to {To} with subject '{Subject}' was not sent.", to, subject);
            return;
        }

        try
        {
            using var client = new SmtpClient(_settings.Host, _settings.Port)
            {
                Credentials = new NetworkCredential(_settings.Username, _settings.Password),
                EnableSsl = _settings.EnableSsl
            };

            var message = new MailMessage
            {
                From = new MailAddress(_settings.FromEmail, _settings.FromName),
                Subject = subject,
                IsBodyHtml = true,
                Body = htmlBody
            };

            message.To.Add(new MailAddress(to));

            // Add plain text alternative
            if (!string.IsNullOrEmpty(textBody))
            {
                var plainView = AlternateView.CreateAlternateViewFromString(textBody, null, "text/plain");
                var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, null, "text/html");
                message.AlternateViews.Add(plainView);
                message.AlternateViews.Add(htmlView);
            }

            await client.SendMailAsync(message);

            _logger.LogInformation("Email sent successfully to {To} with subject '{Subject}'", to, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To} with subject '{Subject}'", to, subject);
            throw;
        }
    }
}

/// <summary>
/// SMTP configuration settings.
/// </summary>
public class SmtpSettings
{
    public const string SectionName = "Smtp";

    /// <summary>
    /// Whether SMTP sending is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// SMTP server hostname.
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// SMTP server port.
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// SMTP username for authentication.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// SMTP password for authentication.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Whether to use SSL/TLS.
    /// </summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>
    /// From email address.
    /// </summary>
    public string FromEmail { get; set; } = "noreply@example.com";

    /// <summary>
    /// From display name.
    /// </summary>
    public string FromName { get; set; } = "MyPlatform";
}

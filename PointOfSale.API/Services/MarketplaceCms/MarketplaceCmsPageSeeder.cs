using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.MarketplaceCms;

public sealed class MarketplaceCmsPageSeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MarketplaceCmsPageSeeder> _logger;

    public MarketplaceCmsPageSeeder(
        IServiceScopeFactory scopeFactory,
        ILogger<MarketplaceCmsPageSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Fire-and-forget: the CMS publish path projects to Mongo; seeding must not
        // block host startup (Kestrel) if the read-model store is slow/unavailable.
        _ = Task.Run(() => RunSeedAsync(cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunSeedAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cmsService = scope.ServiceProvider.GetRequiredService<IMarketplaceCmsPageService>();

            var hasAnyPages = await dbContext.MarketplaceCmsPages
                .AsNoTracking()
                .IgnoreQueryFilters()
                .AnyAsync(page => page.IsDraft || page.IsPublished, cancellationToken);

            if (hasAnyPages)
            {
                _logger.LogInformation("Marketplace CMS seed skipped because marketplace pages already exist.");
                return;
            }

            foreach (var page in BuildDefaultPages())
            {
                var created = await cmsService.CreatePageAsync(page, cancellationToken);
                await cmsService.PublishPageAsync(created.Id, created.Draft?.Version ?? 1, cancellationToken);
            }

            _logger.LogInformation("Marketplace CMS default pages seeded successfully.");
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            _logger.LogWarning(
                ex,
                "Skipping marketplace CMS page seeding because PostgreSQL is unavailable during startup.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Marketplace CMS page seeding failed during startup. The application will continue without seeded marketplace pages.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool IsDatabaseUnavailable(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is NpgsqlException or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<MarketplaceCmsPageRequest> BuildDefaultPages()
    {
        return
        [
            CreateRichTextPage(
                slug: "about",
                title: "About Us",
                metaDescription: "Learn how Shopixy Marketplace helps merchants scale with reliable commerce infrastructure.",
                html: """
                    <h1>About Shopixy Marketplace</h1>
                    <p>Shopixy Marketplace is a multi-tenant commerce platform built to help ambitious merchants launch, operate, and grow with confidence.</p>
                    <h2>What we do</h2>
                    <p>We provide the infrastructure behind modern storefront operations, including catalog management, commerce workflows, customer journeys, and platform-grade administration.</p>
                    <h2>Our approach</h2>
                    <p>We focus on operational clarity, tenant isolation, and scalable platform tooling so every merchant can run a professional storefront without compromising performance or governance.</p>
                    <h2>Why merchants choose Shopixy</h2>
                    <ul>
                      <li>Clear separation between store operations and marketplace administration</li>
                      <li>Flexible content and storefront systems for growth-focused teams</li>
                      <li>Reliable foundations for SEO, customer trust, and day-to-day execution</li>
                    </ul>
                    <p>We continue to invest in practical product improvements that help merchants move faster while staying in control.</p>
                    """),
            CreateRichTextPage(
                slug: "contact",
                title: "Contact Us",
                metaDescription: "Contact Shopixy Marketplace for platform support, partnerships, and general inquiries.",
                html: """
                    <h1>Contact Shopixy Marketplace</h1>
                    <p>Our team is available to help with platform onboarding, merchant operations, partnerships, and customer-facing questions.</p>
                    <h2>General inquiries</h2>
                    <p>Email us at <a href="mailto:support@shopixy.com">support@shopixy.com</a> and include as much detail as possible so we can route your request quickly.</p>
                    <h2>Business hours</h2>
                    <p>Sunday to Thursday, 9:00 AM to 6:00 PM Cairo time.</p>
                    <h2>Partnerships</h2>
                    <p>For strategic partnerships, marketplace expansion, or technology collaborations, contact <a href="mailto:partnerships@shopixy.com">partnerships@shopixy.com</a>.</p>
                    <h2>Response expectations</h2>
                    <p>We aim to acknowledge all legitimate inquiries promptly and provide clear next steps when follow-up is required.</p>
                    """),
            CreateRichTextPage(
                slug: "privacy",
                title: "Privacy Policy",
                metaDescription: "Understand how Shopixy Marketplace collects, uses, stores, and protects personal information.",
                html: """
                    <h1>Privacy Policy</h1>
                    <p>This Privacy Policy explains how Shopixy Marketplace collects, uses, stores, and protects information across our marketplace experiences.</p>
                    <h2>Information we collect</h2>
                    <p>We collect the information needed to operate the platform, process transactions, improve security, and support merchants and customers.</p>
                    <h2>How information is used</h2>
                    <ul>
                      <li>To fulfill platform services and support merchant operations</li>
                      <li>To secure accounts, sessions, and transactional workflows</li>
                      <li>To improve reliability, product quality, and customer experience</li>
                    </ul>
                    <h2>Data protection</h2>
                    <p>Access to personal information is restricted to authorized personnel and operational workflows. We do not sell personal data to third parties.</p>
                    <h2>Your choices</h2>
                    <p>If you need help with privacy-related requests, contact our team and we will guide you through the appropriate process.</p>
                    """),
            CreateRichTextPage(
                slug: "terms",
                title: "Terms & Conditions",
                metaDescription: "Review the terms governing access to and use of Shopixy Marketplace.",
                html: """
                    <h1>Terms &amp; Conditions</h1>
                    <p>These Terms &amp; Conditions govern access to and use of Shopixy Marketplace and the services connected to it.</p>
                    <h2>Use of the platform</h2>
                    <p>By accessing the marketplace, users agree to comply with applicable policies, lawful usage requirements, and operational safeguards designed to protect merchants and customers.</p>
                    <h2>Orders and availability</h2>
                    <p>Orders, listings, pricing, and availability may be subject to validation, fraud review, merchant policies, and operational approval.</p>
                    <h2>Service changes</h2>
                    <p>We may improve, modify, or retire features when necessary to maintain platform quality, reliability, or compliance.</p>
                    <h2>Liability and support</h2>
                    <p>We work to provide dependable service, but interruptions or operational delays may occur. When they do, we aim to communicate clearly and resolve issues responsibly.</p>
                    """)
        ];
    }

    private static MarketplaceCmsPageRequest CreateRichTextPage(
        string slug,
        string title,
        string metaDescription,
        string html)
    {
        return new MarketplaceCmsPageRequest
        {
            Slug = slug,
            Title = title,
            Content = JsonSerializer.SerializeToElement(html),
            Seo = new MarketplaceCmsPageSeoRequest
            {
                MetaTitle = title,
                MetaDescription = metaDescription
            }
        };
    }
}

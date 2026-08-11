using Hangfire;
using PointOfSale.API.Jobs;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Infrastructure.Shipping.BackgroundJobs;

namespace PointOfSale.Tests.Jobs;

public sealed class HangfireRecurringJobCronTests
{
    [Fact]
    public void PreviouslyFailingRenewalCronExpression_IsRejectedByHangfireCronosParser()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            HangfireInitializationHostedService.ValidateCronExpression(
                CertificateRenewalSchedulerJob.RecurringJobId,
                "CertificateRenewalScheduler:ScanIntervalMinutes",
                60,
                Cron.MinuteInterval(60)));

        Assert.Contains("*/60 * * * *", ex.Message, StringComparison.Ordinal);
        Assert.Contains(CertificateRenewalSchedulerJob.RecurringJobId, ex.Message, StringComparison.Ordinal);
        Assert.Contains("CertificateRenewalScheduler:ScanIntervalMinutes", ex.Message, StringComparison.Ordinal);
        Assert.Contains("60", ex.Message, StringComparison.Ordinal);
        Assert.Equal("Cronos.CronFormatException", ex.InnerException?.GetType().FullName);
    }

    [Fact]
    public void ValidCronExpression_IsAcceptedByHangfireCronosParser()
    {
        var cron = HangfireInitializationHostedService.ValidateCronExpression(
            "test-recurring-job",
            "TestScheduler:Cron",
            "0 * * * *",
            Cron.Hourly());

        Assert.Equal("0 * * * *", cron);
    }

    [Fact]
    public void AllRegisteredRecurringJobCronExpressions_AreValidForHangfireCronosParser()
    {
        var jobs = new (string JobId, string ConfigurationKey, object ConfigurationValue, string CronExpression)[]
        {
            (TrackingSyncJobRegistration.SyncAllJobId, "TrackingSyncJobRegistration.CronExpression", TrackingSyncJobRegistration.CronExpression, TrackingSyncJobRegistration.CronExpression),
            ("manual-wallet-proof-cleanup", "hardcoded Cron.HourInterval(6)", 6, Cron.HourInterval(6)),
            ("accounting-outbox-cleanup", "hardcoded Cron.Hourly", "Hourly", Cron.Hourly()),
            ("inventory-reservation-cleanup", "hardcoded Cron.Minutely", "Minutely", Cron.Minutely()),
            ("refund-evidence-cleanup", "hardcoded Cron.Hourly", "Hourly", Cron.Hourly()),
            ("checkout-attachment-cleanup", "hardcoded Cron.Hourly", "Hourly", Cron.Hourly()),
            ("whatsapp-session-heartbeat", "hardcoded Cron.MinuteInterval(5)", 5, Cron.MinuteInterval(5)),
            ("whatsapp-message-retry", "hardcoded Cron.MinuteInterval(5)", 5, Cron.MinuteInterval(5)),
            ("whatsapp-session-cleanup", "hardcoded Cron.Daily(3)", 3, Cron.Daily(3)),
            ("abandoned-cart-scanner", "hardcoded Cron.MinuteInterval(5)", 5, Cron.MinuteInterval(5)),
            ("abandoned-cart-cleanup", "hardcoded Cron.Daily(3)", 3, Cron.Daily(3)),
            ("abandoned-checkout-scanner", "hardcoded Cron.MinuteInterval(5)", 5, Cron.MinuteInterval(5)),
            ("processed-trust-events-cleanup", "hardcoded Cron.Daily(3)", 3, Cron.Daily(3)),
            ("commercehub-cart-cleanup", "hardcoded Cron.HourInterval(6)", 6, Cron.HourInterval(6)),
            ("order-request-idempotency-cleanup", "hardcoded Cron.Daily(2)", 2, Cron.Daily(2)),
            ("marketplace-trust-reconciliation", "hardcoded Cron.Hourly", "Hourly", Cron.Hourly()),
            ("marketplace-es-drift-reconciliation", "hardcoded Cron.Daily(4)", 4, Cron.Daily(4)),
            ("commercehub-search-reconciliation", "hardcoded Cron.Hourly", "Hourly", Cron.Hourly()),
            ("commercehub-finance-reconciliation", "hardcoded Cron.Hourly", "Hourly", Cron.Hourly()),
            ("commercehub-inventory-reconciliation", "hardcoded Cron.Hourly", "Hourly", Cron.Hourly()),
            ("marketplace-disk-monitor", "hardcoded Cron.Hourly", "Hourly", Cron.Hourly()),
            ("affiliate-ranking-recalculation", "hardcoded Cron.Daily", "Daily", Cron.Daily()),
            ("commercehub-stale-return-escalation", "hardcoded Cron.HourInterval(6)", 6, Cron.HourInterval(6)),
            (DomainVerificationSchedulerJob.RecurringJobId, "DnsVerification:Scheduler:PollIntervalSeconds", 10, HangfireInitializationHostedService.BuildSecondsOrMinutesSchedulerCron(DomainVerificationSchedulerJob.RecurringJobId, "DnsVerification:Scheduler:PollIntervalSeconds", 10)),
            (CertificateProvisioningSchedulerJob.RecurringJobId, "CertificateProvisioningScheduler:ScanIntervalSeconds", 60, HangfireInitializationHostedService.BuildSecondsOrMinutesSchedulerCron(CertificateProvisioningSchedulerJob.RecurringJobId, "CertificateProvisioningScheduler:ScanIntervalSeconds", 60)),
            (CertificateRenewalSchedulerJob.RecurringJobId, "CertificateRenewalScheduler:ScanIntervalMinutes", 60, HangfireInitializationHostedService.BuildCertificateRenewalSchedulerCron(60)),
        };

        foreach (var job in jobs)
        {
            var cron = HangfireInitializationHostedService.ValidateCronExpression(
                job.JobId,
                job.ConfigurationKey,
                job.ConfigurationValue,
                job.CronExpression);

            Assert.Equal(job.CronExpression, cron);
        }
    }
}

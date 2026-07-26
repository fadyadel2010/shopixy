using System.Collections.Concurrent;
using PointOfSale.API.SuperAdmin.Models;

namespace PointOfSale.API.SuperAdmin.Services;

public sealed class SuperAdminLoginAttemptMonitor : ISuperAdminLoginAttemptMonitor
{
    private static readonly TimeSpan TrackingWindow = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, List<LoginAttemptRecord>> _attempts = new(StringComparer.Ordinal);

    public SuperAdminLoginAttemptResult RecordFailure(string ipAddress, string email)
    {
        var key = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress.Trim();
        var normalizedEmail = string.IsNullOrWhiteSpace(email) ? "unknown" : email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;

        var attempts = _attempts.AddOrUpdate(
            key,
            _ => new List<LoginAttemptRecord> { new(now, normalizedEmail) },
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.RemoveAll(record => now - record.TimestampUtc > TrackingWindow);
                    existing.Add(new LoginAttemptRecord(now, normalizedEmail));
                    return existing;
                }
            });

        lock (attempts)
        {
            attempts.RemoveAll(record => now - record.TimestampUtc > TrackingWindow);

            var failureCount = attempts.Count;
            var distinctEmailCount = attempts
                .Select(record => record.Email)
                .Distinct(StringComparer.Ordinal)
                .Count();

            return new SuperAdminLoginAttemptResult
            {
                FailureCount = failureCount,
                DistinctEmailCount = distinctEmailCount,
                IsSuspicious = failureCount >= 3 || distinctEmailCount >= 3
            };
        }
    }

    public void ClearFailures(string ipAddress)
    {
        var key = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress.Trim();
        _attempts.TryRemove(key, out _);
    }

    private sealed record LoginAttemptRecord(DateTime TimestampUtc, string Email);
}

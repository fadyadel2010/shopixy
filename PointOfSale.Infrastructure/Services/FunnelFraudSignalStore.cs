using System.Collections.Concurrent;

namespace PointOfSale.Infrastructure.Services;

/// <summary>
/// Bounded, process-local owner for observability-only funnel fraud counters.
/// Entries expire after inactivity and the owner enforces a hard cardinality cap.
/// </summary>
public interface IFunnelFraudSignalStore
{
    long RecordClick(string clientIp, Guid affiliateAccountId);
}

public sealed class FunnelFraudSignalStore : IFunnelFraudSignalStore
{
    internal const int MaximumEntries = 10_000;
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, SignalEntry> _entries = new(StringComparer.Ordinal);

    public long RecordClick(string clientIp, Guid affiliateAccountId)
    {
        var nowUtc = DateTime.UtcNow;
        var key = $"{clientIp}:{affiliateAccountId:N}";
        var entry = _entries.AddOrUpdate(
            key,
            _ => new SignalEntry(1, nowUtc, nowUtc.Add(EntryLifetime)),
            (_, current) => current.ExpiresAtUtc <= nowUtc
                ? new SignalEntry(1, nowUtc, nowUtc.Add(EntryLifetime))
                : current with
                {
                    ClickCount = current.ClickCount + 1,
                    LastSeenAtUtc = nowUtc,
                    ExpiresAtUtc = nowUtc.Add(EntryLifetime)
                });

        if (_entries.Count > MaximumEntries)
        {
            Evict(nowUtc);
        }

        return entry.ClickCount;
    }

    private void Evict(DateTime nowUtc)
    {
        foreach (var expired in _entries.Where(pair => pair.Value.ExpiresAtUtc <= nowUtc))
        {
            _entries.TryRemove(expired.Key, out _);
        }

        var overflow = _entries.Count - MaximumEntries;
        if (overflow <= 0)
        {
            return;
        }

        foreach (var oldest in _entries
                     .OrderBy(pair => pair.Value.LastSeenAtUtc)
                     .Take(overflow))
        {
            _entries.TryRemove(oldest.Key, out _);
        }
    }

    private sealed record SignalEntry(
        long ClickCount,
        DateTime LastSeenAtUtc,
        DateTime ExpiresAtUtc);
}

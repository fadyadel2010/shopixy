namespace PointOfSale.Infrastructure.Utilities;

/// <summary>
/// Normalizes inventory projection timestamps to the precision persisted by MongoDB.
/// PostgreSQL can retain sub-millisecond ticks that MongoDB truncates, so stock replay
/// ordering must use a precision shared by SQL events and downstream read models.
/// </summary>
public static class StockProjectionTimestamp
{
    public static DateTime NormalizeUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        var ticks = utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMillisecond);
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    public static DateTime Next(DateTime? currentStockUpdatedAtUtc, DateTime? candidateUtc = null)
    {
        var nowUtc = NormalizeUtc(candidateUtc ?? DateTime.UtcNow);
        if (!currentStockUpdatedAtUtc.HasValue)
        {
            return nowUtc;
        }

        var currentUtc = NormalizeUtc(currentStockUpdatedAtUtc.Value);
        return currentUtc >= nowUtc
            ? currentUtc.AddMilliseconds(1)
            : nowUtc;
    }

    public static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue ? NormalizeUtc(value.Value) : null;
}

namespace PointOfSale.API.Extensions;

public static class DateTimeExtensions
{
    public static DateTime EnsureUtc(this DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    public static DateTime NormalizeInclusiveEndUtc(this DateTime value)
    {
        var utcValue = value.EnsureUtc();
        return utcValue.TimeOfDay == TimeSpan.Zero
            ? utcValue.AddDays(1).AddTicks(-1)
            : utcValue;
    }
}

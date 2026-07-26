namespace PointOfSale.Core.Pagination;

public static class PaginationGuard
{
    public const int MaxPageSize = 100;

    public static int NormalizePage(int page) => Math.Max(page, 1);

    public static int NormalizePageSize(int pageSize, int maxPageSize = MaxPageSize)
    {
        var effectiveMaxPageSize = Math.Clamp(maxPageSize, 1, MaxPageSize);
        return Math.Clamp(pageSize, 1, effectiveMaxPageSize);
    }

    public static int NormalizeTake(int take, int maxTake = MaxPageSize)
    {
        var effectiveMaxTake = Math.Clamp(maxTake, 1, MaxPageSize);
        return Math.Clamp(take, 1, effectiveMaxTake);
    }

    public static int GetSkip(int page, int pageSize)
    {
        var normalizedPage = NormalizePage(page);
        var normalizedPageSize = NormalizePageSize(pageSize);
        var skip = ((long)normalizedPage - 1L) * normalizedPageSize;

        return skip >= int.MaxValue
            ? int.MaxValue
            : (int)skip;
    }
}

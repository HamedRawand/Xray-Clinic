namespace NasimXray.Api.Services;

public static class Paging
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    // Keeps the requested page inside 1..last page, so a stale page number (records were deleted or the
    // filter changed) returns the last real page instead of an empty one.
    public static (int Page, int PageSize) Normalize(int page, int pageSize, int totalCount)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var lastPage = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        return (Math.Clamp(page, 1, lastPage), pageSize);
    }
}

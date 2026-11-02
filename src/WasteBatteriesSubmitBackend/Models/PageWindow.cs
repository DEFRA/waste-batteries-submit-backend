namespace WasteBatteriesSubmitBackend.Models;

// The slice a paged read asks the store for. A page past the end moves back to the last page.
internal static class PageWindow
{
    public static ResolvedPage Resolve(int? pageNumber, int? pageSize, int totalCount)
    {
        if (pageSize is null)
        {
            return new ResolvedPage(PageNumber: 1, PageSize: totalCount, Skip: 0, IsPaged: false);
        }

        var size = pageSize.Value;
        if (totalCount == 0)
        {
            return new ResolvedPage(PageNumber: 1, PageSize: size, Skip: 0, IsPaged: false);
        }

        var number = pageNumber ?? 1;
        var totalPages = (int)Math.Ceiling(totalCount / (double)size);
        if (number > totalPages)
        {
            number = totalPages;
        }

        return new ResolvedPage(number, size, (number - 1) * size, IsPaged: true);
    }
}

internal readonly record struct ResolvedPage(int PageNumber, int PageSize, int Skip, bool IsPaged);

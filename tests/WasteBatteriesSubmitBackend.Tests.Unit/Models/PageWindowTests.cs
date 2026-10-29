using WasteBatteriesSubmitBackend.Models;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Models;

public class PageWindowTests
{
    [Fact]
    public void Resolve_WhenPageSizeIsOmitted_ReturnsEveryMatch()
    {
        var window = PageWindow.Resolve(pageNumber: null, pageSize: null, totalCount: 4);

        Assert.Equal(new ResolvedPage(1, 4, 0, true), window);
        var page = new PaginatedList<string>(["a", "b", "c", "d"], 4, window.PageNumber, window.PageSize);
        Assert.Equal(1, page.TotalPages);
        Assert.False(page.HasPreviousPage);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public void Resolve_WhenNothingMatchesAndPageSizeIsOmitted_UsesAnEmptyPage()
    {
        var window = PageWindow.Resolve(pageNumber: 3, pageSize: null, totalCount: 0);

        Assert.Equal(new ResolvedPage(1, 0, 0, true), window);
        var page = new PaginatedList<string>([], 0, window.PageNumber, window.PageSize);
        Assert.Equal(0, page.TotalPages);
        Assert.False(page.HasPreviousPage);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public void Resolve_WhenPageNumberIsOmitted_StartsAtTheFirstPage()
    {
        var window = PageWindow.Resolve(pageNumber: null, pageSize: 2, totalCount: 5);

        Assert.Equal(new ResolvedPage(1, 2, 0, false), window);
        var page = new PaginatedList<string>(["a", "b"], 5, window.PageNumber, window.PageSize);
        Assert.Equal(3, page.TotalPages);
        Assert.False(page.HasPreviousPage);
        Assert.True(page.HasNextPage);
    }

    [Fact]
    public void Resolve_WhenThePageIsInRange_SkipsEarlierPages()
    {
        var window = PageWindow.Resolve(pageNumber: 2, pageSize: 2, totalCount: 5);

        Assert.Equal(new ResolvedPage(2, 2, 2, false), window);
        var page = new PaginatedList<string>(["c", "d"], 5, window.PageNumber, window.PageSize);
        Assert.True(page.HasPreviousPage);
        Assert.True(page.HasNextPage);
    }

    [Fact]
    public void Resolve_WhenThePageIsPastTheEnd_ReturnsTheLastPage()
    {
        var window = PageWindow.Resolve(pageNumber: 9, pageSize: 2, totalCount: 5);

        Assert.Equal(new ResolvedPage(3, 2, 4, false), window);
        var page = new PaginatedList<string>(["e"], 5, window.PageNumber, window.PageSize);
        Assert.True(page.HasPreviousPage);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public void Resolve_WhenNothingMatchesARequestedPage_ReturnsTheFirstEmptyPage()
    {
        var window = PageWindow.Resolve(pageNumber: 4, pageSize: 2, totalCount: 0);

        Assert.Equal(new ResolvedPage(1, 2, 0, false), window);
        var page = new PaginatedList<string>([], 0, window.PageNumber, window.PageSize);
        Assert.Equal(0, page.TotalPages);
        Assert.Equal(2, page.PageSize);
    }
}

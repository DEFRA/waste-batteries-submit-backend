using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Http.Json;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

public partial class PomSubmissionEndpointsTests
{
    [Fact]
    public async Task List_WhenCredentialsAreMissing_ReturnsUnauthorized()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync("/pom-submissions", cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/pom-submissions")]
    [InlineData("/pom-submissions?isCurrent=false")]
    public async Task List_WhenNoBusinessFilterIsSet_ReturnsValidationError(string url)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();

        // Act
        var response = await client.GetAsync(url, cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(
            "At least one of schemeOrgId, memberOrgId, compliancePeriodYears or compliancePeriodQuarters is required.",
            problem.Errors["Filters"]);
    }

    [Fact]
    public async Task List_WhenFiltersAreInvalid_ReturnsEveryValidationError()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var url = SubmissionsUrl(
            ("schemeOrgId", "not-an-id"),
            ("memberOrgId", "not-an-id"),
            ("compliancePeriodYears", "12"),
            ("compliancePeriodQuarters", " "),
            ("pageNumber", "0"),
            ("pageSize", "0"));

        // Act
        var response = await client.GetAsync(url, cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("schemeOrgId must be an ObjectId.", problem.Errors["SchemeOrgId"]);
        Assert.Contains("memberOrgId must be an ObjectId.", problem.Errors["MemberOrgId"]);
        Assert.Contains("compliancePeriodYears must be a calendar year.", problem.Errors["CompliancePeriodYears[0]"]);
        Assert.Contains(problem.Errors.Keys, key => key.StartsWith("CompliancePeriodQuarters", StringComparison.Ordinal));
        Assert.Contains("pageNumber must be at least 1.", problem.Errors["PageNumber"]);
        Assert.Contains("pageSize must be at least 1.", problem.Errors["PageSize"]);
    }

    [Fact]
    public async Task List_WhenPageNumberIsSetWithoutPageSize_ReturnsValidationError()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();

        // Act
        var response = await client.GetAsync(
            "/pom-submissions?schemeOrgId=674a1b2c3d4e5f6789012345&pageNumber=2",
            cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("pageSize is required when pageNumber is set.", problem.Errors["PageNumber"]);
    }

    [Fact]
    public async Task List_WhenFiltersAreCombined_ReturnsTheMatchingSubmissions()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        const string schemeOrgId = "674a1b2c3d4e5f6789012701";
        const string member1 = "674a1b2c3d4e5f6789012711";
        const string member2 = "674a1b2c3d4e5f6789012712";
        var firstQuarter = await CreateSubmission(client, schemeOrgId, member1, 2024, "Quarter 1", cancellationToken);
        var secondQuarter = await CreateSubmission(client, schemeOrgId, member1, 2025, "Quarter 2", cancellationToken);
        var thirdQuarter = await CreateSubmission(client, schemeOrgId, member1, 2026, "Quarter 2", cancellationToken);
        var fourthQuarter = await CreateSubmission(client, schemeOrgId, member1, 2026, "Quarter 1", cancellationToken);
        var otherMember = await CreateSubmission(client, schemeOrgId, member2, 2026, "Quarter 2", cancellationToken);
        var previous = await CreateSubmission(client, schemeOrgId, member2, 2025, "Quarter 2", cancellationToken);
        await SetCurrent(previous.Id, false, cancellationToken);

        // Act
        using var current = await ReadPage(client, cancellationToken, ("schemeOrgId", schemeOrgId));
        using var memberOnly = await ReadPage(client, cancellationToken, ("memberOrgId", member1));
        using var memberYearAndQuarter = await ReadPage(
            client,
            cancellationToken,
            ("schemeOrgId", schemeOrgId),
            ("memberOrgId", member1),
            ("compliancePeriodYears", "2024"),
            ("compliancePeriodYears", "2025"),
            ("compliancePeriodYears", "2026"),
            ("compliancePeriodQuarters", "Quarter 2"));
        using var eitherQuarter = await ReadPage(
            client,
            cancellationToken,
            ("schemeOrgId", schemeOrgId),
            ("compliancePeriodQuarters", "Quarter 1"),
            ("compliancePeriodQuarters", "Quarter 3"));
        using var history = await ReadPage(
            client,
            cancellationToken,
            ("schemeOrgId", schemeOrgId),
            ("isCurrent", "false"));
        using var currentOtherMember = await ReadPage(
            client,
            cancellationToken,
            ("schemeOrgId", schemeOrgId),
            ("memberOrgId", member2));

        // Assert
        AssertPage(
            current.RootElement,
            pageNumber: 1,
            pageSize: 5,
            totalPages: 1,
            totalCount: 5,
            hasPrevious: false,
            hasNext: false,
            otherMember.Id,
            fourthQuarter.Id,
            thirdQuarter.Id,
            secondQuarter.Id,
            firstQuarter.Id);
        Assert.Equal(firstQuarter.Json, Item(current.RootElement, firstQuarter.Id).GetRawText());
        AssertPage(memberOnly.RootElement, 1, 4, 1, 4, false, false, fourthQuarter.Id, thirdQuarter.Id, secondQuarter.Id, firstQuarter.Id);
        AssertPage(memberYearAndQuarter.RootElement, 1, 2, 1, 2, false, false, thirdQuarter.Id, secondQuarter.Id);
        AssertPage(eitherQuarter.RootElement, 1, 2, 1, 2, false, false, fourthQuarter.Id, firstQuarter.Id);
        AssertPage(history.RootElement, 1, 1, 1, 1, false, false, previous.Id);
        Assert.False(Item(history.RootElement, previous.Id).GetProperty("isCurrent").GetBoolean());
        AssertPage(currentOtherMember.RootElement, 1, 1, 1, 1, false, false, otherMember.Id);
    }

    [Fact]
    public async Task List_WhenAPageIsRequested_ReturnsThatPage()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        const string schemeOrgId = "674a1b2c3d4e5f6789012601";
        var first = await CreateSubmission(client, schemeOrgId, "674a1b2c3d4e5f6789012611", 2026, "Quarter 2", cancellationToken);
        var second = await CreateSubmission(client, schemeOrgId, "674a1b2c3d4e5f6789012612", 2026, "Quarter 2", cancellationToken);
        var third = await CreateSubmission(client, schemeOrgId, "674a1b2c3d4e5f6789012613", 2026, "Quarter 2", cancellationToken);

        // Act
        using var firstPage = await ReadPage(
            client,
            cancellationToken,
            ("schemeOrgId", schemeOrgId),
            ("pageSize", "2"));
        using var secondPage = await ReadPage(
            client,
            cancellationToken,
            ("schemeOrgId", schemeOrgId),
            ("pageNumber", "2"),
            ("pageSize", "2"));
        using var pastTheEnd = await ReadPage(
            client,
            cancellationToken,
            ("schemeOrgId", schemeOrgId),
            ("pageNumber", "9"),
            ("pageSize", "2"));
        using var all = await ReadPage(client, cancellationToken, ("schemeOrgId", schemeOrgId));
        using var none = await ReadPage(
            client,
            cancellationToken,
            ("schemeOrgId", "674a1b2c3d4e5f6789012801"),
            ("pageNumber", "4"),
            ("pageSize", "2"));

        // Assert
        AssertPage(firstPage.RootElement, 1, 2, 2, 3, false, true, third.Id, second.Id);
        AssertPage(secondPage.RootElement, 2, 2, 2, 3, true, false, first.Id);
        AssertPage(pastTheEnd.RootElement, 2, 2, 2, 3, true, false, first.Id);
        AssertPage(all.RootElement, 1, 3, 1, 3, false, false, third.Id, second.Id, first.Id);
        AssertPage(none.RootElement, 1, 2, 0, 0, false, false);
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.Tests.Integration.Utils;

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

    [Fact]
    public async Task List_WhenTokenIsRejected_ReturnsUnauthorized()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-test-token");

        // Act
        var response = await client.GetAsync(
            "/pom-submissions?schemeOrgId=674a1b2c3d4e5f6789012345",
            cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_WhenServiceDevTokenIsPresented_ReturnsThePage()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateAnonymousClient();
        var token = fixture.Services.GetRequiredService<IOptions<ServiceAuthConfig>>().Value.DevToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync(
            "/pom-submissions?schemeOrgId=674a1b2c3d4e5f6789012aaa",
            cancellationToken);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("/pom-submissions")]
    [InlineData("/pom-submissions?returnCurrentOnly=false")]
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
    public async Task List_WhenPageNumberIsSetWithoutPageSize_LimitsThePage()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();

        // Act
        using var page = await ReadPage(
            client,
            cancellationToken,
            ("schemeOrgId", "674a1b2c3d4e5f6789012999"),
            ("pageNumber", "2"));

        // Assert
        // No rows, so the page number is kept at 1 and the omitted page size is the configured maximum.
        Snapshot.Match(PinApiJson(page.RootElement.GetRawText()));
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
        const string memberOne = "Member One";
        const string memberTwo = "Member Two";
        var member1Year2024Quarter1 = await CreateSubmission(
            client, schemeOrgId, member1, 2024, "Quarter 1", cancellationToken, memberOne);
        var member1Year2025Quarter2 = await CreateSubmission(
            client, schemeOrgId, member1, 2025, "Quarter 2", cancellationToken, memberOne);
        var member1Year2026Quarter2 = await CreateSubmission(
            client, schemeOrgId, member1, 2026, "Quarter 2", cancellationToken, memberOne);
        var member1Year2026Quarter2Version2 = await Amend(
            client, schemeOrgId, member1, 2026, "Quarter 2", memberOne, "2.000", cancellationToken);
        var member1Year2026Quarter2Version3 = await Amend(
            client, schemeOrgId, member1, 2026, "Quarter 2", memberOne, "3.000", cancellationToken);
        var member1Year2026Quarter1 = await CreateSubmission(
            client, schemeOrgId, member1, 2026, "Quarter 1", cancellationToken, memberOne);
        var member2Year2026Quarter2 = await CreateSubmission(
            client, schemeOrgId, member2, 2026, "Quarter 2", cancellationToken, memberTwo);
        var member2Year2025Quarter2 = await CreateSubmission(
            client, schemeOrgId, member2, 2025, "Quarter 2", cancellationToken, memberTwo);
        await SetCurrent(member2Year2025Quarter2.Id, false, cancellationToken);
        var submissions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [member1Year2024Quarter1.Id] = PinHex(1),
            [member1Year2025Quarter2.Id] = PinHex(2),
            [member1Year2026Quarter2.Id] = PinHex(3),
            [member1Year2026Quarter2Version2] = PinHex(4),
            [member1Year2026Quarter2Version3] = PinHex(5),
            [member1Year2026Quarter1.Id] = PinHex(6),
            [member2Year2026Quarter2.Id] = PinHex(7),
            [member2Year2025Quarter2.Id] = PinHex(8)
        };

        // Act
        using var scheme = await ReadPage(client, cancellationToken, ("schemeOrgId", schemeOrgId));
        using var member = await ReadPage(client, cancellationToken, ("memberOrgId", member1));
        using var memberYearsAndQuarter = await ReadPage(
            client,
            cancellationToken,
            ("schemeOrgId", schemeOrgId),
            ("memberOrgId", member1),
            ("compliancePeriodYears", "2024"),
            ("compliancePeriodYears", "2025"),
            ("compliancePeriodYears", "2026"),
            ("compliancePeriodQuarters", "Quarter 2"));
        using var quarter1Or3 = await ReadPage(
            client,
            cancellationToken,
            ("schemeOrgId", schemeOrgId),
            ("compliancePeriodQuarters", "Quarter 1"),
            ("compliancePeriodQuarters", "Quarter 3"));
        using var currentAndHistorical = await ReadPage(
            client,
            cancellationToken,
            ("schemeOrgId", schemeOrgId),
            ("returnCurrentOnly", "false"));
        using var otherMember = await ReadPage(
            client,
            cancellationToken,
            ("schemeOrgId", schemeOrgId),
            ("memberOrgId", member2));

        // Assert
        // Each file is that query's page. Items are the stored submissions, already in list order.
        Snapshot.MatchAll(
        [
            ("Scheme", PinApiJson(scheme.RootElement.GetRawText(), submissions)),
            ("Member", PinApiJson(member.RootElement.GetRawText(), submissions)),
            ("MemberYearsAndQuarter2", PinApiJson(memberYearsAndQuarter.RootElement.GetRawText(), submissions)),
            ("Quarter1Or3", PinApiJson(quarter1Or3.RootElement.GetRawText(), submissions)),
            ("CurrentAndHistorical", PinApiJson(currentAndHistorical.RootElement.GetRawText(), submissions)),
            ("OtherMember", PinApiJson(otherMember.RootElement.GetRawText(), submissions))
        ]);
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
        using var oversized = await ReadPage(
            client,
            cancellationToken,
            ("schemeOrgId", schemeOrgId),
            ("pageSize", (fixture.Defaults.MaxPageSize + 50).ToString()));
        using var none = await ReadPage(
            client,
            cancellationToken,
            ("schemeOrgId", "674a1b2c3d4e5f6789012801"),
            ("pageNumber", "4"),
            ("pageSize", "2"));

        // Assert
        // A page past the end is the last page. A missing or oversized page size is the configured maximum.
        var submissions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [first.Id] = PinHex(1),
            [second.Id] = PinHex(2),
            [third.Id] = PinHex(3)
        };
        var secondJson = PinApiJson(secondPage.RootElement.GetRawText(), submissions);
        var allJson = PinApiJson(all.RootElement.GetRawText(), submissions);
        Assert.Equal(secondJson, PinApiJson(pastTheEnd.RootElement.GetRawText(), submissions));
        Assert.Equal(allJson, PinApiJson(oversized.RootElement.GetRawText(), submissions));
        Snapshot.MatchAll(
        [
            ("First", PinApiJson(firstPage.RootElement.GetRawText(), submissions)),
            ("Second", secondJson),
            ("All", allJson),
            ("None", PinApiJson(none.RootElement.GetRawText(), submissions))
        ]);
    }

    private static async Task<string> Amend(
        HttpClient client,
        string schemeOrgId,
        string memberOrgId,
        int year,
        string quarter,
        string memberName,
        string tonnes,
        CancellationToken cancellationToken)
    {
        using var response = await client.PatchAsync(
            "/pom-submissions",
            Json(SubmissionBody(schemeOrgId, memberOrgId, year, quarter, memberName, tonnes)),
            cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ReadId(json);
    }
}

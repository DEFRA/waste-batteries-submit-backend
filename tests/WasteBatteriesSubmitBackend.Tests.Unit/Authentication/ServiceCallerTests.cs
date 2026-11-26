using System.Security.Claims;
using System.Text.Json;
using WasteBatteriesSubmitBackend.Authentication;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Authentication;

public class ServiceCallerTests
{
    private const string Allowed = "waste-batteries-obligations-be";

    [Theory]
    [InlineData("waste-batteries-obligations-be")]
    [InlineData("arn:aws:iam::506190012364:role/waste-batteries-obligations-be")]
    public void IsAllowed_WhenSubjectIsTheObligationsService_ReturnsTrue(string subject)
    {
        var identity = Identity(new Claim("sub", subject));

        Assert.True(ServiceCaller.IsAllowed(identity, Allowed));
    }

    [Fact]
    public void IsAllowed_WhenPrincipalTagNamesTheObligationsService_ReturnsTrue()
    {
        var identity = Identity(
            new Claim("sub", "arn:aws:iam::506190012364:role/someone-else"),
            new Claim(ServiceCaller.StsClaim, StsJson(Allowed, "arn:aws:iam::506190012364:role/someone-else")));

        Assert.True(ServiceCaller.IsAllowed(identity, Allowed));
    }

    [Fact]
    public void IsAllowed_WhenPrincipalIdIsTheObligationsRole_ReturnsTrue()
    {
        var identity = Identity(new Claim(
            ServiceCaller.StsClaim,
            StsJson("someone-else", "arn:aws:iam::506190012364:role/waste-batteries-obligations-be")));

        Assert.True(ServiceCaller.IsAllowed(identity, Allowed));
    }

    [Theory]
    [InlineData("waste-batteries-submit-backend")]
    [InlineData("arn:aws:iam::506190012364:role/waste-batteries-submit-backend")]
    [InlineData("arn:aws:iam::506190012364:role/not-waste-batteries-obligations-be")]
    public void IsAllowed_WhenSubjectIsAnotherService_ReturnsFalse(string subject)
    {
        var identity = Identity(new Claim("sub", subject));

        Assert.False(ServiceCaller.IsAllowed(identity, Allowed));
    }

    [Fact]
    public void IsAllowed_WhenTheStsClaimIsNotJson_ReturnsFalse()
    {
        var identity = Identity(new Claim(ServiceCaller.StsClaim, "not-json"));

        Assert.False(ServiceCaller.IsAllowed(identity, Allowed));
    }

    [Fact]
    public void IsAllowed_WhenTheAllowedServiceIsBlank_ReturnsFalse()
    {
        var identity = Identity(new Claim("sub", Allowed));

        Assert.False(ServiceCaller.IsAllowed(identity, " "));
    }

    private static ClaimsIdentity Identity(params Claim[] claims) =>
        new(claims, ServiceAuthDefaults.Scheme, "sub", ClaimTypes.Role);

    private static string StsJson(string service, string principalId) =>
        JsonSerializer.Serialize(new
        {
            principal_tags = new { Service = service },
            principal_id = principalId
        });
}

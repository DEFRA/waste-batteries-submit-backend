using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.Utils.Actor;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Utils.Actor;

public class ActorContextTests
{
    [Fact]
    public void ActorId_WhenTheUserIdClaimIsPresent_ReturnsThatClaim()
    {
        var context = Context(new ClaimsIdentity(
            [new Claim("sub", "user-123"), new Claim(ClaimTypes.Name, "Ada Lovelace")],
            "Test",
            ClaimTypes.Name,
            ClaimTypes.Role));

        Assert.Equal("user-123", context.ActorId);
    }

    [Fact]
    public void ActorId_WhenTheUserIdClaimIsMissing_ReturnsTheName()
    {
        var context = Context(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "opaque-name")],
            "Test"));

        Assert.Equal("opaque-name", context.ActorId);
    }

    [Fact]
    public void ActorId_WhenTheCallerHasNoId_Throws()
    {
        var context = Context(new ClaimsIdentity());

        var exception = Assert.Throws<InvalidOperationException>(() => context.ActorId);

        Assert.Equal("An outbox event needs the caller's opaque id.", exception.Message);
    }

    [Fact]
    public void ActorId_WhenThereIsNoHttpContext_Throws()
    {
        var accessor = new HttpContextAccessor();
        var context = new ActorContext(accessor, Options.Create(Jwt()));

        Assert.Throws<InvalidOperationException>(() => context.ActorId);
    }

    private static ActorContext Context(ClaimsIdentity identity)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return new ActorContext(accessor, Options.Create(Jwt()));
    }

    private static JwtConfig Jwt() => new()
    {
        MetadataAddress = "https://idp.test/.well-known/openid-configuration",
        Audience = "test-audience",
        UserIdClaim = "sub"
    };
}

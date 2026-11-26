using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using WasteBatteriesSubmitBackend.Authentication;
using WasteBatteriesSubmitBackend.Config;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Authentication;

public class AllowedServiceHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenTheCallerIsNotTheService_Succeeds()
    {
        var context = Context(new ClaimsIdentity(
            [new Claim("sub", "user-123")],
            "Bearer",
            "sub",
            ClaimTypes.Role));

        await Handler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_WhenTheServiceIsAllowed_Succeeds()
    {
        var context = Context(new ClaimsIdentity(
            [new Claim("sub", "arn:aws:iam::1:role/waste-batteries-obligations-be")],
            ServiceAuthDefaults.Scheme,
            "sub",
            ClaimTypes.Role));

        await Handler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_WhenTheServiceIsNotAllowed_DoesNotSucceed()
    {
        var context = Context(new ClaimsIdentity(
            [new Claim("sub", "arn:aws:iam::1:role/waste-batteries-submit-backend")],
            ServiceAuthDefaults.Scheme,
            "sub",
            ClaimTypes.Role));

        await Handler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    private static AllowedServiceHandler Handler()
    {
        var services = new ServiceCollection();
        services.AddOptions<ServiceAuthConfig>().Configure(config =>
        {
            config.AllowedService = "waste-batteries-obligations-be";
        });
        using var provider = services.BuildServiceProvider();
        return new AllowedServiceHandler(provider.GetRequiredService<IOptionsMonitor<ServiceAuthConfig>>());
    }

    private static AuthorizationHandlerContext Context(ClaimsIdentity identity)
    {
        var requirement = new AllowedServiceRequirement();
        return new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(identity), null);
    }
}

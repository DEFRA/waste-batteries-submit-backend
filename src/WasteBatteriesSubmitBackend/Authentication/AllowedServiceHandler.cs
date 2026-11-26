using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using WasteBatteriesSubmitBackend.Config;

namespace WasteBatteriesSubmitBackend.Authentication;

public sealed class AllowedServiceRequirement : IAuthorizationRequirement;

// A Defra ID user is already authenticated by the other scheme. A service token
// is only accepted when it names the allowed service.
public sealed class AllowedServiceHandler(IOptionsMonitor<ServiceAuthConfig> serviceAuth)
    : AuthorizationHandler<AllowedServiceRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AllowedServiceRequirement requirement)
    {
        var authenticated = context.User.Identities.Where(identity => identity.IsAuthenticated).ToList();
        if (authenticated.Count == 0)
        {
            return Task.CompletedTask;
        }

        var serviceIdentity = authenticated.FirstOrDefault(identity =>
            identity.AuthenticationType == ServiceAuthDefaults.Scheme);
        if (serviceIdentity is null)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (ServiceCaller.IsAllowed(serviceIdentity, serviceAuth.CurrentValue.AllowedService))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

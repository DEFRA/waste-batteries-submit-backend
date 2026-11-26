using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using WasteBatteriesSubmitBackend.Config;

namespace WasteBatteriesSubmitBackend.Authentication;

// Local stand-in for STS. The obligations API sends the same DevToken.
public sealed class DevelopmentServiceAuthHandler(
    IOptionsMonitor<ServiceAuthConfig> serviceAuth,
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var config = serviceAuth.CurrentValue;
        if (!DevelopmentServiceToken.Matches(Request.Headers.Authorization.ToString(), config.DevToken))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim("sub", config.AllowedService)],
            ServiceAuthDefaults.Scheme,
            "sub",
            ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ServiceAuthDefaults.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

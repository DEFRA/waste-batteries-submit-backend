using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WasteBatteriesSubmitBackend.Test.Utils;

public static class TestAuthentication
{
    public const string Scheme = "Test";
    public const string Token = "test-token";
    public const string UserId = "user-123";

    public static void ConfigureTestAuthentication(this IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:MetadataAddress"] = "https://idp.test/.well-known/openid-configuration",
                ["Jwt:Audience"] = "test-audience"
            });
        });

        builder.ConfigureServices(services =>
        {
            services
                .AddAuthentication(Scheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(Scheme, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = Scheme;
                options.DefaultChallengeScheme = Scheme;
            });
        });
    }

    public static HttpClient CreateAuthenticatedClient<TProgram>(this WebApplicationFactory<TProgram> factory)
        where TProgram : class
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }
}

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (
            !Request.Headers.TryGetValue("Authorization", out var authorization)
            || !authorization.Contains($"Bearer {TestAuthentication.Token}"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim("sub", TestAuthentication.UserId)],
            TestAuthentication.Scheme,
            "sub",
            ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, TestAuthentication.Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

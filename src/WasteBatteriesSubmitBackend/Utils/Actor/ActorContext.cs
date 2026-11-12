using Microsoft.Extensions.Options;
using System.Security.Claims;
using WasteBatteriesSubmitBackend.Config;

namespace WasteBatteriesSubmitBackend.Utils.Actor;

// The caller's opaque id for an outbox event. Personal details stay out of the event.
public interface IActorContext
{
    string ActorId { get; }
}

public sealed class ActorContext(IHttpContextAccessor httpContextAccessor, IOptions<JwtConfig> jwt) : IActorContext
{
    // Read on use. A request that does not write an event never needs a caller id.
    public string ActorId => Read(httpContextAccessor.HttpContext?.User, jwt.Value.UserIdClaim);

    private static string Read(ClaimsPrincipal? user, string claimType)
    {
        var actorId = string.IsNullOrWhiteSpace(claimType) ? null : user?.FindFirst(claimType)?.Value;
        if (string.IsNullOrWhiteSpace(actorId))
        {
            actorId = user?.Identity?.Name;
        }

        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new InvalidOperationException("An outbox event needs the caller's opaque id.");
        }

        return actorId;
    }
}

using System.Security.Cryptography;
using System.Text;

namespace WasteBatteriesSubmitBackend.Authentication;

internal static class DevelopmentServiceToken
{
    internal static bool Matches(string? authorizationHeader, string expectedToken)
    {
        if (string.IsNullOrEmpty(expectedToken) || string.IsNullOrEmpty(authorizationHeader))
        {
            return false;
        }

        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var presented = Encoding.UTF8.GetBytes(authorizationHeader[prefix.Length..]);
        var expected = Encoding.UTF8.GetBytes(expectedToken);
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}

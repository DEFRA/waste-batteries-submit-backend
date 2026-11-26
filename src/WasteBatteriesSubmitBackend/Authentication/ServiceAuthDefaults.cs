namespace WasteBatteriesSubmitBackend.Authentication;

public static class ServiceAuthDefaults
{
    public const string Scheme = "ServiceBearer";

    // POM routes accept a Defra ID user token or this service token.
    public const string Policy = "DefraIdOrService";
}

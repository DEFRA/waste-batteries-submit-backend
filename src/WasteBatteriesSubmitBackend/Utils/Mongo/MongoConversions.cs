using MongoDB.Bson.Serialization.Conventions;

namespace WasteBatteriesSubmitBackend.Utils.Mongo;

public static class MongoConventions
{
    private static readonly object Gate = new();
    private static volatile bool registered;

    public static void Register()
    {
        if (registered)
        {
            return;
        }

        lock (Gate)
        {
            if (registered)
            {
                return;
            }

            var conversions = new ConventionPack
            {
                new CamelCaseElementNameConvention()
            };

            ConventionRegistry.Register("CamelCase", conversions, _ => true);
            registered = true;
        }
    }
}
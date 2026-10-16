using MongoDB.Bson.Serialization.Conventions;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Utils.Mongo;

public class MongoConventionsTests
{
    [Fact]
    public void Register_applies_camel_case_names_and_ignores_later_calls()
    {
        MongoConventions.Register();
        MongoConventions.Register();

        var conventions = ConventionRegistry.Lookup(typeof(SampleDocument));

        Assert.Contains(conventions.Conventions, convention => convention is CamelCaseElementNameConvention);
    }

    private sealed class SampleDocument;
}

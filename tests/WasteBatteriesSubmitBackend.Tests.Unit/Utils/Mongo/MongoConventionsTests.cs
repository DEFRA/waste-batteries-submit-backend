using MongoDB.Bson.Serialization.Conventions;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Utils.Mongo;

public class MongoConventionsTests
{
    [Fact]
    public void Register_WhenCalledMoreThanOnce_AppliesCamelCaseElementNames()
    {
        // Arrange
        // Register is idempotent, so a second call must not replace the convention pack.

        // Act
        MongoConventions.Register();
        MongoConventions.Register();
        var conventions = ConventionRegistry.Lookup(typeof(SampleDocument));

        // Assert
        Assert.Contains(conventions.Conventions, convention => convention is CamelCaseElementNameConvention);
    }

    private sealed class SampleDocument;
}

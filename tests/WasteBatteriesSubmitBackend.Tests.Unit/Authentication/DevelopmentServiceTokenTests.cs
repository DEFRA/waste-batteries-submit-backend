using WasteBatteriesSubmitBackend.Authentication;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Authentication;

public class DevelopmentServiceTokenTests
{
    [Theory]
    [InlineData("Bearer local-obligations-to-submit")]
    [InlineData("bearer local-obligations-to-submit")]
    public void Matches_WhenTheHeaderIsTheDevToken_ReturnsTrue(string header)
    {
        Assert.True(DevelopmentServiceToken.Matches(header, "local-obligations-to-submit"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("local-obligations-to-submit")]
    [InlineData("Bearer other-token")]
    [InlineData("Bearer local-obligations-to-submit ")]
    public void Matches_WhenTheHeaderIsNotTheDevToken_ReturnsFalse(string? header)
    {
        Assert.False(DevelopmentServiceToken.Matches(header, "local-obligations-to-submit"));
    }

    [Fact]
    public void Matches_WhenTheExpectedTokenIsEmpty_ReturnsFalse()
    {
        Assert.False(DevelopmentServiceToken.Matches("Bearer local-obligations-to-submit", ""));
    }
}

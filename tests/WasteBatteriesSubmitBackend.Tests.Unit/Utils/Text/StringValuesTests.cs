using WasteBatteriesSubmitBackend.Utils.Text;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Utils.Text;

public class StringValuesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \t ")]
    public void TrimToEmpty_WhenBlank_ReturnsEmpty(string? value)
    {
        Assert.Equal(string.Empty, StringValues.TrimToEmpty(value));
    }

    [Fact]
    public void TrimToEmpty_WhenPadded_ReturnsTheTrimmedText()
    {
        Assert.Equal("Portable", StringValues.TrimToEmpty("  Portable  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \t ")]
    public void TrimToNull_WhenBlank_ReturnsNull(string? value)
    {
        Assert.Null(StringValues.TrimToNull(value));
    }

    [Fact]
    public void TrimToNull_WhenPadded_ReturnsTheTrimmedText()
    {
        Assert.Equal("General use", StringValues.TrimToNull("  General use  "));
    }
}

using System.Text.Json;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.Utils.Json;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Utils.Json;

public class IntJsonConverterTests
{
    private sealed class Sample
    {
        [IntJsonConverter("widgetCount")]
        public int WidgetCount { get; init; }
    }

    [Fact]
    public void Read_WhenValueIsNotANumber_IncludesThePropertyName()
    {
        // Arrange
        const string json = """{ "widgetCount": true }""";

        // Act
        var act = () => JsonSerializer.Deserialize<Sample>(json, JsonSerializerOptions.Web);

        // Assert
        var exception = Assert.Throws<JsonException>(act);
        Assert.Contains("widgetCount must be a JSON number.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_WhenCompliancePeriodYearIsNotANumber_IncludesThatPropertyName()
    {
        // Arrange
        const string json = """{ "compliancePeriodYear": "twelve" }""";

        // Act
        var act = () => JsonSerializer.Deserialize<PomSubmissionRequest>(json, JsonSerializerOptions.Web);

        // Assert
        var exception = Assert.Throws<JsonException>(act);
        Assert.Contains("compliancePeriodYear must be a JSON number.", exception.Message, StringComparison.Ordinal);
    }
}

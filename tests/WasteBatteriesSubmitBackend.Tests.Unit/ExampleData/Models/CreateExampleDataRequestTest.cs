using System.Text.Json;
using WasteBatteriesSubmitBackend.ExampleData.Models;
using WasteBatteriesSubmitBackend.ExampleData.Validators;

namespace WasteBatteriesSubmitBackend.Tests.Unit.ExampleData.Models;

public class CreateExampleDataRequestTest
{
    [Fact]
    public void Test_validation_on_valid_request()
    {
        var req = new CreateExampleDataRequest { ExampleText = "Hello backend" };
        var result = new CreateExampleDataRequestValidator().Validate(req);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Test_validation_fails_on_invalid_request()
    {
        var req = new CreateExampleDataRequest { ExampleText = "" };
        var result = new CreateExampleDataRequestValidator().Validate(req);
        Assert.False(result.IsValid);
        Assert.Contains(nameof(CreateExampleDataRequest.ExampleText), result.Errors.Select(error => error.PropertyName));
    }

    [Fact]
    public void Test_validation_fails_on_example_text_over_255_characters()
    {
        var req = new CreateExampleDataRequest { ExampleText = new string('a', 256) };
        var result = new CreateExampleDataRequestValidator().Validate(req);
        Assert.False(result.IsValid);
        Assert.Contains(nameof(CreateExampleDataRequest.ExampleText), result.Errors.Select(error => error.PropertyName));
    }

    [Fact]
    public void Test_json_serialization()
    {
        var req = new CreateExampleDataRequest { ExampleText = "Hello backend" };
        var json = JsonSerializer.Serialize(req);
        Assert.Equal("{\"ExampleText\":\"Hello backend\"}", json);
    }

    [Fact]
    public void Test_json_deserialization()
    {
        var json = "{\"ExampleText\":\"Hello backend\"}";
        var req = JsonSerializer.Deserialize<CreateExampleDataRequest>(json);
        Assert.Equivalent(new CreateExampleDataRequest { ExampleText = "Hello backend" }, req);
    }
}

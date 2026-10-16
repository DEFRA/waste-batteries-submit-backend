using System.Text.Json;
using WasteBatteriesSubmitBackend.Example.Models;
using WasteBatteriesSubmitBackend.Example.Validators;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Example.Models;

public class CreateExampleRequestTest
{
    [Fact]
    public void Test_validation_on_valid_request()
    {
        var req = new CreateExampleRequest { Name = "Foo", Counter = 1, Value = "test" };
        var result = new CreateExampleRequestValidator().Validate(req);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Test_validation_fails_on_invalid_request()
    {
        var req = new CreateExampleRequest { Name = "", Counter = -1, Value = "" };
        var result = new CreateExampleRequestValidator().Validate(req);
        Assert.False(result.IsValid);
        Assert.Contains(nameof(CreateExampleRequest.Name), result.Errors.Select(error => error.PropertyName));
        Assert.Contains(nameof(CreateExampleRequest.Value), result.Errors.Select(error => error.PropertyName));
        Assert.Contains(nameof(CreateExampleRequest.Counter), result.Errors.Select(error => error.PropertyName));
    }

    [Fact]
    public void Test_json_serialization()
    {
        var req = new CreateExampleRequest { Name = "Foo", Counter = 1, Value = "test" };
        var json = JsonSerializer.Serialize(req);
        Assert.Equal("{\"Name\":\"Foo\",\"Value\":\"test\",\"Counter\":1}", json);
    }

    [Fact]
    public void Test_json_deserialization()
    {
        var json = "{\"Name\":\"Foo\",\"Value\":\"test\",\"Counter\":1}";
        var req = JsonSerializer.Deserialize<CreateExampleRequest>(json);
        Assert.Equivalent(new CreateExampleRequest { Name = "Foo", Counter = 1, Value = "test" }, req);
    }
}

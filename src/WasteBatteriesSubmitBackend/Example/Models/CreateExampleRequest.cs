namespace WasteBatteriesSubmitBackend.Example.Models;

public sealed class CreateExampleRequest
{
    public string Name { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public int Counter { get; init; }
}

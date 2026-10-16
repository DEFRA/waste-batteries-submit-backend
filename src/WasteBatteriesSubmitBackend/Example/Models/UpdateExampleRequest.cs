namespace WasteBatteriesSubmitBackend.Example.Models;

public sealed class UpdateExampleRequest
{
    public string Value { get; init; } = string.Empty;

    public int Counter { get; init; }
}

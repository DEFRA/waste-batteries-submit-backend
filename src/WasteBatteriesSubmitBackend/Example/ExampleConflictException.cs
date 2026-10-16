namespace WasteBatteriesSubmitBackend.Example;

public sealed class ExampleConflictException(string name, Exception innerException)
    : Exception($"An example record named '{name}' already exists.", innerException);
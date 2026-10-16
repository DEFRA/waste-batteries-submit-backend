using FluentValidation;
using WasteBatteriesSubmitBackend.Example.Models;
using WasteBatteriesSubmitBackend.Example.Repositories;

namespace WasteBatteriesSubmitBackend.Example.Services;

public interface IExampleService
{
    Task<ExampleModel> CreateAsync(CreateExampleRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ExampleModel>> GetAllAsync(string? searchTerm, CancellationToken cancellationToken = default);

    Task<ExampleModel?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<ExampleModel?> UpdateAsync(string name, UpdateExampleRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);
}

// There is no service logic here yet. These methods validate the request and call the repository.
// Service logic will be added when this feature needs behaviour of its own.
public class ExampleService(
    IExampleRepository exampleRepository,
    IValidator<CreateExampleRequest> createExampleRequestValidator,
    IValidator<UpdateExampleRequest> updateExampleRequestValidator) : IExampleService
{
    public async Task<ExampleModel> CreateAsync(CreateExampleRequest request, CancellationToken cancellationToken = default)
    {
        await createExampleRequestValidator.ValidateAndThrowAsync(request, cancellationToken);

        var example = new ExampleModel
        {
            Name = request.Name,
            Value = request.Value,
            Counter = request.Counter
        };

        await exampleRepository.CreateAsync(example, cancellationToken);

        return example;
    }

    public async Task<IReadOnlyCollection<ExampleModel>> GetAllAsync(string? searchTerm, CancellationToken cancellationToken = default)
    {
        if (searchTerm is not null && !string.IsNullOrWhiteSpace(searchTerm))
        {
            return await exampleRepository.SearchAsync(searchTerm, cancellationToken);
        }

        return await exampleRepository.GetAllAsync(cancellationToken);
    }

    public async Task<ExampleModel?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await exampleRepository.GetByNameAsync(name, cancellationToken);
    }

    public async Task<ExampleModel?> UpdateAsync(
        string name,
        UpdateExampleRequest request,
        CancellationToken cancellationToken = default)
    {
        await updateExampleRequestValidator.ValidateAndThrowAsync(request, cancellationToken);

        var example = new ExampleModel
        {
            Name = name,
            Value = request.Value,
            Counter = request.Counter
        };

        return await exampleRepository.UpdateAsync(example, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        return await exampleRepository.DeleteAsync(name, cancellationToken);
    }
}

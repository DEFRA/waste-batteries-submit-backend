using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using WasteBatteriesSubmitBackend.Example.Models;
using WasteBatteriesSubmitBackend.Example.Repositories;
using WasteBatteriesSubmitBackend.Example.Services;
using WasteBatteriesSubmitBackend.Example.Validators;

namespace WasteBatteriesSubmitBackend.Example;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddExampleServices(this IServiceCollection services)
    {
        services.AddSingleton<IExampleRepository, ExampleRepository>();
        services.AddScoped<IExampleService, ExampleService>();
        services.AddScoped<IValidator<CreateExampleRequest>, CreateExampleRequestValidator>();
        services.AddScoped<IValidator<UpdateExampleRequest>, UpdateExampleRequestValidator>();
        return services;
    }
}

using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using WasteBatteriesSubmitBackend.ExampleData.Models;
using WasteBatteriesSubmitBackend.ExampleData.Repositories;
using WasteBatteriesSubmitBackend.ExampleData.Services;
using WasteBatteriesSubmitBackend.ExampleData.Validators;

namespace WasteBatteriesSubmitBackend.ExampleData;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddExampleDataServices(this IServiceCollection services)
    {
        services.AddSingleton<IExampleDataRepository, ExampleDataRepository>();
        services.AddScoped<IExampleDataService, ExampleDataService>();
        services.AddScoped<IValidator<CreateExampleDataRequest>, CreateExampleDataRequestValidator>();
        services.AddScoped<IValidator<ExampleDataUser>, ExampleDataUserValidator>();
        return services;
    }
}

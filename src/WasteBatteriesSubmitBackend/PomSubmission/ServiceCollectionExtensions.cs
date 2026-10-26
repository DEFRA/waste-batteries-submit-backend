using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Services;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;

namespace WasteBatteriesSubmitBackend.PomSubmission;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPomSubmissionServices(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<PomSubmissionConfig>()
            .Bind(configuration.GetRequiredSection("PomSubmission"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPomSubmissionMapper, PomSubmissionMapper>();
        services.AddSingleton<IPomSubmissionRepository, PomSubmissionRepository>();
        services.AddScoped<IValidator<PomSubmissionRequest>, PomSubmissionRequestValidator>();
        services.AddScoped<IValidator<PomSubmissionListRequest>, PomSubmissionListRequestValidator>();
        services.AddScoped<IPomSubmissionService, PomSubmissionService>();
        return services;
    }
}

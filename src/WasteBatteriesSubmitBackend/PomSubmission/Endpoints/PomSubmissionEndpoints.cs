using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using WasteBatteriesSubmitBackend.Endpoints;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Services;

namespace WasteBatteriesSubmitBackend.PomSubmission.Endpoints;

// POM (placed on market) submissions filed by a battery compliance scheme.
[ExcludeFromCodeCoverage]
public static class PomSubmissionEndpoints
{
    public const string RoutePrefix = "/pom-submissions";

    public static RouteGroupBuilder MapPomSubmissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags("POM submissions");

        group.MapPost(string.Empty, Create)
            .WithName("CreatePomSubmission")
            .WithSummary("Create a placed-on-market submission");

        return group;
    }

    private static async Task<Results<Created<PomSubmissionModel>, ValidationProblem>> Create(
        PomSubmissionRequest request,
        [FromServices] IPomSubmissionService pomSubmissionService,
        CancellationToken cancellationToken)
    {
        try
        {
            var submission = await pomSubmissionService.CreateAsync(request, cancellationToken);
            return TypedResults.Created($"{RoutePrefix}/{submission.Id}", submission);
        }
        catch (ValidationException exception)
        {
            return ValidationExceptionResults.ToProblem(exception);
        }
    }
}

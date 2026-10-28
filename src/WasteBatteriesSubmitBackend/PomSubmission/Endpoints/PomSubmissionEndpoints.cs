using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
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
            .WithSummary("Create a placed-on-market submission")
            .ProducesValidationProblem();

        return group;
    }

    private static async Task<Created<PomSubmissionModel>> Create(
        PomSubmissionRequest request,
        [FromServices] IPomSubmissionService pomSubmissionService,
        CancellationToken cancellationToken)
    {
        var submission = await pomSubmissionService.CreateAsync(request, cancellationToken);
        return TypedResults.Created($"{RoutePrefix}/{submission.Id}", submission);
    }
}

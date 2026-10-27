using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
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

        // The document URL from create. A filtered, optionally paged read is the collection route below.
        group.MapGet("/{id}", Get)
            .WithName("GetPomSubmission")
            .WithSummary("Get a placed-on-market submission")
            .ProducesValidationProblem();

        group.MapGet(string.Empty, GetList)
            .WithName("GetPomSubmissions")
            .WithSummary("List placed-on-market submissions")
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

    private static async Task<Results<Ok<PomSubmissionModel>, NotFound>> Get(
        [FromRoute] string id,
        [FromServices] IPomSubmissionService pomSubmissionService,
        CancellationToken cancellationToken)
    {
        var submission = await pomSubmissionService.GetAsync(id, cancellationToken);
        return submission is not null ? TypedResults.Ok(submission) : TypedResults.NotFound();
    }

    private static async Task<Ok<PaginatedList<PomSubmissionModel>>> GetList(
        [AsParameters] PomSubmissionListRequest request,
        [FromServices] IPomSubmissionService pomSubmissionService,
        CancellationToken cancellationToken)
    {
        var page = await pomSubmissionService.GetAsync(request, cancellationToken);
        return TypedResults.Ok(page);
    }
}

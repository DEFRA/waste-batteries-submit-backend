using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Responses;
using WasteBatteriesSubmitBackend.PomSubmission.Services;
using WasteBatteriesSubmitBackend.Utils.Http;

namespace WasteBatteriesSubmitBackend.PomSubmission.Endpoints;

// POM (placed on market) submissions filed by a battery compliance scheme.
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

        group.MapPatch(string.Empty, Amend)
            .WithName("AmendPomSubmission")
            .WithSummary("Amend the current placed-on-market submission")
            .ProducesValidationProblem();

        group.MapPost("/upsert-batch", UpsertBatch)
            .WithName("UpsertBatchPomSubmissions")
            .WithSummary("Create or amend a batch of placed-on-market submissions")
            .ProducesValidationProblem();

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

    // The body is a JSON object with the minimum needed to save a submission.
    // The response is the saved submission with all the additional values - e.g. ids, defaulted values etc
    private static async Task<Created<PomSubmissionModel>> Create(
        PomSubmissionRequest request,
        [FromServices] IPomSubmissionService pomSubmissionService,
        CancellationToken cancellationToken)
    {
        var submission = await pomSubmissionService.CreateAsync(request, cancellationToken);
        return TypedResults.Created($"{RoutePrefix}/{submission.Id}", submission);
    }

    // The body is a JSON object with the minimum needed to save a submission, matches the Create endpoint.
    // The response is the saved submission with all the additional values - e.g. ids, defaulted values etc
    private static async Task<Ok<PomSubmissionModel>> Amend(
        PomSubmissionRequest request,
        [FromServices] IPomSubmissionService pomSubmissionService,
        CancellationToken cancellationToken)
    {
        var submission = await pomSubmissionService.AmendAsync(request, cancellationToken);
        return TypedResults.Ok(submission);
    }

    // The body is a JSON array. Each element has the same shape as a single create or amend.
    // The response is one short result per item, in the same order: the new id, the key,
    // the request index, and whether that item was created or amended.
    private static async Task<Ok<IReadOnlyList<PomSubmissionUpsertResult>>> UpsertBatch(
        [FromBody] List<PomSubmissionRequest> requests,
        [FromServices] IPomSubmissionService pomSubmissionService,
        CancellationToken cancellationToken)
    {
        var results = await pomSubmissionService.UpsertBatchAsync(requests, cancellationToken);
        return TypedResults.Ok(results);
    }

    // Get a single submission by id.
    private static async Task<Results<Ok<PomSubmissionModel>, NotFound>> Get(
        [FromRoute] string id,
        [FromServices] IPomSubmissionService pomSubmissionService,
        CancellationToken cancellationToken)
    {
        var submission = await pomSubmissionService.GetAsync(id, cancellationToken);
        return submission is not null ? TypedResults.Ok(submission) : TypedResults.NotFound();
    }

    // Get a list of submissions that match the filters (combined using "AND") that are passed in.
    private static async Task<Ok<PaginatedList<PomSubmissionModel>>> GetList(
        [AsParameters] PomSubmissionFilterRequest request,
        HttpRequest httpRequest,
        [FromServices] IPomSubmissionService pomSubmissionService,
        CancellationToken cancellationToken)
    {
        var page = await pomSubmissionService.GetAsync(
            request,
            GzipResponseCompression.ClientAcceptsGzip(httpRequest),
            cancellationToken);
        return TypedResults.Ok(page);
    }
}

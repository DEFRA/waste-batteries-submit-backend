using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using WasteBatteriesSubmitBackend.ExampleData.Models;
using WasteBatteriesSubmitBackend.ExampleData.Services;

namespace WasteBatteriesSubmitBackend.ExampleData.Endpoints;

[ExcludeFromCodeCoverage]
public static class ExampleDataEndpoints
{
    public static RouteGroupBuilder MapExampleDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/example")
            .WithTags("ExampleData");

        group.MapGet(string.Empty, GetAll).ProducesValidationProblem();
        group.MapPost(string.Empty, Create).ProducesValidationProblem();
        group.MapGet("/{exampleId}", GetById).WithName("GetExampleDataById");

        return group;
    }

    private static async Task<Ok<IReadOnlyCollection<ExampleDataModel>>> GetAll(
        ClaimsPrincipal user,
        [FromServices] IExampleDataService exampleDataService,
        CancellationToken cancellationToken)
    {
        var examples = await exampleDataService.GetAllAsync(GetUserId(user), cancellationToken);
        return TypedResults.Ok(examples);
    }

    private static async Task<CreatedAtRoute<ExampleDataModel>> Create(
        CreateExampleDataRequest request,
        ClaimsPrincipal user,
        [FromServices] IExampleDataService exampleDataService,
        CancellationToken cancellationToken)
    {
        var saved = await exampleDataService.CreateAsync(request, GetUserId(user), cancellationToken);
        return TypedResults.CreatedAtRoute(saved, "GetExampleDataById", new { exampleId = saved.Id });
    }

    private static async Task<Results<Ok<ExampleDataModel>, NotFound>> GetById(
        [FromRoute] string exampleId,
        ClaimsPrincipal user,
        [FromServices] IExampleDataService exampleDataService,
        CancellationToken cancellationToken)
    {
        var example = await exampleDataService.GetByIdAsync(exampleId, GetUserId(user), cancellationToken);
        return example is not null ? TypedResults.Ok(example) : TypedResults.NotFound();
    }

    private static string? GetUserId(ClaimsPrincipal user)
    {
        return user.Identity?.Name ?? user.FindFirstValue("sub");
    }
}

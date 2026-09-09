using WasteBatteriesSubmitBackend.ExampleData.Models;
using WasteBatteriesSubmitBackend.ExampleData.Services;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace WasteBatteriesSubmitBackend.ExampleData.Endpoints;

[ExcludeFromCodeCoverage]
public static class ExampleDataEndpoints
{
    public static RouteGroupBuilder MapExampleDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/example")
            .WithTags("ExampleData");

        group.MapGet(string.Empty, GetAll);
        group.MapPost(string.Empty, Create);
        group.MapGet("/{exampleId}", GetById).WithName("GetExampleDataById");

        return group;
    }

    private static async Task<Results<Ok<IReadOnlyCollection<ExampleDataModel>>, ValidationProblem>> GetAll(
        ClaimsPrincipal user,
        [FromServices] IExampleDataPersistence exampleDataPersistence,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["userId"] = ["Authenticated user id must not be empty"]
            });
        }

        var examples = await exampleDataPersistence.GetAllAsync(userId, cancellationToken);
        return TypedResults.Ok(examples);
    }

    private static async Task<Results<CreatedAtRoute<ExampleDataModel>, ValidationProblem>> Create(
        CreateExampleDataRequest request,
        ClaimsPrincipal user,
        [FromServices] IExampleDataPersistence exampleDataPersistence,
        CancellationToken cancellationToken)
    {
        var exampleText = request.ExampleText.Trim();
        var userId = GetUserId(user);

        var errors = new Dictionary<string, string[]>();
        if (exampleText.Length == 0)
        {
            errors[nameof(request.ExampleText)] = ["ExampleText must not be empty"];
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            errors["userId"] = ["Authenticated user id must not be empty"];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var saved = await exampleDataPersistence.SaveAsync(exampleText, userId!, cancellationToken);

        return TypedResults.CreatedAtRoute(saved, "GetExampleDataById", new { exampleId = saved.Id });
    }

    private static async Task<Results<Ok<ExampleDataModel>, NotFound>> GetById(
        [FromRoute] string exampleId,
        ClaimsPrincipal user,
        [FromServices] IExampleDataPersistence exampleDataPersistence,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var example = await exampleDataPersistence.GetByIdAsync(exampleId, cancellationToken);
        return example is not null && example.UserId == userId ? TypedResults.Ok(example) : TypedResults.NotFound();
    }

    private static string? GetUserId(ClaimsPrincipal user)
    {
        return user.Identity?.Name ?? user.FindFirstValue("sub");
    }
}

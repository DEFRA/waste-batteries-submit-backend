using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.CodeAnalysis;
using WasteBatteriesSubmitBackend.Endpoints;
using WasteBatteriesSubmitBackend.Example;
using WasteBatteriesSubmitBackend.Example.Models;
using WasteBatteriesSubmitBackend.Example.Services;

namespace WasteBatteriesSubmitBackend.Example.Endpoints;

[ExcludeFromCodeCoverage]
public static class ExampleEndpoints
{
    public static RouteGroupBuilder MapExampleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/db-demo")
            .WithTags("Example");

        group.MapPost(string.Empty, Create);
        group.MapGet(string.Empty, GetAll);
        group.MapGet("/{name}", GetByName).WithName("GetByName");
        group.MapPut("/{name}", Update);
        group.MapDelete("/{name}", Delete);

        return group;
    }

    private static async Task<Results<CreatedAtRoute<ExampleModel>, Conflict<ProblemDetails>, ValidationProblem>> Create(
        CreateExampleRequest request,
        [FromServices] IExampleService exampleService,
        CancellationToken cancellationToken)
    {
        try
        {
            var example = await exampleService.CreateAsync(request, cancellationToken);
            return TypedResults.CreatedAtRoute(example, "GetByName", new { name = example.Name });
        }
        catch (ValidationException exception)
        {
            return ValidationExceptionResults.ToProblem(exception);
        }
        catch (ExampleConflictException exception)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Example already exists",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
    }

    private static async Task<Ok<IReadOnlyCollection<ExampleModel>>> GetAll(
        [FromQuery] string? searchTerm,
        [FromServices] IExampleService exampleService,
        CancellationToken cancellationToken)
    {
        var matches = await exampleService.GetAllAsync(searchTerm, cancellationToken);
        return TypedResults.Ok(matches);
    }

    private static async Task<Results<Ok<ExampleModel>, NotFound>> GetByName(
        [FromRoute] string name,
        [FromServices] IExampleService exampleService,
        CancellationToken cancellationToken)
    {
        var example = await exampleService.GetByNameAsync(name, cancellationToken);
        return example is not null ? TypedResults.Ok(example) : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<ExampleModel>, NotFound, ValidationProblem>> Update(
        [FromRoute] string name,
        UpdateExampleRequest request,
        [FromServices] IExampleService exampleService,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await exampleService.UpdateAsync(name, request, cancellationToken);
            return updated is not null ? TypedResults.Ok(updated) : TypedResults.NotFound();
        }
        catch (ValidationException exception)
        {
            return ValidationExceptionResults.ToProblem(exception);
        }
    }

    private static async Task<Results<NoContent, NotFound>> Delete(
        [FromRoute] string name,
        [FromServices] IExampleService exampleService,
        CancellationToken cancellationToken)
    {
        var deleted = await exampleService.DeleteAsync(name, cancellationToken);
        return deleted ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}

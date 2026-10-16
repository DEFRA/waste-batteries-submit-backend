using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using WasteBatteriesSubmitBackend.OpenApi;

namespace WasteBatteriesSubmitBackend.Tests.Unit.OpenApi;

public class BearerSecuritySchemeTransformerTests
{
    [Fact]
    public async Task Document_transformer_adds_bearer_scheme_when_components_are_missing()
    {
        var document = new OpenApiDocument();
        var transformer = new BearerSecuritySchemeTransformer();

        await transformer.TransformAsync(document, CreateDocumentContext(), CancellationToken.None);

        Assert.Equal(BearerSecuritySchemeTransformer.DocumentTitle, document.Info.Title);
        Assert.Equal("v1", document.Info.Version);
        Assert.NotNull(document.Components);
        Assert.NotNull(document.Components.SecuritySchemes);
        Assert.True(document.Components.SecuritySchemes.ContainsKey(BearerSecuritySchemeTransformer.SchemeId));
        Assert.NotNull(document.Security);
        Assert.NotEmpty(document.Security);
    }

    [Fact]
    public async Task Document_transformer_keeps_existing_components_and_security()
    {
        var document = new OpenApiDocument
        {
            Components = new OpenApiComponents
            {
                SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>()
            },
            Security = []
        };
        var transformer = new BearerSecuritySchemeTransformer();

        await transformer.TransformAsync(document, CreateDocumentContext(), CancellationToken.None);

        Assert.NotNull(document.Components.SecuritySchemes);
        Assert.True(document.Components.SecuritySchemes.ContainsKey(BearerSecuritySchemeTransformer.SchemeId));
        Assert.NotNull(document.Security);
        Assert.Single(document.Security);
    }

    [Fact]
    public async Task Operation_transformer_clears_security_for_anonymous_operations()
    {
        var operation = new OpenApiOperation
        {
            Security = [new OpenApiSecurityRequirement()]
        };
        var transformer = new BearerSecuritySchemeTransformer();

        await transformer.TransformAsync(
            operation,
            CreateOperationContext(new AllowAnonymousAttribute()),
            CancellationToken.None);

        Assert.NotNull(operation.Security);
        Assert.Empty(operation.Security);
    }

    [Fact]
    public async Task Operation_transformer_clears_security_when_the_operation_is_not_authorized()
    {
        var operation = new OpenApiOperation();
        var transformer = new BearerSecuritySchemeTransformer();

        await transformer.TransformAsync(operation, CreateOperationContext(), CancellationToken.None);

        Assert.NotNull(operation.Security);
        Assert.Empty(operation.Security);
    }

    [Fact]
    public async Task Operation_transformer_leaves_security_when_the_operation_requires_authorization()
    {
        var operation = new OpenApiOperation();
        var transformer = new BearerSecuritySchemeTransformer();

        await transformer.TransformAsync(
            operation,
            CreateOperationContext(new AuthorizeAttribute()),
            CancellationToken.None);

        Assert.Null(operation.Security);
    }

    private static OpenApiDocumentTransformerContext CreateDocumentContext()
    {
        return new OpenApiDocumentTransformerContext
        {
            DocumentName = "v1",
            DescriptionGroups = [],
            ApplicationServices = new ServiceCollection().BuildServiceProvider()
        };
    }

    private static OpenApiOperationTransformerContext CreateOperationContext(params object[] metadata)
    {
        return new OpenApiOperationTransformerContext
        {
            DocumentName = "v1",
            Document = new OpenApiDocument(),
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
            Description = new ApiDescription
            {
                ActionDescriptor = new ControllerActionDescriptor
                {
                    EndpointMetadata = metadata
                }
            }
        };
    }
}

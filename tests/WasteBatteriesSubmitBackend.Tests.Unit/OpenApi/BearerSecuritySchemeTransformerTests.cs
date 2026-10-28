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
    public async Task TransformAsync_WhenDocumentComponentsAreMissing_AddsBearerScheme()
    {
        // Arrange
        var document = new OpenApiDocument();
        var transformer = new BearerSecuritySchemeTransformer();

        // Act
        await transformer.TransformAsync(document, CreateDocumentContext(), CancellationToken.None);

        // Assert
        Assert.Equal(BearerSecuritySchemeTransformer.DocumentTitle, document.Info.Title);
        Assert.Equal("v1", document.Info.Version);
        Assert.NotNull(document.Components);
        Assert.NotNull(document.Components.SecuritySchemes);
        Assert.True(document.Components.SecuritySchemes.ContainsKey(BearerSecuritySchemeTransformer.SchemeId));
        Assert.NotNull(document.Security);
        Assert.NotEmpty(document.Security);
    }

    [Fact]
    public async Task TransformAsync_WhenDocumentAlreadyHasComponents_KeepsExistingSecurity()
    {
        // Arrange
        var document = new OpenApiDocument
        {
            Components = new OpenApiComponents
            {
                SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>()
            },
            Security = []
        };
        var transformer = new BearerSecuritySchemeTransformer();

        // Act
        await transformer.TransformAsync(document, CreateDocumentContext(), CancellationToken.None);

        // Assert
        Assert.NotNull(document.Components.SecuritySchemes);
        Assert.True(document.Components.SecuritySchemes.ContainsKey(BearerSecuritySchemeTransformer.SchemeId));
        Assert.NotNull(document.Security);
        Assert.Single(document.Security);
    }

    [Fact]
    public async Task TransformAsync_WhenOperationIsAnonymous_ClearsSecurity()
    {
        // Arrange
        var operation = new OpenApiOperation
        {
            Security = [new OpenApiSecurityRequirement()]
        };
        var transformer = new BearerSecuritySchemeTransformer();

        // Act
        await transformer.TransformAsync(
            operation,
            CreateOperationContext(new AllowAnonymousAttribute()),
            CancellationToken.None);

        // Assert
        Assert.NotNull(operation.Security);
        Assert.Empty(operation.Security);
    }

    [Fact]
    public async Task TransformAsync_WhenOperationIsNotAuthorized_ClearsSecurity()
    {
        // Arrange
        var operation = new OpenApiOperation();
        var transformer = new BearerSecuritySchemeTransformer();

        // Act
        await transformer.TransformAsync(operation, CreateOperationContext(), CancellationToken.None);

        // Assert
        Assert.NotNull(operation.Security);
        Assert.Empty(operation.Security);
    }

    [Fact]
    public async Task TransformAsync_WhenOperationRequiresAuthorization_LeavesSecurityUnset()
    {
        // Arrange
        var operation = new OpenApiOperation();
        var transformer = new BearerSecuritySchemeTransformer();

        // Act
        await transformer.TransformAsync(
            operation,
            CreateOperationContext(new AuthorizeAttribute()),
            CancellationToken.None);

        // Assert
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

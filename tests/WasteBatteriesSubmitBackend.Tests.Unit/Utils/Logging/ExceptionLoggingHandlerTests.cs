using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Logging;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Utils.Logging;

public class ExceptionLoggingHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_WhenExceptionIsUnhandled_LogsErrorAndReturnsProblemDetails()
    {
        // Arrange
        var logger = new CapturingLogger<ExceptionLoggingHandler>();
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        ProblemDetailsContext? captured = null;
        problemDetailsService
            .TryWriteAsync(Arg.Do<ProblemDetailsContext>(context => captured = context))
            .Returns(new ValueTask<bool>(true));
        var handler = new ExceptionLoggingHandler(logger, problemDetailsService);
        var exception = new InvalidOperationException("secret boom");
        var httpContext = new DefaultHttpContext();

        // Act
        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        Assert.NotNull(captured);
        Assert.Equal(StatusCodes.Status500InternalServerError, captured.ProblemDetails.Status);
        Assert.Equal("An error occurred while processing your request.", captured.ProblemDetails.Title);
        Assert.Null(captured.ProblemDetails.Detail);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(1, entry.EventId.Id);
        Assert.Same(exception, entry.Exception);
    }

    [Fact]
    public async Task TryHandleAsync_WhenRequestIsBad_ReturnsBadRequestProblemDetails()
    {
        // Arrange
        var logger = new CapturingLogger<ExceptionLoggingHandler>();
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        ProblemDetailsContext? captured = null;
        problemDetailsService
            .TryWriteAsync(Arg.Do<ProblemDetailsContext>(context => captured = context))
            .Returns(new ValueTask<bool>(true));
        var handler = new ExceptionLoggingHandler(logger, problemDetailsService);
        var httpContext = new DefaultHttpContext();

        // Act
        await handler.TryHandleAsync(httpContext, new BadHttpRequestException("Malformed JSON."), CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.NotNull(captured);
        Assert.Equal(StatusCodes.Status400BadRequest, captured.ProblemDetails.Status);
        Assert.Equal("Bad request", captured.ProblemDetails.Title);
        Assert.Equal("Malformed JSON.", captured.ProblemDetails.Detail);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
    }

    [Fact]
    public async Task TryHandleAsync_WhenRequestIsInvalid_LogsInformationAndReturnsValidationProblem()
    {
        // Arrange
        var logger = new CapturingLogger<ExceptionLoggingHandler>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        var handler = new ExceptionLoggingHandler(logger, services.BuildServiceProvider().GetRequiredService<IProblemDetailsService>());
        var body = new MemoryStream();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = body }
        };
        var exception = new ValidationException(
        [
            new ValidationFailure("FieldA", "FieldA is the wrong format."),
            new ValidationFailure("FieldA", "FieldA is too long.")
        ]);

        // Act
        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: TestContext.Current.CancellationToken);
        var messages = json.RootElement.GetProperty("errors").GetProperty("FieldA").EnumerateArray()
            .Select(message => message.GetString())
            .ToArray();
        Assert.Equal(["FieldA is the wrong format.", "FieldA is too long."], messages);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(3, entry.EventId.Id);
        Assert.Null(entry.Exception);
        Assert.Contains("FieldA: FieldA is the wrong format.", entry.Message, StringComparison.Ordinal);
        Assert.Contains("FieldA: FieldA is too long.", entry.Message, StringComparison.Ordinal);
    }
}

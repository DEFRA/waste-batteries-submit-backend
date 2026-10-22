using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
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
        var (level, logged) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.Same(exception, logged);
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
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception));
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;
using WasteBatteriesSubmitBackend.Utils.Correlation;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Utils.Correlation;

public class CorrelationTests
{
    private const string HeaderName = "x-cdp-request-id";

    [Fact]
    public async Task Invoke_WhenTheHeaderIsPresent_KeepsItForLogsAndTheContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = "trace-from-caller";
        string? logged = null;
        var middleware = Middleware(_ =>
        {
            logged = LoggedCorrelationId();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal("trace-from-caller", context.Request.Headers[HeaderName].ToString());
        Assert.Equal("trace-from-caller", context.Items[CorrelationIds.ItemKey]);
        Assert.Equal("trace-from-caller", logged);
        Assert.Equal("trace-from-caller", ContextFor(context).CorrelationId);
    }

    [Fact]
    public async Task Invoke_WhenTheHeaderIsMissing_UsesTheActivityTraceId()
    {
        using var activity = new Activity("correlation-test").Start();
        var context = new DefaultHttpContext();
        var middleware = Middleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var traceId = activity.TraceId.ToHexString();
        Assert.Equal(traceId, context.Request.Headers[HeaderName].ToString());
        Assert.Equal(traceId, context.Items[CorrelationIds.ItemKey]);
        Assert.Equal(traceId, ContextFor(context).CorrelationId);
    }

    [Fact]
    public async Task Invoke_WhenTheHeaderIsBlank_ReplacesItWithTheActivityTraceId()
    {
        using var activity = new Activity("correlation-test").Start();
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = "   ";
        var middleware = Middleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal(activity.TraceId.ToHexString(), context.Request.Headers[HeaderName].ToString());
    }

    [Fact]
    public void Establish_WhenThereIsNoHeaderOrActivity_CreatesOneStableId()
    {
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            var context = new DefaultHttpContext();

            var first = CorrelationIds.Establish(context, HeaderName);
            var second = CorrelationIds.Establish(context, HeaderName);

            Assert.Equal(first, second);
            Assert.Matches("^[0-9a-f]{32}$", first);
            Assert.NotEqual("00000000000000000000000000000000", first);
            Assert.Equal(first, context.Request.Headers[HeaderName].ToString());
            Assert.NotEqual(first, CorrelationIds.Establish(new DefaultHttpContext(), HeaderName));
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Fact]
    public void CorrelationContext_WhenThereIsNoRequest_CreatesOneStableId()
    {
        var correlation = new CorrelationContext(new HttpContextAccessor(), Configuration(HeaderName));

        Assert.Equal(correlation.CorrelationId, correlation.CorrelationId);
        Assert.Matches("^[0-9a-f]{32}$", correlation.CorrelationId);
        Assert.NotEqual(
            correlation.CorrelationId,
            new CorrelationContext(new HttpContextAccessor(), Configuration(HeaderName)).CorrelationId);
    }

    [Fact]
    public void Establish_WhenTheHeaderNameIsNotConfigured_StillStoresAnId()
    {
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            var context = new DefaultHttpContext();

            var correlationId = CorrelationIds.Establish(context, headerName: null);

            Assert.Matches("^[0-9a-f]{32}$", correlationId);
            Assert.Equal(correlationId, context.Items[CorrelationIds.ItemKey]);
            Assert.Empty(context.Request.Headers);
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    private static CorrelationMiddleware Middleware(RequestDelegate next) =>
        new(next, Configuration(HeaderName));

    private static CorrelationContext ContextFor(HttpContext context) =>
        new(new HttpContextAccessor { HttpContext = context }, Configuration(HeaderName));

    private static IConfiguration Configuration(string? headerName) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TraceHeader"] = headerName })
            .Build();

    private static string LoggedCorrelationId()
    {
        string? logged = null;
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new CaptureSink(logEvent =>
            {
                logged = logEvent.Properties[CorrelationIds.ItemKey].ToString().Trim('"');
            }))
            .CreateLogger();
        logger.Information("during request");
        Assert.NotNull(logged);
        return logged;
    }

    private sealed class CaptureSink(Action<LogEvent> write) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => write(logEvent);
    }
}

using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WasteBatteriesSubmitBackend.Utils.Logging;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Utils.Logging;

public class ProblemDetailsTraceIdTests
{
    [Fact]
    public void Select_WhenRequestTraceIdIsPresent_UsesRequestTraceId()
    {
        var traceId = ProblemDetailsTraceId.Select(
            "trace-mismatch-check-001",
            "e9553a6557614e09143838699e68851a",
            "request-7");

        Assert.Equal("trace-mismatch-check-001", traceId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Select_WhenRequestTraceIdIsBlank_UsesActivityTraceId(string? requestTraceId)
    {
        var traceId = ProblemDetailsTraceId.Select(
            requestTraceId,
            "e9553a6557614e09143838699e68851a",
            "request-7");

        Assert.Equal("e9553a6557614e09143838699e68851a", traceId);
    }

    [Fact]
    public void Select_WhenRequestAndActivityTraceIdsAreBlank_UsesRequestIdentifier()
    {
        var traceId = ProblemDetailsTraceId.Select(null, null, "request-7");

        Assert.Equal("request-7", traceId);
    }

    [Fact]
    public void Apply_WhenTraceHeaderIsPresent_UsesHeader()
    {
        using var activity = new Activity("test").Start();
        var context = Context();
        context.HttpContext.Request.Headers["x-cdp-request-id"] = "trace-mismatch-check-001";

        ProblemDetailsTraceId.Apply(context, "x-cdp-request-id");

        Assert.Equal("trace-mismatch-check-001", context.ProblemDetails.Extensions["traceId"]);
        Assert.NotEqual(activity.TraceId.ToHexString(), context.ProblemDetails.Extensions["traceId"]);
    }

    [Fact]
    public void Apply_WhenTraceHeaderIsAbsent_UsesActivityTraceId()
    {
        using var activity = new Activity("test").Start();
        var context = Context();

        ProblemDetailsTraceId.Apply(context, "x-cdp-request-id");

        Assert.Equal(activity.TraceId.ToHexString(), context.ProblemDetails.Extensions["traceId"]);
    }

    [Fact]
    public void Apply_WhenTraceHeaderIsBlank_UsesActivityTraceId()
    {
        using var activity = new Activity("test").Start();
        var context = Context();
        context.HttpContext.Request.Headers["x-cdp-request-id"] = "   ";

        ProblemDetailsTraceId.Apply(context, "x-cdp-request-id");

        Assert.Equal(activity.TraceId.ToHexString(), context.ProblemDetails.Extensions["traceId"]);
    }

    [Fact]
    public void Apply_WhenHeaderNameIsNotConfigured_IgnoresHeader()
    {
        using var activity = new Activity("test").Start();
        var context = Context();
        context.HttpContext.Request.Headers["x-cdp-request-id"] = "trace-mismatch-check-001";

        ProblemDetailsTraceId.Apply(context, null);

        Assert.Equal(activity.TraceId.ToHexString(), context.ProblemDetails.Extensions["traceId"]);
    }

    private static ProblemDetailsContext Context()
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "request-7"
        };
        return new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails()
        };
    }
}

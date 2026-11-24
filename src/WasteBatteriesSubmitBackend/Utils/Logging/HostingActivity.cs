using System.Diagnostics;

namespace WasteBatteriesSubmitBackend.Utils.Logging;

// The host registers its ActivitySource as Microsoft.AspNetCore. With a listener it
// starts a W3C request activity, which is the trace id problem details and the logs share.
// With no listener it uses new Activity(), and that keeps a hierarchical parent when one
// is current. A hierarchical activity has no W3C trace id, so the response falls back to
// the connection id.
public static class HostingActivity
{
    private static int s_listening;

    public static void Listen()
    {
        if (Interlocked.Exchange(ref s_listening, 1) == 1)
        {
            return;
        }

        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = static source => source.Name == "Microsoft.AspNetCore",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded
        });
    }
}

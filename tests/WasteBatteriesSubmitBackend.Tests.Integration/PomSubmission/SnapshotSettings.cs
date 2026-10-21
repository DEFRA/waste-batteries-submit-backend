using System.Runtime.CompilerServices;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

internal static class SnapshotSettings
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("DiffEngine_Disabled", "true");
    }

    public static VerifySettings Create()
    {
        var settings = new VerifySettings();
        settings.UseDirectory(Path.Combine(ProjectDirectory(), "Data", "Snapshots"));
        settings.DontScrubDateTimes();
        return settings;
    }

    private static string ProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        return directory.Parent?.Parent?.Parent?.FullName
            ?? throw new InvalidOperationException("Could not find the integration test project directory.");
    }
}

using Microsoft.Extensions.Configuration;
using WasteBatteriesSubmitBackend.Config;

namespace WasteBatteriesSubmitBackend.Tests.Common;

// The API appsettings.json. Walking up from the test output finds the repo root, so a moved
// test file or a different bin layout still resolves the same file.
public static class AppSettings
{
    private static readonly Lazy<PomSubmissionConfig> s_pomSubmission = new(LoadPomSubmission);

    public static PomSubmissionConfig PomSubmission => s_pomSubmission.Value;

    private static PomSubmissionConfig LoadPomSubmission()
    {
        var path = FindAppSettings();
        var section = new ConfigurationBuilder()
            .AddJsonFile(path, optional: false)
            .Build()
            .GetSection("PomSubmission");

        return new PomSubmissionConfig
        {
            InitialStatus = Text(section, "InitialStatus"),
            InitialVersion = Number(section, "InitialVersion"),
            InitialActivity = Text(section, "InitialActivity"),
            MaxPageSize = Number(section, "MaxPageSize"),
            MaxGzipPageSize = Number(section, "MaxGzipPageSize")
        };
    }

    private static string FindAppSettings()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "WasteBatteriesSubmitBackend", "appsettings.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find src/WasteBatteriesSubmitBackend/appsettings.json above {AppContext.BaseDirectory}.");
    }

    private static string Text(IConfiguration section, string name) =>
        section[name] ?? throw new InvalidOperationException($"PomSubmission:{name} is missing from appsettings.json.");

    private static int Number(IConfiguration section, string name)
    {
        var text = Text(section, name);
        return int.TryParse(text, out var value)
            ? value
            : throw new InvalidOperationException($"PomSubmission:{name} is not an integer in appsettings.json.");
    }
}

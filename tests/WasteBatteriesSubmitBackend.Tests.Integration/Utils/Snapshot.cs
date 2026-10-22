using System.Runtime.CompilerServices;

namespace WasteBatteriesSubmitBackend.Tests.Integration.Utils;

internal static class Snapshot
{
    // One file per test method. Pass name for a theory case or a second snapshot in the same method:
    // Data/Snapshots/{TestFile}.{Method}.{name}.verified.txt
    public static void Match(
        string actual,
        string? name = null,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string method = "")
    {
        if (name is not null && !IsFileName(name))
        {
            throw new ArgumentException(
                "Snapshot name must be a single file name with no path separators.",
                nameof(name));
        }

        var snapshotName = name is null
            ? $"{Path.GetFileNameWithoutExtension(sourceFile)}.{method}"
            : $"{Path.GetFileNameWithoutExtension(sourceFile)}.{method}.{name}";
        var directory = Path.Combine(ProjectDirectory(sourceFile), "Data", "Snapshots");
        var verifiedPath = Path.Combine(directory, $"{snapshotName}.verified.txt");
        var receivedPath = Path.Combine(directory, $"{snapshotName}.received.txt");
        var normalized = Normalize(actual);

        if (!File.Exists(verifiedPath))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(receivedPath, normalized);
            Assert.Fail($"Snapshot not found: {verifiedPath}. Wrote {receivedPath}.");
        }

        var expected = Normalize(File.ReadAllText(verifiedPath));
        if (expected == normalized)
        {
            if (File.Exists(receivedPath))
            {
                File.Delete(receivedPath);
            }

            return;
        }

        File.WriteAllText(receivedPath, normalized);
        Assert.Equal(expected, normalized);
    }

    private static bool IsFileName(string name) =>
        name.Length > 0
        && !name.Contains("..", StringComparison.Ordinal)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    private static string ProjectDirectory(string sourceFile)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.csproj").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find a test project directory above {sourceFile}.");
    }
}

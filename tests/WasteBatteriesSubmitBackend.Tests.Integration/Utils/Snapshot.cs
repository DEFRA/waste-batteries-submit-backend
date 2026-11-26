using System.Runtime.CompilerServices;

namespace WasteBatteriesSubmitBackend.Tests.Integration.Utils;

//This is a class to allow us to use snapshots in the tests and compare a data file (e.g. a .txt) to a test repsonse (e.g. some .json).  
//We previously used Verify.Xunit for snapshots, but due to a package license being needed going forward this manual solution is better
internal static class Snapshot
{
    // One file per test method. Pass name for a theory case or a second snapshot in the same method:
    // Data/Snapshots/{TestFile}.{Method}.{name}.verified.txt
    // MatchAll writes every missing or different file before it fails, so one test can own several responses.
    public static void Match(
        string actual,
        string? name = null,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string method = "")
    {
        if (!Differs(actual, name, sourceFile, method, out var expected, out var normalized, out var missingMessage))
        {
            return;
        }

        if (missingMessage is not null)
        {
            Assert.Fail(missingMessage);
        }

        Assert.Equal(expected, normalized);
    }

    public static void MatchAll(
        (string Name, string Actual)[] snapshots,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string method = "")
    {
        string? firstMissing = null;
        (string Expected, string Actual)? firstDifference = null;
        foreach (var (name, actual) in snapshots)
        {
            if (!Differs(actual, name, sourceFile, method, out var expected, out var normalized, out var missingMessage))
            {
                continue;
            }

            if (missingMessage is not null)
            {
                firstMissing ??= missingMessage;
            }
            else
            {
                firstDifference ??= (expected, normalized);
            }
        }

        if (firstMissing is not null)
        {
            Assert.Fail(firstMissing);
        }

        if (firstDifference is not null)
        {
            Assert.Equal(firstDifference.Value.Expected, firstDifference.Value.Actual);
        }
    }

    // True when the verified file is missing or different. A received file is written for each of those.
    private static bool Differs(
        string actual,
        string? name,
        string sourceFile,
        string method,
        out string expected,
        out string normalized,
        out string? missingMessage)
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
        normalized = Normalize(actual);
        expected = "";
        missingMessage = null;

        if (!File.Exists(verifiedPath))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(receivedPath, normalized);
            missingMessage = $"Snapshot not found: {verifiedPath}. Wrote {receivedPath}.";
            return true;
        }

        expected = Normalize(File.ReadAllText(verifiedPath));
        if (expected == normalized)
        {
            if (File.Exists(receivedPath))
            {
                File.Delete(receivedPath);
            }

            return false;
        }

        File.WriteAllText(receivedPath, normalized);
        return true;
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
